using WebPush;
using Microsoft.Extensions.Configuration;

namespace QRQueue.Services
{
    public class VapidService : IVapidService
    {
        // 既定はアプリルート直下。ただし publish 先は rsync --delete のデプロイで消えるため、
        // 本番は設定 `Vapid:KeysFilePath` で配布物の外(例: /var/www/qrqueue/data/vapid_keys.json)へ出すこと
        private readonly string _keysFilePath;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public VapidService(IConfiguration configuration)
        {
            _keysFilePath = configuration["Vapid:KeysFilePath"] ?? "vapid_keys.json";
        }

        public async Task<VapidKeys> GetOrCreateKeysAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(_keysFilePath))
                {
                    var json = await File.ReadAllTextAsync(_keysFilePath);
                    var keys = System.Text.Json.JsonSerializer.Deserialize<VapidKeys>(json);

                    if (keys != null && keys.PublicKey != null && keys.PrivateKey != null)
                    {
                        return new VapidKeys
                        {
                            PublicKey = keys.PublicKey,
                            PrivateKey = keys.PrivateKey
                        };
                    }
                }

                var newKeys = GenerateKeys();
                var newJson = System.Text.Json.JsonSerializer.Serialize(newKeys);
                await File.WriteAllTextAsync(_keysFilePath, newJson);

                return newKeys;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static VapidKeys GenerateKeys()
        {
            var vapidKeys = VapidHelper.GenerateVapidKeys();
            return new VapidKeys
            {
                PublicKey = vapidKeys.PublicKey,
                PrivateKey = vapidKeys.PrivateKey
            };
        }
    }

    public class VapidKeys
    {
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
    }
}
