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

        /// <summary>
        /// 診断用: 保存されたシークレットを新しいランダム値で再生成する。
        /// 実行すると全イベントの印刷済み受付QRが無効化されるため、再印刷とセットで使う。
        /// </summary>
        Task RotateSecretAsync();
    }

    public class CheckinCodeService(IConfiguration configuration, IVapidService vapidService) : ICheckinCodeService
    {
        private static readonly object SecretFileLock = new();
        private byte[]? cachedKey;

        /// <summary>
        /// コード導出用のサーバー秘密鍵。
        /// 1) 設定 Checkin:ReceptionSecret があればそれを最優先で使用
        /// 2) 未設定なら専用ファイル(Checkin:SecretFilePath、既定 checkin_secret.json)に
        ///    自動生成したシークレットを保存して使用する。
        ///    その際、VAPID 鍵が既に存在する環境では VAPID 秘密鍵を初期値として引き継ぐため、
        ///    以前のフォールバック時代に印刷した掲示QRも無効化されない。
        ///    以後は VAPID 鍵の再生成・喪失の影響を受けない。
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
                key = System.Text.Encoding.UTF8.GetBytes(GetOrCreateStoredSecretAsync().GetAwaiter().GetResult());
            }
            cachedKey = key;
            return key;
        }

        /// <summary>自動生成シークレットの保存先(配布物の外に出す場合は設定で変更する)</summary>
        private string SecretFilePath => configuration["Checkin:SecretFilePath"] ?? "checkin_secret.json";

        private async Task<string> GetOrCreateStoredSecretAsync()
        {
            lock (SecretFileLock)
            {
                var path = SecretFilePath;
                if (System.IO.File.Exists(path))
                {
                    var stored = System.Text.Json.JsonSerializer.Deserialize<StoredSecret>(
                        System.IO.File.ReadAllText(path));
                    if (!string.IsNullOrEmpty(stored?.Secret))
                    {
                        return stored.Secret;
                    }
                }

                // 初回生成。VAPID 鍵が既に存在する場合はその秘密鍵を引き継ぐことで、
                // フォールバック時代に導出されたコード(=印刷済みQR)との互換を維持する。
                // 新規環境では VAPID 鍵もこのタイミングで作成され、秘密鍵がそのまま使われる。
                var seed = vapidService.GetOrCreateKeysAsync().GetAwaiter().GetResult().PrivateKey;
                var secret = string.IsNullOrEmpty(seed)
                    ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                    : seed;

                System.IO.File.WriteAllText(path,
                    System.Text.Json.JsonSerializer.Serialize(new StoredSecret(secret)));
                return secret;
            }
        }

        private record StoredSecret(string Secret);

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

        /// <summary>診断用: シークレットを新しいランダム値で再生成する(RotateSecretAsync を参照)</summary>
        public Task RotateSecretAsync()
        {
            lock (SecretFileLock)
            {
                var secret = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                System.IO.File.WriteAllText(SecretFilePath,
                    System.Text.Json.JsonSerializer.Serialize(new StoredSecret(secret)));
                cachedKey = null;
            }
            return Task.CompletedTask;
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
