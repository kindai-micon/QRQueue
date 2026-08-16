using System.ComponentModel.DataAnnotations.Schema;

namespace QRQueue.Models
{
    public class Ticket : BaseModel
    {
        public Ticket():base()
        {
            DisplayId = Guid.CreateVersion7();
        }
        // 廃止予定: 呼び出し番号は ParticipationGroup.Number へ移行
        public long Number { get; set; }
        // QR/URL/Push購読の鍵
        public Guid DisplayId { get; set; }
        [ForeignKey(nameof(ParticipationGroup))]
        public Guid? ParticipationGroupId { get; set; }
        public ParticipationGroup ParticipationGroup { get; set; }
        public TicketStatus Status { get; set; } = TicketStatus.Registered;
        // 匿名デバイス識別(重複登録検知)
        public Guid? ParticipantToken { get; set; }
    }
    public enum TicketStatus
    {
        Registered, // 参加登録済み
        Cancelled,  // 上書き・離脱により無効
    }
}
