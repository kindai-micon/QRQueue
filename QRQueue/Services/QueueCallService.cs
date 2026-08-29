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

    /// <summary>
    /// 再呼び出し。現在 Calling のグループの CallCount++ と表示再強調(Push 再送)。
    /// 呼び出し中のグループがなければ null を返す。
    /// </summary>
    Task<ParticipationGroup?> CallAgainAsync(Event ev);

    /// <summary>
    /// 方式②のグループ成立(設計§4.2)。プールの参加順先頭 memberCount 人で1グループを成立させ、
    /// 採番して Waiting へ載せる。満員成立(join側)と自動確定(next側)の共用。
    /// プールが空なら null。
    /// </summary>
    Task<ParticipationGroup?> FormGroupFromMatchingPoolAsync(Event ev, int memberCount);

    /// <summary>呼び出しの通知: SignalR(Called/QueueChanged) + 対象グループ全員へ Web Push(設計§7)</summary>
    Task AnnounceAsync(Event ev, ParticipationGroup group);
}

public class QueueCallService(
    IParticipationGroupRepository groupRepository,
    IGroupNumberIssuanceService groupNumberIssuanceService,
    IPushSubscriptionService pushSubscriptionService,
    IHubContext<QueueHub> hubContext) : IQueueCallService
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

        // ② 呼び出し先の決定: 正常キュー(Waiting)の先頭 → 方式②プールの自動確定
        var target = (await groupRepository.GetWaitingAsync(ev.Id)).FirstOrDefault();
        if (target == null)
        {
            target = await FormGroupFromMatchingPoolAsync(ev, ev.AutoGroupSize);
        }
        if (target == null)
        {
            return null;
        }

        target.Status = GroupStatus.Calling;
        target.CalledAt = DateTimeOffset.UtcNow;
        await groupRepository.SaveChangesAsync();

        await AnnounceAsync(ev, target);
        return target;
    }

    public async Task<ParticipationGroup?> CallAgainAsync(Event ev)
    {
        var target = (await groupRepository.GetCallingAsync(ev.Id)).FirstOrDefault();
        if (target == null)
        {
            return null;
        }

        target.CallCount++;
        target.CalledAt = DateTimeOffset.UtcNow;
        await groupRepository.SaveChangesAsync();

        await AnnounceAsync(ev, target);
        return target;
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

        foreach (var ticket in group.Tickets.Where(t => t.Status != TicketStatus.Cancelled))
        {
            // 呼び出し通知に転用(設計§7)。master 側のメソッド名 SendLotteryPushAsync を使用
            await pushSubscriptionService.SendLotteryPushAsync(ticket);
        }
    }

    private Task NotifyQueueChangedAsync(Event ev)
    {
        return hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("QueueChanged");
    }
}
