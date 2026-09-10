using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// ⚠�E�E一時的な診断用エンド�EインチE削除前提)、E
    /// サーバ�Eに配置されてぁE�� appsettings.json の中身をそのまま返す、E
    /// アクセスには マスターパスワーチEMaster:Password、ActionsのシークレチE��
    /// MASTER_PASSWORD からチE�Eロイ時に注入)が忁E��、E
    /// 確認が済みしだぁEControllers/DiagnosticsController.cs ごと忁E��削除すること、E
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
        // ブラウザのアドレスバ�Eから直接開けるよぁE?password= クエリと
        // X-Master-Password ヘッダの両方を受け付けめEヘッダ推奨: クエリはログに残る)
        private bool IsAuthorized()
        {
            var masterPassword = configuration["Master:Password"];
            if (string.IsNullOrEmpty(masterPassword))
            {
                // マスターパスワードが未設定なら機�Eを無効匁Efail-closed)
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

        // サーバ�Eに配置されぁEappsettings.json と vapid_keys.json をそのまま返す
        [HttpGet("settings")]
        public async Task<IActionResult> Settings()
        {
            if (!IsAuthorized())
            {
                // 存在自体を悟らせなぁE��めE404 を返す
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
                // 500 を返すとブラウザが�EチE��を表示してくれなぁE��め、E
                // 200 + error フィールドで例外�E容を返す(一時診断用のため許容)
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

            // VAPID 鍵の「取征E生�E」が実際に成功するか�E検�E、E
            // ここで失敗すると Web Push 送信・到着確認コード導�Eの両方が壊れめEVAPID時代の問顁E
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
                    hint = "VAPID鍵の取征E生�Eに失敗してぁE��す。vapid_keys.json の配置・権限を確認してください(Vapid:KeysFilePath 参�E)",
                };
            }

            // 到着確認コード導�Eチェーン(CheckinCodeService)全体�E動作確誁E
            try
            {
                var code = await checkinCodeService.GetCurrentCheckinCodeAsync(Guid.NewGuid());
                result["checkinCodeTest"] = new { ok = true, sampleCode = code };
            }
            catch (Exception ex)
            {
                result["checkinCodeTest"] = new
                {
                    ok = false,
                    error = $"{ex.GetType().Name}: {ex.Message}",
                    hint = "到着確認コード�E導�Eに失敗してぁE��す。checkin_secret.json の配置・権限を確認してください(Checkin:SecretFilePath 参�E)",
                };
            }

            return Ok(result);
        }

        // 診断: 現在のシークレチE��から導�Eされる「あるべき到着確認コード」を返す、E
        // 印刷済みポスターのURL冁E��ーチErc=...)と照合し、一致しなければ再印刷が忁E��と刁E��めE
        // LINE連携の診断(マスターパスワード必要)。
        // 連携が失敗するとき、原因が「設定漏れ」「トークン無効」「RedirectUri不一致」の
        // どれかをここで切り分けられる。シークレットそのものは返さない
        [HttpGet("line")]
        public async Task<IActionResult> LineDiagnosis()
        {
            if (!IsAuthorized())
            {
                return NotFound();
            }
            return Ok(await lineService.DiagnoseAsync());
        }

        [HttpGet("checkin-code/{eventDisplayId}")]
        public async Task<IActionResult> CheckinCode(string eventDisplayId)
        {
            if (!IsAuthorized() || !Guid.TryParse(eventDisplayId, out var eventDisplayIdGuid))
            {
                return NotFound();
            }

            var code = await checkinCodeService.GetCurrentCheckinCodeAsync(eventDisplayIdGuid);
            return Ok(new
            {
                eventDisplayId = eventDisplayId,
                code = code,
                note = "こ�Eコード�E Checkin:ReceptionSecret / checkin_secret.json 現在値から導�EされてぁE��す。印刷済みポスターと一致しなぁE��合�E再印刷してください",
            });
        }

        // 診断: 到着確認コード�EシークレチE��を新しいランダム値で再生成する、E
        // ⚠�E�E実行すると全イベント�E印刷済み受付QRが無効化される(再印刷が忁E��E、E
        // VAPID時代に鍵が変わる等�E問題でコードが不整合を起こしてぁE��場合�EリセチE��用
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
                warning = "シークレチE��を�E生�Eしました。�Eイベント�E受付QRを�E印刷・再掲示してください",
            });
        }
    }
}
