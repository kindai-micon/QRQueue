using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Services
{
    /// <summary>
    /// LINE連携(LINE Login v2.1 OAuth + Messaging API push)。
    /// 紐付けは「電子券ページを開いた人がLINE Loginで承認する」方式で、
    /// state にチケットDisplayIdの署名付きトークンを載せて戻り先を検証する。
    /// bot=link を付けると承認時に公式アカウントの友だち追加を同時に促せる
    /// (Messaging APIチャネルとLINE Loginチャネルのリンクが必要)。
    /// </summary>
    public class LineService(
        IConfiguration configuration,
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<LineService> logger) : ILineService
    {
        private string? ChannelAccessToken => configuration.GetSection("Line")["ChannelAccessToken"];
        private string? LoginClientId => configuration.GetSection("Line")["LoginClientId"];
        private string? LoginClientSecret => configuration.GetSection("Line")["LoginClientSecret"];
        private string? RedirectUri => configuration.GetSection("Line")["RedirectUri"];

        public bool IsConfigured =>
            !string.IsNullOrEmpty(ChannelAccessToken)
            && !string.IsNullOrEmpty(LoginClientId)
            && !string.IsNullOrEmpty(LoginClientSecret)
            // コード内フォールバックは持たない(issue #63レビュー指摘)。
            // RedirectUri 未設定なら LINE 連携自体を無効化し、設定漏れの環境で
            // 別ドメインのURLへリダイレクトされることを防ぐ。
            && !string.IsNullOrEmpty(RedirectUri);

        // ===== state(署名付きトークン) =====

        private string SignStatePayload(string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LoginClientSecret!));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }

        private string BuildState(Guid ticketDisplayId)
        {
            var payload = $"{ticketDisplayId:N}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            return $"{payload}.{SignStatePayload(payload)}";
        }

        /// <summary>stateを検証してチケットDisplayIdを復元する(有効期限10分)</summary>
        private bool TryParseState(string state, out Guid ticketDisplayId)
        {
            ticketDisplayId = default;
            var parts = state.Split('.');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out ticketDisplayId)
                || !long.TryParse(parts[1], out var issuedAt))
            {
                return false;
            }
            var payload = $"{parts[0]}.{parts[1]}";
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(SignStatePayload(payload)),
                    Encoding.UTF8.GetBytes(parts[2])))
            {
                return false;
            }
            // リプレイ対策の緩和策として発行から10分のみ有効
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt <= 600;
        }

        public string BuildAuthorizeUrl(Guid ticketDisplayId)
        {
            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = LoginClientId,
                ["redirect_uri"] = RedirectUri,
                ["state"] = BuildState(ticketDisplayId),
                ["scope"] = "profile openid",
                // 承認画面でMessaging APIチャネルのBot(公式アカウント)の友だち追加を促す
                ["bot"] = "link",
            };
            return "https://access.line.me/oauth2/v2.1/authorize?" + string.Join("&",
                query.Where(kv => !string.IsNullOrEmpty(kv.Value))
                     .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        }

        // ===== コールバック(紐付け) =====

        private sealed record TokenResponse(string? AccessToken, string? IdToken);

        public async Task<Guid?> ResolveBindingAsync(string code, string state)
        {
            if (IsConfigured == false || !TryParseState(state, out var ticketDisplayId))
            {
                logger.LogWarning("LINE callback: state検証に失敗");
                return null;
            }

            try
            {
                var client = httpClientFactory.CreateClient();
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = LoginClientId!,
                    ["client_secret"] = LoginClientSecret!,
                };
                var res = await client.PostAsync("https://api.line.me/oauth2/v2.1/token",
                    new FormUrlEncodedContent(form));
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    logger.LogWarning("LINE token交換失敗 ({Status}): {Body}", (int)res.StatusCode, body);
                    return null;
                }

                var token = JsonSerializer.Deserialize<TokenResponse>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var lineUserId = ExtractSubject(token?.IdToken);
                if (string.IsNullOrEmpty(lineUserId))
                {
                    logger.LogWarning("LINE id_token から sub を取得できませんでした");
                    return null;
                }

                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.DisplayId == ticketDisplayId);
                if (ticket == null)
                {
                    logger.LogWarning("LINE callback: チケットが見つからない ({TicketId})", ticketDisplayId);
                    return null;
                }
                ticket.LineUserId = lineUserId;
                await db.SaveChangesAsync();

                await SendNotifyAsync([ticketDisplayId],
                    "QRQueueの呼び出し通知を設定しました。\n順番が来るとこのトークに通知が届きます。");
                return ticketDisplayId;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LINE連携処理でエラー");
                return null;
            }
        }

        /// <summary>id_token(JWT)のペイロードから sub(LINE userId)を取り出す</summary>
        private static string? ExtractSubject(string? idToken)
        {
            if (string.IsNullOrEmpty(idToken)) return null;
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var json = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(payload));
            return json.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }

        // ===== 送信 =====

        public async Task SendNotifyAsync(IReadOnlyList<Guid> ticketDisplayIds, string text)
        {
            if (!IsConfigured)
            {
                logger.LogDebug("LINE未設定のため通知をスキップ");
                return;
            }

            var lineUserIds = await db.Tickets
                .Where(t => ticketDisplayIds.Contains(t.DisplayId) && t.LineUserId != null)
                .Select(t => t.LineUserId!)
                .ToListAsync();
            if (lineUserIds.Count == 0)
            {
                logger.LogInformation("LINE: 送信対象が0件");
                return;
            }

            var client = httpClientFactory.CreateClient();
            // LINE API の遅延・障害時に呼び出し操作(call/next等)が長時間ブロック
            // しないよう、既定の100秒ではなく短めのタイムアウトを設定する
            client.Timeout = TimeSpan.FromSeconds(10);
            foreach (var lineUserId in lineUserIds)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        "https://api.line.me/v2/bot/message/push");
                    request.Headers.Authorization = new("Bearer", ChannelAccessToken);
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            to = lineUserId,
                            messages = new[] { new { type = "text", text } }
                        }),
                        Encoding.UTF8, "application/json");
                    var res = await client.SendAsync(request);
                    if (!res.IsSuccessStatusCode)
                    {
                        // 友だち追加していないユーザーには届かない(400)。削除等はしないでログのみ
                        var body = await res.Content.ReadAsStringAsync();
                        logger.LogWarning("LINE push失敗 ({Status}): {Body}", (int)res.StatusCode, body);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "LINE push送信でエラー");
                }
            }
        }

        public async Task<bool> UnlinkAsync(Guid ticketDisplayId)
        {
            var count = await db.Tickets
                .Where(t => t.DisplayId == ticketDisplayId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LineUserId, (string?)null));
            return count > 0;
        }
    }
}
