namespace QRQueue.Models
{
    /// <summary>
    /// 旧紙券PDF用のラベル情報(設計§6.3/§8 で印刷物は廃止)。
    /// DB 上は Event の必須 FK として残置しており、マイグレーションは行わない。
    /// </summary>
    public class TicketInfo : BaseModel
    {
        public string Name { get; set; } = "抽選券";
        public string Description { get; set; } = "2025年度文化会新入生歓迎会";
        public string Warning { get; set; } = "当日のみ有効 本券は汚したり破らないよう大切に保管してください";
    }
}
