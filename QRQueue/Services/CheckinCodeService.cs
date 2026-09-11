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

        /// <summary>
        /// 固定掲示用チェックインQRのURLに埋め込むコードを取得する。
        /// 印刷した固定QR(撮影・共有・再利用が可能)を許容する運用向け。
        /// 回転コードとは異なり失効しないため、発行は受付担当者の責任で行うこと。
        /// </summary>
        Task<string> GetStaticPosterCodeAsync(Guid eventDisplayId);

        /// <summary>到着確認コードが正しいか検証する(直前ウィンドウの許容を含む)</summary>
        Task<bool> IsValidAsync(Guid eventDisplayId, string? code);

        /// <summary>
        /// 診断用: 保存されたシークレットを新しいランダム値で再生成する。
        /// 実行すると全イベントの印刷済み受付QRが無効化されるため、再印刷とセットで使う。
        /// </summary>
        Task RotateSecretAsync();
    }

    public class CheckinCodeService(IConfiguration configuration, IVapidService vapidService) : ICheckinCodeService
    {
        /// <summary>コードの更新間隔(秒)。受付画面のQRはこの間隔で更新される</summary>
        public const int WindowSeconds = 30;

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

                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(path,
                    System.Text.Json.JsonSerializer.Serialize(new StoredSecret(secret)));
                return secret;
            }
        }

        private record StoredSecret(string Secret);

        public async Task<string> GetCurrentCheckinCodeAsync(Guid eventDisplayId)
        {
            return Compute(await GetKeyAsync(), eventDisplayId, CurrentWindow());
        }

        // 固定掲示用コードの導出に使うウィンドウ値。
        // 回転ウィンドウは UNIX時間/30 の正値しか取らないため、負値を使えば衝突しない。
        private const long StaticPosterWindow = -1;

        public async Task<string> GetStaticPosterCodeAsync(Guid eventDisplayId)
        {
            return Compute(await GetKeyAsync(), eventDisplayId, StaticPosterWindow);
        }

        public async Task<bool> IsValidAsync(Guid eventDisplayId, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var key = await GetKeyAsync();

            var trimmed = System.Text.Encoding.UTF8.GetBytes(code.Trim());

            // 固定掲示用QR(印刷物)のコードも許容する。
            // 印刷した固定QRは失効しないため、撮影・共有されたURLは再利用可能になる。
            // この運用を許容するかどうかは掲示PDFの発行判断に委ねられる(併存方式)。
            var staticPoster = System.Text.Encoding.UTF8.GetBytes(
                Compute(key, eventDisplayId, StaticPosterWindow));
            if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(trimmed, staticPoster))
            {
                return true;
            }

            // 現在ウィンドウと直前ウィンドウを許容(QR更新タイミング・時計ずれの吸収)。
            // それより古いコードは拒否されるため、撮影・共有されたQRの再利用は制限される。
            var current = CurrentWindow();
            foreach (var window in new long[] { current, current - 1 })
            {
                var expected = Compute(key, eventDisplayId, window);
                if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    trimmed,
                    System.Text.Encoding.UTF8.GetBytes(expected)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>診断用: シークレットを新しいランダム値で再生成する(RotateSecretAsync を参照)</summary>
        public Task RotateSecretAsync()
        {
            lock (SecretFileLock)
            {
                var secret = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                var dir = Path.GetDirectoryName(Path.GetFullPath(SecretFilePath));
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(SecretFilePath,
                    System.Text.Json.JsonSerializer.Serialize(new StoredSecret(secret)));
                cachedKey = null;
            }
            return Task.CompletedTask;
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