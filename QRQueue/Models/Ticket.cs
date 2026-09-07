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
        // チケット引き継ぎ(別端末への復元)用ワンタイムコードのSHA256ハッシュと有効期限(issue #75)。
        // コード自体は保存せず、DB漏えい時にも悪用できないようにする。
        public string? TransferCodeHash { get; set; }
        public DateTimeOffset? TransferCodeExpiresAt { get; set; }
        // LINE連携(LINE Login の userId。呼び出し時に Messaging API で push する)
        public string? LineUserId { get; set; }
    }
    public enum TicketStatus
    {
        Registered, // 参加登録済み
        Cancelled,  // 上書き・離脱により無効
        Used,       // ゲーム参加が終了した使用済み(issue #71)。同一イベントでの再参加は新規チケットで行う
    }
}
