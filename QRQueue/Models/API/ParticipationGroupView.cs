namespace QRQueue.Models.API
{
    public class ParticipationGroupView
    {
        public long Number { get; set; }
        public int  People {  get; set; }
        public GroupStatus Status {  get; set; }
        // スタッフ操作(優先待機移動・棄権)の対象指定に使うグループID(issue #73)
        public Guid? DisplayId { get; set; }
        // グループ内の有効チケット(スタッフがメンバー個人宛に通知を送る際の対象指定に使う)
        public List<TicketRefView> Tickets { get; set; } = new();
    }

    /// <summary>メンバー(チケット)個別通知の対象指定用。電子券IDのみ持たせる(個人情報は含めない)</summary>
    public class TicketRefView
    {
        public Guid DisplayId { get; set; }
    }
}
