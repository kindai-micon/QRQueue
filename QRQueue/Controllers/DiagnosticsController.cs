using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace QRQueue.Controllers
{
    /// <summary>
    /// ⚠️ 一時的な診断用エンドポイント(削除前提)。
    /// サーバーに配置されている appsettings.json の中身をそのまま返す。
    /// アクセスには マスターパスワード(Master:Password、Actionsのシークレット
    /// MASTER_PASSWORD からデプロイ時に注入)が必要。
    /// 確認が済みしだい Controllers/DiagnosticsController.cs ごと必ず削除すること。
    /// </summary>
    [Route("api/diagnostics")]
    [ApiController]
    public class DiagnosticsController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
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

        // サーバーに配置された appsettings.json をそのまま返す
        [HttpGet("settings")]
        public IActionResult Settings()
        {
            if (!IsAuthorized())
            {
                // 存在自体を悟らせないため 404 を返す
                return NotFound();
            }

            try
            {
                var path = Path.Combine(environment.ContentRootPath, "appsettings.json");
                var json = System.IO.File.ReadAllText(path);
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
