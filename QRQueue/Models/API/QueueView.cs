using System.Collections;

namespace QRQueue.Models.API
{
    public class QueueView
    {
        public IEnumerable<ParticipationGroupView> WaitingGroup { get; set; }=new List<ParticipationGroupView>();
        public IEnumerable<ParticipationGroupView> CallingGroup { get; set; } = new List<ParticipationGroupView>();
        public IEnumerable<ParticipationGroupView> InterruptedGroup { get; set; } = new List<ParticipationGroupView>();
        public int PeoplePool { get; set; }
    }
}
