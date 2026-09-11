using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Repositories;

namespace QRQueue.Services;

/// <summary>
/// 呼び出しの状態遷移ルール(設計§4.6、issue #82で明文化):
/// - Waiting → Calling: 「次を呼ぶ」(スタッフ操作/チェックイン完了後のAutoNext)でのみ遷移する。
/// - Calling → Completed: 代表者のチェックインでのみ遷移する。
/// - Calling → Interrupted: 次の呼び出し時に未チェックインのまま退避される。
/// - Interrupted → Completed: 代表者がそろった時点でチェックインし、次の呼び出しに割り込んで処理される。
/// - これらの遷移はすべて「イベント単位の排他制御」(PostgreSQL アドバイザリロック+トランザクション)
///   の下で原子的に確定されるため、同時実行でも状態が一意に定まる。
/// </summary>
public interface IQueueCallService
{
    /// <summary>
    /// 「次を呼ぶ」(設計§4.6)。
    /// ① 現在呼び出し中(Calling)のグループを割り込みpool(Interrupted)へ退避し、
    /// ② 呼び出し先を優先順位どおり決定(Waiting先頭 → 方式②プール自動確定)して Calling へ移す。
    /// 呼び出せるグループがなければ null を返す。
    /// SignalR(Called/QueueChanged)と Web Push の通知もここで行う。
    /// </summary>
    Task<ParticipationGroup?> CallNextAsync(Event ev);

    /// <summary>
    /// 優先待機(Interrupted/割り込みプール)のグループをスタッフが直接呼び出す。
    /// 状態を Calling へ移し(新しいゲーム参加枠を付与)、SignalR/Web Push/LINE の告知を行う。
    /// 対象が優先待機でない場合は null を返す。
    /// </summary>
    Task<ParticipationGroup?> CallInterruptedGroupAsync(Event ev, Guid groupDisplayId);

    // ※「再呼び出し(CallAgain)」は管理向け(CallController)側の責務のため、ここでは提供しない(§6.2)

    /// <summary>
    /// 方式②のグループ成立(設計§4.2)。プールの参加順先頭 memberCount 人で1グループを成立させ、
    /// 採番して Waiting へ載せる。満員成立(join側)と自動確定(next側)の共用。
    /// プールが空なら null。
    /// </summary>
    Task<ParticipationGroup?> FormGroupFromMatchingPoolAsync(Event ev, int memberCount);

    /// <summary>呼び出しの通知: SignalR(Called/QueueChanged) + 対象グループ全員へ Web Push(設計§7)</summary>
    Task AnnounceAsync(Event ev, ParticipationGroup group);

    /// <summary>
    /// 未到着による遅延が一定時間(QueueCall:SlotTimeoutMinutes、既定5分)を超えた
    /// ゲーム参加枠の未到着グループを優先待機(Interrupted)へ退避する(issue #69)。
    /// </summary>
    Task EvacuateExpiredSlotGroupsAsync(Event ev);

    /// 代表者のチェックイン完了を原子的に確定する(issue #82)。
    /// グループ状態の更新(→Completed)と、それに続く再告知/次の呼び出しを
    /// イベント単位の排他制御下で一括処理する。
    /// グループが呼び出されていない等で確定できない場合は null を返す。
    /// 既に Completed の場合はそのまま返す(冪等)。
    /// </summary>
    Task<ParticipationGroup?> CompleteCheckinAsync(Event ev, Guid groupId);

    /// <summary>
    /// 同一イベントに対する更新処理を直列化して実行する(issue #82)。
    /// 再呼び出し(CallAgain)など、状態を更新する管理操作から利用する。
    /// </summary>
    Task<T> RunExclusiveAsync<T>(Event ev, Func<Task<T>> operation);
}

