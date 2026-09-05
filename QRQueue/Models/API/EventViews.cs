namespace QRQueue.Models.API
{
    /// <summary>GET /api/event/List の要素(管理画面のイベント一覧)</summary>
    public record EventListItem(string Name, string Id);
}
