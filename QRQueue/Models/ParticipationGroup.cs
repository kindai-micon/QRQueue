using System.ComponentModel.DataAnnotations.Schema;

namespace QRQueue.Models
{
    /// <summary>
    /// 呼び出しの最小単位となる参加グループ(設計書)
    /// </summary>
    public class ParticipationGroup : BaseModel
    {
        public Guid DisplayId { get; set; } = Guid.CreateVersion7();
        // 整理券番号(キュー載せ時に採番、未採番=0)
        public long Number { get; set; }
        [ForeignKey(nameof(Event))]
        public Guid EventId { get; set; }
        public Event Event { get; set; }
        public GroupType Type { get; set; }
        // 方式③のみ。代表者が共有する招待トークン
        public string? JoinToken { get; set; }
        public GroupStatus Status { get; set; } = GroupStatus.Waiting;
        // 他グループとの同時参加(マッチング)を許可するか(issue #66、#72)。
        // 3人に達した場合は自動で false になり変更できない。
        public bool AllowCoJoin { get; set; } = true;
        // 同じゲーム参加枠に割り当てられた識別子(issue #69)。
        // 呼び出し時に、マッチングで組み合わされたグループ群に共通の値が設定される。
        public Guid? GameSlotId { get; set; }
        // 最後の呼び出し時刻
        public DateTimeOffset? CalledAt { get; set; }
        // 再呼び出し回数
        public int CallCount { get; set; } = 0;
        public List<Ticket> Tickets { get; set; } = new();

        [NotMapped]
        public bool IsFull => Tickets.Count(t => t.Status != TicketStatus.Cancelled) >= 3;
    }

    public enum GroupType
    {
        Solo,        // 方式①: 1人固定
        AutoMatched, // 方式②: システム側マッチング
        Manual,      // 方式③: 代表者による手動グループ
    }

    public enum GroupStatus
    {
        Matching,    // 方式②: プール内でメンバー待ち(キュー外・番号未採番)
        Waiting,     // 呼び出し待ち(正常キュー内)
        Calling,     // 呼び出し中(チェックイン待ち)
        Interrupted, // 割り込みpool: 「次を呼ぶ」で未チェックインのまま退避された
        Completed,   // チェックイン済み(受け渡し完了)
        Cancelled,   // 上書き・キャンセルにより無効
        Draft,       // 受付確定前: 代表者が「受付」を確定するまで呼び出し対象にならない(issue #66)
    }
}
