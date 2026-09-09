namespace QRQueue.Services
{
    /// <summary>
    /// 受付確認用QRコードに埋め込む到着確認コードの発行・検証。
    /// issue #68: チェックインは受付に掲示された確認用QR(確認コード付きURL)を起点としてのみ行える。
    /// issue #76: コードは30秒で回転する方式へ移行し、
    /// QRの撮影・共有・履歴からの再利用を制限する。
    /// </summary>
    public interface ICheckinCodeService
    {
        /// <summary>現在の到着確認コードを取得する(受付確認QRのURLに埋め込む)</summary>
        Task<string> GetCurrentCheckinCodeAsync(Guid eventDisplayId);

        /// <summary>到着確認コードが正しいか検証する(直前ウィンドウの許容を含む)</summary>
        Task<bool> IsValidAsync(Guid eventDisplayId, string? code);
    }

    public class CheckinCodeService(IConfiguration configuration, IVapidService vapidService) : ICheckinCodeService
    {
        /// <summary>コードの更新間隔(秒)。受付画面のQRはこの間隔で更新される</summary>
        public const int WindowSeconds = 30;

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

        public async Task<string> GetCurrentCheckinCodeAsync(Guid eventDisplayId)
        {
            return Compute(await GetKeyAsync(), eventDisplayId, CurrentWindow());
        }

        public async Task<bool> IsValidAsync(Guid eventDisplayId, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var key = await GetKeyAsync();

            // 現在ウィンドウと直前ウィンドウを許容(QR更新タイミング・時計ずれの吸収)。
            // それより古いコードは拒否されるため、撮影・共有されたQRの再利用は制限される。
            var current = CurrentWindow();
            foreach (var window in new long[] { current, current - 1 })
            {
                var expected = Compute(key, eventDisplayId, window);
                if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(code.Trim()),
                    System.Text.Encoding.UTF8.GetBytes(expected)))
                {
                    return true;
                }
            }
            return false;
        }

        private static long CurrentWindow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / WindowSeconds;
        }

        private static string Compute(byte[] key, Guid eventDisplayId, long window)
        {
            var input = new byte[16 + 8];
            eventDisplayId.ToByteArray().CopyTo(input, 0);
            BitConverter.GetBytes(window).CopyTo(input, 16);
            var hash = System.Security.Cryptography.HMACSHA256.HashData(key, input);
            // 16進16桁(64bit)。8桁だと推測耐性が不十分のため延長した(レビュー指摘)。
            // HMAC鍵はサーバー秘匿のためオフライン総当たりは不可能だが、
            // 在線総当たりの窓を狭める意味でも十分な長さを確保する。
            return Convert.ToHexString(hash, 0, 8); // 16進16桁
        }
    }
}
