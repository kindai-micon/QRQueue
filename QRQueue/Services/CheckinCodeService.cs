namespace QRQueue.Services
{
    /// <summary>
    /// 受付確認用QRコードに埋め込む到着確認コードの発行・検証(issue #68)。
    /// チェックインは受付に掲示された確認用QR(確認コード付きURL)を起点としてのみ行える。
    /// コードはイベントIDとサーバー秘密鍵から導出される安定した値で、
    /// 確認用QR以外のURL(電子券画面からの直接リンク等)では到着確認を完了できない。
    /// </summary>
    public interface ICheckinCodeService
    {
        /// <summary>イベントの到着確認コードを取得する(受付確認QRのURLに埋め込む)</summary>
        Task<string> GetCheckinCodeAsync(Guid eventDisplayId);

        /// <summary>到着確認コードが正しいか検証する</summary>
        Task<bool> IsValidAsync(Guid eventDisplayId, string? code);
    }

    public class CheckinCodeService(IConfiguration configuration, IVapidService vapidService) : ICheckinCodeService
    {
        private byte[]? cachedKey;

        /// <summary>
        /// コード導出用のサーバー秘密鍵。
        /// 設定 Checkin:ReceptionSecret を優先し、未設定の場合は VAPID 秘密鍵
        /// (vapid_keys.json ファイルに永続化。喪失時は再生成により掲示済みQRの
        /// 確認コードが無効化されるため、安定性は設定値より劣る)から導出する。
        /// </summary>
        private async Task<byte[]> GetKeyAsync()
        {
            if (cachedKey != null)
            {
                return cachedKey;
            }
            var configured = configuration["Checkin:ReceptionSecret"];
            byte[] key;
            if (!string.IsNullOrEmpty(configured))
            {
                key = System.Text.Encoding.UTF8.GetBytes(configured);
            }
            else
            {
                var keys = await vapidService.GetOrCreateKeysAsync();
                key = System.Text.Encoding.UTF8.GetBytes(keys.PrivateKey ?? "QRQueue-checkin-fallback");
            }
            cachedKey = key;
            return key;
        }

        public async Task<string> GetCheckinCodeAsync(Guid eventDisplayId)
        {
            return Compute(await GetKeyAsync(), eventDisplayId);
        }

        public async Task<bool> IsValidAsync(Guid eventDisplayId, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }
            var expected = Compute(await GetKeyAsync(), eventDisplayId);
            // 恒時間比較で検証する
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(code.Trim()),
                System.Text.Encoding.UTF8.GetBytes(expected));
        }

        private static string Compute(byte[] key, Guid eventDisplayId)
        {
            var hash = System.Security.Cryptography.HMACSHA256.HashData(key, eventDisplayId.ToByteArray());
            // 16進16桁(64bit)。8桁だと推測耐性が不十分のため延長した(レビュー指摘)。
            // HMAC鍵はサーバー秘匿のためオフライン総当たりは不可能だが、
            // 在線総当たりの窓を狭める意味でも十分な長さを確保する。
            return Convert.ToHexString(hash, 0, 8); // 16進16桁
        }
    }
}
