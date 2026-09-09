using Microsoft.AspNetCore.SignalR;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Repositories;

namespace QRQueue.Services;

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
}

public class QueueCallService(
    IParticipationGroupRepository groupRepository,
    IGroupNumberIssuanceService groupNumberIssuanceService,
    IPushSubscriptionService pushSubscriptionService,
    IHubContext<QueueHub> hubContext,
    IConfiguration configuration) : IQueueCallService
{
    public async Task<ParticipationGroup?> CallNextAsync(Event ev)
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

        // 採番(Serializable トランザクション内で付け替えも一緒に保存される)
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
    }

    private Task NotifyQueueChangedAsync(Event ev)
    {
        return hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("QueueChanged");
    }
}
