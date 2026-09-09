using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// ⚠️ 一時的な診断用エンドポイント(削除前提)。
    /// サーバーに実際に読み込まれている設定値を確認するためのもの。
    /// シークレット値をそのまま返すため、設定確認が済みしだい
    /// Controllers/DiagnosticsController.cs ごと必ず削除すること。
    /// </summary>
    [Route("api/diagnostics")]
    [ApiController]
    [Authorize(Policy = "EventManagement")]
    public class DiagnosticsController(
        IConfiguration configuration,
        IVapidService vapidService,
        ILineService lineService,
        IWebHostEnvironment environment) : ControllerBase
    {
        // 受付QRの到着確認コード・VAPID・LINE連携など、運用設定が
        // 「本番サーバーで実際にどう読み込まれているか」を確認する
        [HttpGet("settings")]
        public async Task<IActionResult> Settings()
        {
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