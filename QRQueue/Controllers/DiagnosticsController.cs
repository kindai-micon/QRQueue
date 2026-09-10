using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Services;

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
    public class DiagnosticsController(
        IConfiguration configuration,
        IVapidService vapidService,
        ILineService lineService,
        ICheckinCodeService checkinCodeService,
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

        // サーバーに配置された appsettings.json と vapid_keys.json をそのまま返す
        [HttpGet("settings")]
        public async Task<IActionResult> Settings()
        {
            if (!IsAuthorized())
            {
                // 存在自体を悟らせないため 404 を返す
                return NotFound();
            }

            var result = new Dictionary<string, object?>();

            try
            {
                var path = Path.Combine(environment.ContentRootPath, "appsettings.json");
                result["appsettings"] = System.Text.Json.Nodes.JsonNode.Parse(
                    System.IO.File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                // 500 を返すとブラウザがボディを表示してくれないため、
                // 200 + error フィールドで例外内容を返す(一時診断用のため許容)
                result["appsettings"] = new { error = $"{ex.GetType().Name}: {ex.Message}" };
            }

            try
            {
                var vapidPath = Path.Combine(environment.ContentRootPath,
                    configuration["Vapid:KeysFilePath"] ?? "vapid_keys.json");
                if (!System.IO.File.Exists(vapidPath))
                {
                    result["vapidKeys"] = new { error = "ファイルが存在しません", path = vapidPath };
                }
                else
                {
                    result["vapidKeys"] = System.Text.Json.Nodes.JsonNode.Parse(
                        System.IO.File.ReadAllText(vapidPath));
                }
            }
            catch (Exception ex)
            {
                result["vapidKeys"] = new { error = $"{ex.GetType().Name}: {ex.Message}" };
            }

            // VAPID 鍵の「取得/生成」が実際に成功するかの検出。
            // ここで失敗すると Web Push 送信・到着確認コード導出の両方が壊れる(VAPID時代の問題)
            try
            {
                var keys = await vapidService.GetOrCreateKeysAsync();
                result["vapidTest"] = new { ok = true, publicKey = keys.PublicKey };
            }
            catch (Exception ex)
            {
                result["vapidTest"] = new
                {
                    ok = false,
                    error = $"{ex.GetType().Name}: {ex.Message}",
                    hint = "VAPID鍵の取得/生成に失敗しています。vapid_keys.json の配置・権限を確認してください(Vapid:KeysFilePath 参照)",
                };
            }

            // 到着確認コード導出チェーン(CheckinCodeService)全体の動作確認
            try
            {
                var code = await checkinCodeService.GetCheckinCodeAsync(Guid.NewGuid());
                result["checkinCodeTest"] = new { ok = true, sampleCode = code };
            }
            catch (Exception ex)
            {
                result["checkinCodeTest"] = new
                {
                    ok = false,
                    error = $"{ex.GetType().Name}: {ex.Message}",
                    hint = "到着確認コードの導出に失敗しています。checkin_secret.json の配置・権限を確認してください(Checkin:SecretFilePath 参照)",
                };
            }

            return Ok(result);
        }

        // 診断: 現在のシークレットから導出される「あるべき到着確認コード」を返す。
        // 印刷済みポスターのURL内コード(rc=...)と照合し、一致しなければ再印刷が必要と分かる
        [HttpGet("checkin-code/{eventDisplayId}")]
        public async Task<IActionResult> CheckinCode(string eventDisplayId)
        {
            if (!IsAuthorized() || !Guid.TryParse(eventDisplayId, out var eventDisplayIdGuid))
            {
                return NotFound();
            }

            var code = await checkinCodeService.GetCheckinCodeAsync(eventDisplayIdGuid);
            return Ok(new
            {
                eventDisplayId = eventDisplayId,
                code = code,
                note = "このコードは Checkin:ReceptionSecret / checkin_secret.json 現在値から導出されています。印刷済みポスターと一致しない場合は再印刷してください",
            });
        }

        // 診断: 到着確認コードのシークレットを新しいランダム値で再生成する。
        // ⚠️ 実行すると全イベントの印刷済み受付QRが無効化される(再印刷が必須)。
        // VAPID時代に鍵が変わる等の問題でコードが不整合を起こしている場合のリセット用
        [HttpPost("rotate-checkin-secret")]
        public async Task<IActionResult> RotateCheckinSecret()
        {
            if (!IsAuthorized())
            {
                return NotFound();
            }

            await checkinCodeService.RotateSecretAsync();
            return Ok(new
            {
                rotated = true,
                warning = "シークレットを再生成しました。全イベントの受付QRを再印刷・再掲示してください",
            });
        }
    }
}
