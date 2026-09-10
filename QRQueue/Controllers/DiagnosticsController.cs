using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// 笞・・荳譎ら噪縺ｪ險ｺ譁ｭ逕ｨ繧ｨ繝ｳ繝峨・繧､繝ｳ繝・蜑企勁蜑肴署)縲・
    /// 繧ｵ繝ｼ繝舌・縺ｫ驟咲ｽｮ縺輔ｌ縺ｦ縺・ｋ appsettings.json 縺ｮ荳ｭ霄ｫ繧偵◎縺ｮ縺ｾ縺ｾ霑斐☆縲・
    /// 繧｢繧ｯ繧ｻ繧ｹ縺ｫ縺ｯ 繝槭せ繧ｿ繝ｼ繝代せ繝ｯ繝ｼ繝・Master:Password縲、ctions縺ｮ繧ｷ繝ｼ繧ｯ繝ｬ繝・ヨ
    /// MASTER_PASSWORD 縺九ｉ繝・・繝ｭ繧､譎ゅ↓豕ｨ蜈･)縺悟ｿ・ｦ√・
    /// 遒ｺ隱阪′貂医∩縺励□縺・Controllers/DiagnosticsController.cs 縺斐→蠢・★蜑企勁縺吶ｋ縺薙→縲・
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
        // 繝悶Λ繧ｦ繧ｶ縺ｮ繧｢繝峨Ξ繧ｹ繝舌・縺九ｉ逶ｴ謗･髢九￠繧九ｈ縺・?password= 繧ｯ繧ｨ繝ｪ縺ｨ
        // X-Master-Password 繝倥ャ繝縺ｮ荳｡譁ｹ繧貞女縺台ｻ倥￠繧・繝倥ャ繝謗ｨ螂ｨ: 繧ｯ繧ｨ繝ｪ縺ｯ繝ｭ繧ｰ縺ｫ谿九ｋ)
        private bool IsAuthorized()
        {
            var masterPassword = configuration["Master:Password"];
            if (string.IsNullOrEmpty(masterPassword))
            {
                // 繝槭せ繧ｿ繝ｼ繝代せ繝ｯ繝ｼ繝峨′譛ｪ險ｭ螳壹↑繧画ｩ溯・繧堤┌蜉ｹ蛹・fail-closed)
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

        // 繧ｵ繝ｼ繝舌・縺ｫ驟咲ｽｮ縺輔ｌ縺・appsettings.json 縺ｨ vapid_keys.json 繧偵◎縺ｮ縺ｾ縺ｾ霑斐☆
        [HttpGet("settings")]
        public async Task<IActionResult> Settings()
        {
            if (!IsAuthorized())
            {
                // 蟄伜惠閾ｪ菴薙ｒ謔溘ｉ縺帙↑縺・◆繧・404 繧定ｿ斐☆
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
                // 500 繧定ｿ斐☆縺ｨ繝悶Λ繧ｦ繧ｶ縺後・繝・ぅ繧定｡ｨ遉ｺ縺励※縺上ｌ縺ｪ縺・◆繧√・
                // 200 + error 繝輔ぅ繝ｼ繝ｫ繝峨〒萓句､門・螳ｹ繧定ｿ斐☆(荳譎りｨｺ譁ｭ逕ｨ縺ｮ縺溘ａ險ｱ螳ｹ)
                result["appsettings"] = new { error = $"{ex.GetType().Name}: {ex.Message}" };
            }

            try
            {
                var vapidPath = Path.Combine(environment.ContentRootPath,
                    configuration["Vapid:KeysFilePath"] ?? "vapid_keys.json");
                if (!System.IO.File.Exists(vapidPath))
                {
                    result["vapidKeys"] = new { error = "繝輔ぃ繧､繝ｫ縺悟ｭ伜惠縺励∪縺帙ｓ", path = vapidPath };
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

            // VAPID 骰ｵ縺ｮ縲悟叙蠕・逕滓・縲阪′螳滄圀縺ｫ謌仙粥縺吶ｋ縺九・讀懷・縲・
            // 縺薙％縺ｧ螟ｱ謨励☆繧九→ Web Push 騾∽ｿ｡繝ｻ蛻ｰ逹遒ｺ隱阪さ繝ｼ繝牙ｰ主・縺ｮ荳｡譁ｹ縺悟｣翫ｌ繧・VAPID譎ゆｻ｣縺ｮ蝠城｡・
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
                    hint = "VAPID骰ｵ縺ｮ蜿門ｾ・逕滓・縺ｫ螟ｱ謨励＠縺ｦ縺・∪縺吶Ｗapid_keys.json 縺ｮ驟咲ｽｮ繝ｻ讓ｩ髯舌ｒ遒ｺ隱阪＠縺ｦ縺上□縺輔＞(Vapid:KeysFilePath 蜿ら・)",
                };
            }

            // 蛻ｰ逹遒ｺ隱阪さ繝ｼ繝牙ｰ主・繝√ぉ繝ｼ繝ｳ(CheckinCodeService)蜈ｨ菴薙・蜍穂ｽ懃｢ｺ隱・
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
                    hint = "蛻ｰ逹遒ｺ隱阪さ繝ｼ繝峨・蟆主・縺ｫ螟ｱ謨励＠縺ｦ縺・∪縺吶Ｄheckin_secret.json 縺ｮ驟咲ｽｮ繝ｻ讓ｩ髯舌ｒ遒ｺ隱阪＠縺ｦ縺上□縺輔＞(Checkin:SecretFilePath 蜿ら・)",
                };
            }

            return Ok(result);
        }

        // 險ｺ譁ｭ: 迴ｾ蝨ｨ縺ｮ繧ｷ繝ｼ繧ｯ繝ｬ繝・ヨ縺九ｉ蟆主・縺輔ｌ繧九後≠繧九∋縺榊芦逹遒ｺ隱阪さ繝ｼ繝峨阪ｒ霑斐☆縲・
        // 蜊ｰ蛻ｷ貂医∩繝昴せ繧ｿ繝ｼ縺ｮURL蜀・さ繝ｼ繝・rc=...)縺ｨ辣ｧ蜷医＠縲∽ｸ閾ｴ縺励↑縺代ｌ縺ｰ蜀榊魂蛻ｷ縺悟ｿ・ｦ√→蛻・°繧・
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
                note = "縺薙・繧ｳ繝ｼ繝峨・ Checkin:ReceptionSecret / checkin_secret.json 迴ｾ蝨ｨ蛟､縺九ｉ蟆主・縺輔ｌ縺ｦ縺・∪縺吶ょ魂蛻ｷ貂医∩繝昴せ繧ｿ繝ｼ縺ｨ荳閾ｴ縺励↑縺・ｴ蜷医・蜀榊魂蛻ｷ縺励※縺上□縺輔＞",
            });
        }

        // 險ｺ譁ｭ: 蛻ｰ逹遒ｺ隱阪さ繝ｼ繝峨・繧ｷ繝ｼ繧ｯ繝ｬ繝・ヨ繧呈眠縺励＞繝ｩ繝ｳ繝繝蛟､縺ｧ蜀咲函謌舌☆繧九・
        // 笞・・螳溯｡後☆繧九→蜈ｨ繧､繝吶Φ繝医・蜊ｰ蛻ｷ貂医∩蜿嶺ｻ浪R縺檎┌蜉ｹ蛹悶＆繧後ｋ(蜀榊魂蛻ｷ縺悟ｿ・・縲・
        // VAPID譎ゆｻ｣縺ｫ骰ｵ縺悟､峨ｏ繧狗ｭ峨・蝠城｡後〒繧ｳ繝ｼ繝峨′荳肴紛蜷医ｒ襍ｷ縺薙＠縺ｦ縺・ｋ蝣ｴ蜷医・繝ｪ繧ｻ繝・ヨ逕ｨ
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
                warning = "繧ｷ繝ｼ繧ｯ繝ｬ繝・ヨ繧貞・逕滓・縺励∪縺励◆縲ょ・繧､繝吶Φ繝医・蜿嶺ｻ浪R繧貞・蜊ｰ蛻ｷ繝ｻ蜀肴軸遉ｺ縺励※縺上□縺輔＞",
            });
        }
    }
}
