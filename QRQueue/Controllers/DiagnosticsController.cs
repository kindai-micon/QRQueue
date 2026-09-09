using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// ⚠️ 一時的な診断用エンドポイント(削除前提)。
    /// サーバーに実際に読み込まれている設定値を確認するためのもの。
    /// アクセスには マスターパスワード(Master:Password、Actionsのシークレット
    /// MASTER_PASSWORD からデプロイ時に注入)が必要。
    /// 設定値をそのまま返すため、設定確認が済みしだい
    /// Controllers/DiagnosticsController.cs ごと必ず削除すること。
    /// </summary>
    [Route("api/diagnostics")]
    [ApiController]
    public class DiagnosticsController(
        IConfiguration configuration,
        IVapidService vapidService,
        ILineService lineService,
        IWebHostEnvironment environment) : ControllerBase
    {
        // ブラウザのアドレスバーから直接開けるよう ?password= クエリと
        // X-Master-Password ヘッダの両方を受け付ける(ヘッダ推奨: クエリはログに残る)
        private bool IsAuthorized()
        {
            var masterPassword = configuration["Master:Password"];
            if (string.IsNullOrEmpty(masterPassword))
            {
                // マスターパスワードが未設定なら機能を無効化(fail-closed)
                return false;
            }

            var provided = Request.Headers["X-Master-Password"].FirstOrDefault();
            if (string.IsNullOrEmpty(provided))
            {
                provided = Request.Query["password"];
            }
            if (string.IsNullOrEmpty(provided))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(masterPassword));
        }

        // 受付QRの到着確認コード・VAPID・LINE連携など、運用設定が
        // 「本番サーバーで実際にどう読み込まれているか」を確認する
        [HttpGet("settings")]
        public async Task<IActionResult> Settings()
        {
            if (!IsAuthorized())
            {
                // 存在自体を悟らせないため 404 を返す
                return NotFound();
            }

            var keys = await vapidService.GetOrCreateKeysAsync();
            var keysPath = configuration["Vapid:KeysFilePath"] ?? "vapid_keys.json";

            return Ok(new
            {
                environment = environment.EnvironmentName,
                checkin = new
                {
                    // 受付QRの到着確認コードを導出する秘密鍵(未設定ならVAPID鍵へフォールバック)
                    receptionSecret = configuration["Checkin:ReceptionSecret"],
                },
                vapid = new
                {
                    subject = configuration["Vapid:Subject"],
                    keysFilePath = keysPath,
                    keysFileExists = System.IO.File.Exists(keysPath),
                    publicKey = keys.PublicKey,
                },
                line = new
                {
                    configured = lineService.IsConfigured,
                    channelAccessToken = configuration["Line:ChannelAccessToken"],
                    loginClientId = configuration["Line:LoginClientId"],
                    loginClientSecret = configuration["Line:LoginClientSecret"],
                    redirectUri = configuration["Line:RedirectUri"],
                },
                cors = new
                {
                    allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").GetChildren()
                        .Select(c => c.Value).ToArray(),
                    envAllowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS"),
                },
                useHttpsForQrCode = configuration.GetValue<bool?>("UseHttpsForQrCode"),
            });
        }
    }
}