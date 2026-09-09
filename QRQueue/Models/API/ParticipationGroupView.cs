namespace QRQueue.Models.API
{
    public class ParticipationGroupView
    {
        public long Number { get; set; }
        public int  People {  get; set; }
        public GroupStatus Status {  get; set; }
        // スタッフ操作(優先待機移動・棄権)の対象指定に使うグループID(issue #73)
        public Guid? DisplayId { get; set; }
    }
}
