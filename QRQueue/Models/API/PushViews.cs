namespace QRQueue.Models.API
{
    /// <summary>GET /api/push-subscription/vapid-public-key のレスポンス(Web Push 購読用公開鍵)</summary>
    public record VapidPublicKeyView(string PublicKey);
}
