using System.ComponentModel.DataAnnotations.Schema;

namespace QRQueue.Models
{
    public class IssueLog : BaseModel
    {
        // BaseModel が Guid Id, Created, Updated を提供
        public string IssuerName { get; set; } = string.Empty;
        public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
        public int Count { get; set; }
        public long StartNumber { get; set; }
        public long EndNumber { get; set; }
        // イベントID(DisplayId)でログを紐づけ
        public Guid EventDisplayId { get; set; }
    }
}