public class QueueCallService(
    IParticipationGroupRepository groupRepository,
    IGroupNumberIssuanceService groupNumberIssuanceService,
    IPushSubscriptionService pushSubscriptionService,
    ILineService lineService,
    IHubContext<QueueHub> hubContext,
    IConfiguration configuration,
    ApplicationDbContext db) : IQueueCallService
{
    // ===== イベント単位の排他制御(issue #82) =====

    /// <summary>アドバイザリロック用のキーに変換する</summary>
    private static long ToLockKey(Guid eventId)
    {
        return BitConverter.ToInt64(eventId.ToByteArray(), 0);
    }

    /// <summary>
    /// イベント単位の排他制御: PostgreSQL アドバイザリロック(pg_advisory_xact_lock)を取得してから
    /// 処理を実行し、一連の読み取り・更新・保存を一つのトランザクションで確定する。
    /// 同じイベントに対する呼び出し・チェックイン・再呼び出しが同時に実行されても、
    /// 状態の読み取り→更新が直列化されるため、不整合が発生しない。
    /// </summary>
    private async Task<T> InEventLockAsync<T>(Event ev, Func<Task<T>> operation)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ToLockKey(ev.DisplayId)})");
            var result = await operation();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public Task<T> RunExclusiveAsync<T>(Event ev, Func<Task<T>> operation)
    {
        return InEventLockAsync(ev, operation);
    }

    public Task<ParticipationGroup?> CallNextAsync(Event ev)
    {
        return InEventLockAsync(ev, () => CallNextCoreAsync(ev));
    }

    /// <summary>
    /// 優先待機(Interrupted/割り込みプール)のグループをスタッフが直接呼び出す。
    /// 新しいゲーム参加枠(GameSlotId)を付与して Calling へ移し、CallNext と同様の告知を行う。
    /// これにより「代表者のチェックインを待たずに」優先プールのグループを任意のタイミングで呼び出せる。
    /// </summary>
    public Task<ParticipationGroup?> CallInterruptedGroupAsync(Event ev, Guid groupDisplayId)
    {
        return InEventLockAsync(ev, async () =>
        {
            var group = await groupRepository.FindByDisplayIdAsync(groupDisplayId);
            if (group == null)
            {
                return null;
            }
            // ロック取得後に DB の最新値を再読み込みする(CompleteCheckinAsync と同じ理由)
            await db.Entry(group).ReloadAsync();
            if (group.Status != GroupStatus.Interrupted)
            {
                // 優先待機以外(呼び出し中/待機中/完了等)は対象外
                return null;
            }

            group.Status = GroupStatus.Calling;
            group.CalledAt = DateTimeOffset.UtcNow;
            group.GameSlotId = Guid.CreateVersion7();
            await groupRepository.SaveChangesAsync();

            await AnnounceAsync(ev, group);
            return group;
        });
    }

    /// <summary>CallNextAsync の本体(イベントロック内で実行される)</summary>
    private async Task<ParticipationGroup?> CallNextCoreAsync(Event ev)
    {
        // ① 現在呼び出し中で未チェックインのグループを割り込みpoolへ退避
        //    (チェックイン済みグループは参加者のチェックイン時点で Completed になっているため、
        //     Calling にあるものはすべて未チェックイン)
        var calling = await groupRepository.GetCallingAsync(ev.Id);
        foreach (var group in calling)
        {
            group.Status = GroupStatus.Interrupted;
        }
        if (calling.Count > 0)
        {
            await groupRepository.SaveChangesAsync();
            await NotifyQueueChangedAsync(ev);
        }

        // ② 呼び出し先の決定: 正常キュー(Waiting)の先頭グループを基準にマッチング(issue #72)
        //    → 条件に合わなければ方式②プールの自動確定
        var waiting = await groupRepository.GetWaitingAsync(ev.Id);
        List<ParticipationGroup>? selected = null;
        if (waiting.Count > 0)
        {
            selected = SelectMatchingGroups(waiting);
        }
        if (selected == null || selected.Count == 0)
        {
            var formed = await FormGroupFromMatchingPoolAsync(ev, ev.AutoGroupSize);
            if (formed == null)
            {
                return null;
            }
            selected = new List<ParticipationGroup> { formed };
        }

        // ③ 選択された組み合わせ(=同じゲーム参加枠)をまとめて Calling へ移す。
        //    元のグループ・代表者・グループ番号は維持される(マッチングでグループを併合しない)。
        //    同時に呼び出されたグループ群には共通の GameSlotId を付与する(issue #69)
        var slotId = Guid.CreateVersion7();
        foreach (var group in selected)
        {
            group.Status = GroupStatus.Calling;
            group.CalledAt = DateTimeOffset.UtcNow;
            group.GameSlotId = slotId;
        }
        await groupRepository.SaveChangesAsync();

        foreach (var group in selected)
        {
            await AnnounceAsync(ev, group);
        }
        return selected[0];
    }

    /// <summary>
    /// 未到着による遅延が一定時間を超えたゲーム参加枠を処理する(issue #69)。
    /// 枠内の一部グループのみ到着(代表者チェックイン=Completed)していて、未到着(Calling)の
    /// グループが timeout を超過している場合、未到着グループを割り込みpool(Interrupted/優先待機)へ
    /// 退避する。到着済みグループだけでゲームを進められるようにするための処理。
    /// 到着済みグループは Completed(受付完了)のまま維持される。
    /// 遅れて到着した未到着グループは、代表者のチェックインで従来どおり
    /// 割り込み優先で処理される(Interrupted → Completed)。
    /// スタッフのキュー表示(モニタリング)のタイミングで評価される。
    /// </summary>
    public async Task EvacuateExpiredSlotGroupsAsync(Event ev)
    {
        var timeoutMinutes = configuration.GetValue<int?>("QueueCall:SlotTimeoutMinutes") ?? 5;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(-timeoutMinutes);

        var activeGroups = await groupRepository.GetCallingAsync(ev.Id);

        // 枠ごとに到着状況を評価: 同一 GameSlotId 内に「到着済み(Completed)」が存在する場合のみ
        // タイムアウト判定を行う(誰も到着していない枠はまだ待つ)
        var groupsBySlot = activeGroups
            .Where(g => g.GameSlotId != null)
            .GroupBy(g => g.GameSlotId!.Value)
            .ToList();

        foreach (var slot in groupsBySlot)
        {
            var hasArrived = await groupRepository.HasArrivedGroupAsync(ev.Id, slot.Key);
            if (!hasArrived)
            {
                continue;
            }
            foreach (var group in slot.Where(g => g.CalledAt != null && g.CalledAt < deadline))
            {
                group.Status = GroupStatus.Interrupted;
            }
        }

        await groupRepository.SaveChangesAsync();
    }

    /// <summary>
    /// 待機キューの先頭グループを基準に、同時参加許可に基づいて同じゲーム参加枠へ
    /// 入れるグループの組み合わせを選択する(issue #72)。
    /// - 先頭が3人: 単独で呼び出す
    /// - 先頭が同時参加を許可していない: 単独で呼び出す
    /// - 先頭が2人で許可している: 許可している1人グループを先頭に近い順で探索して1組組み合わせる
    /// - 先頭が1人で許可している: まず許可している2人グループを探索し、
    ///   なければ許可している1人グループを最大2組探索する
    /// - マッチングは双方が許可している場合のみ行い、候補は待機キューの先頭に近いものを優先する
    /// - 相手がいない場合は、先頭グループを現在の人数のまま呼び出す
    /// </summary>
    private List<ParticipationGroup>? SelectMatchingGroups(List<ParticipationGroup> waiting)
    {
        var head = waiting[0];
        var headCount = ActiveMemberCount(head);

        if (headCount == 0)
        {
            // 有効メンバーが存在しないグループはスキップできないため単独扱いとする
            return new List<ParticipationGroup> { head };
        }

        var selected = new List<ParticipationGroup> { head };

        // 3人グループ、または同時参加を許可していないグループは単独で呼び出す
        if (headCount >= 3 || !head.AllowCoJoin)
        {
            return selected;
        }

        // 先頭に近い順の候補(双方が同時参加を許可しているグループのみ)
        var candidates = waiting.Skip(1)
            .Where(g => g.AllowCoJoin)
            .ToList();

        if (headCount == 2)
        {
            // 2人 + 1人の組み合わせ
            var partner = candidates.FirstOrDefault(g => ActiveMemberCount(g) == 1);
            if (partner != null)
            {
                selected.Add(partner);
            }
            return selected;
        }

        // 先頭が1人: まず2人グループを探し、なければ1人グループを最大2組
        var partner2 = candidates.FirstOrDefault(g => ActiveMemberCount(g) == 2);
        if (partner2 != null)
        {
            selected.Add(partner2);
            return selected;
        }
        var singles = candidates.Where(g => ActiveMemberCount(g) == 1).Take(2).ToList();
        selected.AddRange(singles);
        return selected;
    }

    /// <summary>グループの有効(未キャンセル)メンバー数</summary>
    private static int ActiveMemberCount(ParticipationGroup group)
    {
        return group.Tickets.Count(t => t.Status != TicketStatus.Cancelled);
    }

    public Task<ParticipationGroup?> CompleteCheckinAsync(Event ev, Guid groupId)
    {
        return InEventLockAsync(ev, async () =>
        {
            var group = await groupRepository.FindByIdAsync(groupId);
            if (group == null)
            {
                return null;
            }

            // ロック取得後に DB の最新値を再読み込みする(issue #82)。
            // コントローラが同一スコープの DbContext で当該グループをロード済みのため、
            // 単純な再クエリでは identity resolution により追跡済み(コミット前)の
            // インスタンスが返る。明示的に Reload してから状態を確定的に判定する。
            await db.Entry(group).ReloadAsync();
            if (group.Status is GroupStatus.Matching or GroupStatus.Waiting or GroupStatus.Cancelled)
            {
                // 呼び出されていない/無効なグループ(呼び出し側で案内を返す)
                return null;
            }
            if (group.Status == GroupStatus.Completed)
            {
                // 冪等: 多重チェックインでも状態は変化しない
                return group;
            }

            var wasInterrupted = group.Status == GroupStatus.Interrupted;
            group.Status = GroupStatus.Completed;
            await groupRepository.SaveChangesAsync();

            if (wasInterrupted)
            {
                // 割り込みpoolのグループ: そろった時点で完了扱いとし、次の呼び出しに割り込んで
                // 処理対象にする(§4.6 優先順位1。チェックイン時点での即時告知として実装)
                await AnnounceAsync(ev, group);
                return group;
            }

            // 正常キューから呼び出されていたグループのチェックイン完了をトリガーに AutoNext。
            // 同一ロック内で実行されるため、退避→呼び出しの過程で他の操作が介入しない。
            await CallNextCoreAsync(ev);
            return group;
        });
    }

    public async Task<ParticipationGroup?> FormGroupFromMatchingPoolAsync(Event ev, int memberCount)
    {
        var pool = await groupRepository.GetMatchingPoolAsync(ev.Id);
        if (pool.Count == 0)
        {
            return null;
        }

        // 成立人数はプールの残り人数と AutoGroupSize の上限で切り詰める
        memberCount = Math.Min(memberCount, pool.Count);
        var survivor = pool[0];
        foreach (var other in pool.Skip(1).Take(memberCount - 1))
        {
            // チケットの付け替え(DisplayId は変わらないため Push 購読も引き継がれる §4.4)
            foreach (var ticket in other.Tickets)
            {
                ticket.ParticipationGroupId = survivor.Id;
            }
            groupRepository.Remove(other);
        }
        survivor.Type = GroupType.AutoMatched;
        survivor.Status = GroupStatus.Waiting;

        // 採番(排他トランザクション内で呼ばれた場合はそのトランザクションに参加、
        // それ以外は IssueNumberAsync が自身の Serializable トランザクションを開始する)
        await groupNumberIssuanceService.IssueNumberAsync(survivor);
        await NotifyQueueChangedAsync(ev);
        return survivor;
    }

    public async Task AnnounceAsync(Event ev, ParticipationGroup group)
    {
        await NotifyQueueChangedAsync(ev);
        await hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("Called", new
        {
            groupNumber = group.Number,
            groupDisplayId = group.DisplayId.ToString()
        });
        await pushSubscriptionService.SendNotifyTicketGroupAsync(group.Tickets, $"{ev.Name}で順番になりました",$"順番になりましたのでブースまで来てください");
        // LINE連携済みのチケットには Messaging API でも通知する(Web Push と併用)
        await lineService.SendNotifyAsync(group.Tickets.Select(t => t.DisplayId).ToList(),
            $"{ev.Name}で順番になりました。ブースまで来てください");
    }

    private Task NotifyQueueChangedAsync(Event ev)
    {
        return hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("QueueChanged");
    }
}
