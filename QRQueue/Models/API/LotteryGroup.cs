using System.ComponentModel.DataAnnotations.Schema;

namespace QRQueue.Models.API
{
    public class LotteryGroup
    {
        public string Name { get; set; }
        public Guid TicketInfoId { get; set; }
        public TicketInfo TicketInfo { get; set; }
    }
}
