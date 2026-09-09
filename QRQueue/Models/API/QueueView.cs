using QRQueue.Models;

namespace QRQueue.Models.API
{
    public class QueueView
    {
        public IEnumerable<ParticipationGroupView> WaitingGroup { get; set; }=new List<ParticipationGroupView>();
        public IEnumerable<ParticipationGroupView> CallingGroup { get; set; } = new List<ParticipationGroupView>();
        public IEnumerable<ParticipationGroupView> InterruptedGroup { get; set; } = new List<ParticipationGroupView>();
        public int PeoplePool { get; set; }
        // アクティブなゲーム参加枠(マッチングで組み合わされたグループ群ごとの到着状況)(issue #69)
        public IEnumerable<GameSlotView> Slots { get; set; } = new List<GameSlotView>();
    }

    /// <summary>ゲーム参加枠: 同時に呼び出されたグループ群(issue #69)</summary>
    public class GameSlotView
    {
        public string SlotId { get; set; } = "";
        public DateTimeOffset? CalledAt { get; set; }
        // 全グループの到着確認(代表者チェックイン)が完了しているか
        public bool AllArrived { get; set; }
        public IEnumerable<ParticipationGroupView> Groups { get; set; } = new List<ParticipationGroupView>();
    }
}
