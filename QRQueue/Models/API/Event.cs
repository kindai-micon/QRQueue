namespace QRQueue.Models.API
{
    public class Event
    {
        public string Name { get; set; }
        public Guid TicketInfoId { get; set; }
        public TicketInfo TicketInfo { get; set; }
    }
}
