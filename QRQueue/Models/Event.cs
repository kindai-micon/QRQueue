using System.ComponentModel.DataAnnotations.Schema;

namespace QRQueue.Models
{
    public class Event : BaseModel
    {
        public Guid DisplayId { get; set; } = Guid.CreateVersion7();

        public string Name { get; set; }
        public List<ParticipationGroup> Groups { get; set; } = new();
        // イベント運用状態(受付前/受付中/受付終了)
        public EventStatus Status { get; set; } = EventStatus.Preparing;
        // 方式②のマッチング人数(上限3)
        public int AutoGroupSize { get; set; } = 3;
        [ForeignKey(nameof(TicketInfo))]
        public Guid TicketInfoId { get; set; }
        public TicketInfo TicketInfo { get; set; }
    }
    public enum EventStatus
    {
        Preparing,
        Open,
        Closed
    }
}
