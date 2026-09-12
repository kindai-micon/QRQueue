using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        private string? ChannelSecret => configuration.GetSection("Line")["ChannelSecret"];
        private string? OfficialAccountId => configuration.GetSection("Line")["OfficialAccountId"];
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
        /// <summary>stateを検証してチケットDisplayIdを復元する。有効期限30分。
        /// 失敗時は failureReason に原因(state不正/署名不一致/期限切れ)を返す</summary>
        private bool TryParseState(string state, out Guid ticketDisplayId, out string? failureReason)
        {
            ticketDisplayId = default;
            failureReason = null;
            var parts = state.Split('.');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out ticketDisplayId)
                || !long.TryParse(parts[1], out var issuedAt))
            {
                failureReason = "state形式不正";
                return false;
            }
            var payload = $"{parts[0]}.{parts[1]}";
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(SignStatePayload(payload)),
                    Encoding.UTF8.GetBytes(parts[2])))
            {
                failureReason = "state署名不一致";
                return false;
            }
            // リプレイ対策として発行から30分のみ有効。LINE Login の入力待ちで
            // 10分だと失効して連携が失敗し、ユーザー体験を損なうため余裕を持たせる
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt > 1800)
            {
                failureReason = "state有効期限切れ(発行から30分超過)";
                return false;
            }
            return true;
        }

        /// <summary>state(署名付きトークン)からチケットDisplayIdだけを緩く取り出す。
        /// 連携失敗時でも電子券ページへユーザーを戻すために使う(署名・期限の検証はしない)。
        /// チケットDisplayIdは電子券ページのURL自体に使われているため、取り出しても秘匿性の低下はない</summary>
        public Guid? ExtractTicketIdFromState(string state)
        {
            var parts = state?.Split('.');
            return parts is { Length: 3 } && Guid.TryParseExact(parts[0], "N", out var id) ? id : null;
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
                // 承認画面の後に友だち追加の確認画面を表示する(公式仕様の bot_prompt。
                // 旧パラメータ bot=link は無視されるため友だち追加が促されない問題があった)
                ["bot_prompt"] = "aggressive",
            };
            return "https://access.line.me/oauth2/v2.1/authorize?" + string.Join("&",
                query.Where(kv => !string.IsNullOrEmpty(kv.Value))
                     .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        }

        // ===== コールバック(紐付け) =====

        // LINEのトークンレスポンスはスネークケース(access_token / id_token)。
        // PropertyNameCaseInsensitive ではアンダースコアを吸収できないため、
        // JsonPropertyName で明示的にマッピングする(無いと常に null になり連携が必ず失敗する)
        private sealed record TokenResponse(
            [property: JsonPropertyName("access_token")] string? AccessToken,
            [property: JsonPropertyName("id_token")] string? IdToken);

        public async Task<(Guid? TicketDisplayId, string? FailureReason, bool? NotFriend)> ResolveBindingAsync(string code, string state)
        {
            // FailureReason は電子券画面に ?line=error&reason=... で渡す短いコード。
            // スマホ等デベロッパーツールを使えない環境でも原因を画面で分かるようにするため
            if (IsConfigured == false)
            {
                logger.LogWarning("LINE callback: LINE連携設定が未完成のため失敗(ChannelAccessToken/ClientId/ClientSecret/RedirectUri を確認)");
                return (null, "config", null);
            }
            if (!TryParseState(state, out var ticketDisplayId, out var stateFailure))
            {
                logger.LogWarning("LINE callback: state検証に失敗({Reason})。state先頭={StateHead}", stateFailure, state.Length > 13 ? state[..13] : state);
                // 原因の種類を画面に渡せるよう細分化する
                var stateCode = stateFailure switch
                {
                    "state署名不一致" => "sign",
                    "state有効期限切れ(発行から30分超過)" => "expired",
                    _ => "state",
                };
                return (null, stateCode, null);
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
                    return (null, "token", null);
                }

                var token = JsonSerializer.Deserialize<TokenResponse>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var lineUserId = ExtractSubject(token?.IdToken);
                // 画面表示用の失敗原因コード(id_token由来か/プロフィールAPI由来か/ステータスコード)を細かく出す。
                // スマホ等でログを見られない利用者の画面に、原因をそのまま出せるようにするため
                string? failCode = null;
                if (lineUserId == null)
                {
                    failCode = string.IsNullOrEmpty(token?.IdToken) ? "idtoken_absent" : "idtoken_sub";
                }
                if (lineUserId == null && !string.IsNullOrEmpty(token?.AccessToken))
                {
                    // id_token に sub がない(または id_token 自体が返らない)環境向けフォールバック。
                    // LINE Login ソーシャルAPIのプロフィール取得から userId を取得する
                    // (Messaging API push の to に使えるのはこの userId と同じ値)
                    var (profileUserId, profileStatus) = await FetchUserIdFromProfileAsync(token.AccessToken);
                    if (profileUserId != null)
                    {
                        lineUserId = profileUserId;
                        failCode = null;
                    }
                    else
                    {
                        failCode = profileStatus != null ? $"profile{profileStatus}" : "profile_error";
                    }
                }
                if (string.IsNullOrEmpty(lineUserId))
                {
                    logger.LogWarning("LINE callback: id_token とプロフィールAPIのどちらからも userId を取得できませんでした({Code})", failCode);
                    return (null, failCode ?? "idtoken", null);
                }

                var ticket = await db.Tickets
                    .Include(t => t.ParticipationGroup)
                        .ThenInclude(g => g.Event)
                    .FirstOrDefaultAsync(t => t.DisplayId == ticketDisplayId);
                if (ticket == null)
                {
                    logger.LogWarning("LINE callback: チケットが見つからない ({TicketId})", ticketDisplayId);
                    return (null, "ticket", null);
                }
                ticket.LineUserId = lineUserId;
                await db.SaveChangesAsync();

                // 連携直後に友だち登録状態を照会する。未追加のまま連携が完了すると
                // 通知が届かないため、電子券ページで警告できるよう結果を返す
                var notFriend = await CheckFriendFlagAsync(lineUserId) == false;

                // 連携したチケットが分かるよう、イベント名と番号を確認メッセージに載せる。
                // 複数のチケットで連携したときに「どのチケットの通知だったか」を区別できるようにするため
                var group = ticket.ParticipationGroup;
                var message = "QRQueueの呼び出し通知を設定しました。\n";
                if (group?.Event != null)
                {
                    message += $"イベント: {group.Event.Name}\n";
                }
                // Ticket.Number は旧システムの廃止予定項目で新規採番されない(常に0)ため表示しない
                if (group != null && group.Number > 0)
                {
                    message += $"整理券番号: {group.Number}番";
                }
                message += "\n順番が来るとこのトークに通知が届きます。";

                await SendNotifyAsync([ticketDisplayId], message);
                return (ticketDisplayId, null, notFriend);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LINE連携処理でエラー");
                return (null, "exception", null);
            }
        }

        /// <summary>id_token(JWT)のペイロードから sub(LINE userId)を取り出す。
        /// 取得できない場合は、原因をログに残す(値そのものはログに出さない)</summary>
        private string? ExtractSubject(string? idToken)
        {
            if (string.IsNullOrEmpty(idToken))
            {
                // LINE はスコープやチャネル設定によって id_token を返さないことがある
                logger.LogWarning("LINE id_token がレスポンスに含まれていません(プロフィールAPIへフォールバックします)");
                return null;
            }
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                logger.LogWarning("LINE id_token の形式が不正です(セグメント数={Count})", parts.Length);
                return null;
            }
            string json;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            }
            catch (FormatException ex)
            {
                logger.LogWarning(ex, "LINE id_token のペイロードをデコードできませんでした");
                return null;
            }
            JsonElement jsonElement;
            try
            {
                jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "LINE id_token のペイロードがJSONとして解釈できませんでした");
                return null;
            }
            if (jsonElement.TryGetProperty("sub", out var sub) && !string.IsNullOrEmpty(sub.GetString()))
            {
                return sub.GetString();
            }
            // 原因特定用にクレーム名だけをログへ出す(値は出さない)
            var keys = string.Join(",", jsonElement.EnumerateObject().Select(p => p.Name));
            logger.LogWarning("LINE id_token に sub が含まれませんでした。含まれるクレーム: [{Keys}]", keys);
            return null;
        }

        /// <summary>公式アカウントの友だち追加URL(line.me/R/ti/p/@ID)。未設定なら null。
        /// 連携フローの外からでも友だち追加できるよう、電子券ページに直接リンクを出すためのもの</summary>
        public string? GetAddFriendUrl()
        {
            return string.IsNullOrEmpty(OfficialAccountId)
                ? null
                : $"https://line.me/R/ti/p/@{OfficialAccountId}";
        }

        /// <summary>Messaging APIのfriendship status照会で、ユーザーが公式アカウントの友だちかを返す。
        /// 照会自体が失敗した場合は判定不能として null を返す</summary>
        private async Task<bool?> CheckFriendFlagAsync(string lineUserId)
        {
            if (ChannelAccessToken == null)
            {
                return null;
            }
            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.line.me/v2/bot/friendship/status?userId={Uri.EscapeDataString(lineUserId)}");
                request.Headers.Authorization = new("Bearer", ChannelAccessToken);
                var res = await client.SendAsync(request);
                if (!res.IsSuccessStatusCode)
                {
                    logger.LogWarning("LINE friendship status照会が失敗 ({Status})", (int)res.StatusCode);
                    return null;
                }
                var json = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync());
                return json.TryGetProperty("friendFlag", out var flag) && flag.ValueKind == JsonValueKind.True;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LINE friendship status照会でエラー");
                return null;
            }
        }

        /// <summary>アクセストークンを使い LINE Login ソーシャルAPI(GET /v2/profile)から userId を取得する。
        /// id_token に sub がない環境のフォールバック。userId と、失敗時は HTTPステータスコードを返す</summary>
        private async Task<(string? UserId, int? Status)> FetchUserIdFromProfileAsync(string accessToken)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/profile");
                request.Headers.Authorization = new("Bearer", accessToken);
                var res = await client.SendAsync(request);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    logger.LogWarning("LINE プロフィールAPIが失敗 ({Status}): {Body}", (int)res.StatusCode, body);
                    return (null, (int)res.StatusCode);
                }
                var profile = JsonSerializer.Deserialize<JsonElement>(body);
                return profile.TryGetProperty("userId", out var userId)
                    ? (userId.GetString(), null)
                    : (null, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LINE プロフィールAPIの呼び出しでエラー");
                return (null, null);
            }
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

        // ===== テスト通知(電子券ページのデバッグ用) =====

        /// <summary>チケット1件宛にテスト通知を実際に送り、結果を診断情報として返す。
        /// 通常の SendNotifyAsync は失敗してもログのみで黙るため、利用者側から原因を
        /// 切り分けられるよう、LINE API の応答コードと内容をそのまま返す(シークレットは返さない)</summary>
        public async Task<Dictionary<string, object?>> SendTestNotifyAsync(Guid ticketDisplayId)
        {
            var result = new Dictionary<string, object?> { ["configured"] = IsConfigured };
            if (!IsConfigured)
            {
                result["ok"] = false;
                result["reason"] = "サーバー側のLINE設定が未完了です(ChannelAccessToken / LoginClientId / LoginClientSecret / RedirectUri)。管理者に連絡してください";
                return result;
            }

            var lineUserId = await db.Tickets
                .Where(t => t.DisplayId == ticketDisplayId)
                .Select(t => t.LineUserId)
                .FirstOrDefaultAsync();
            result["lineLinked"] = lineUserId != null;
            if (lineUserId == null)
            {
                result["ok"] = false;
                result["reason"] = "このチケットはまだLINE連携されていません。「LINEで通知を受け取る」から連携してください";
                return result;
            }

            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://api.line.me/v2/bot/message/push");
                request.Headers.Authorization = new("Bearer", ChannelAccessToken);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        to = lineUserId,
                        messages = new[] { new { type = "text", text = "🔔 テスト通知です。QRQueueの呼び出し通知は正常に設定されています。" } }
                    }),
                    Encoding.UTF8, "application/json");
                var res = await client.SendAsync(request);
                var body = await res.Content.ReadAsStringAsync();
                result["pushStatus"] = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    result["ok"] = true;
                    result["message"] = "テスト通知を送信しました。LINEに届かない場合は公式アカウントの友だち追加が解除されていないか確認してください";
                }
                else
                {
                    result["ok"] = false;
                    result["error"] = body;
                    result["reason"] = (int)res.StatusCode == 400
                        ? "LINEへの送信が拒否されました(400)。公式アカウントの友だち追加が解除されていないか確認してください"
                        : (int)res.StatusCode == 401
                            ? "ChannelAccessToken が無効・期限切れです(401)。管理者はLINE Developersで再発行してください"
                            : "LINE Messaging API がエラーを返しました";
                }
            }
            catch (Exception ex)
            {
                result["ok"] = false;
                result["reason"] = "LINE APIへの接続に失敗しました(ネットワークエラー/タイムアウト)";
                result["error"] = $"{ex.GetType().Name}: {ex.Message}";
            }
            return result;
        }

        /// <summary>LINEプラットフォームのWebhook署名(X-Line-Signature)を検証する。
        /// Messaging APIチャネルのChannelSecretでのHMAC-SHA256(base64)。未設定なら検証不可=失敗扱い(fail-closed)</summary>
        private bool VerifyWebhookSignature(string body, string? signature)
        {
            if (string.IsNullOrEmpty(ChannelSecret) || string.IsNullOrEmpty(signature))
            {
                return false;
            }
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ChannelSecret));
            var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature));
        }

        /// <summary>Webhookイベントを処理する。unfollow(ブロック/友だち解除)が来たら
        /// 該当ユーザーの連携を自動解除する(ブロック済みへのpushは200で黙って届かなくなるため)。
        /// 戻り値は処理したイベント件数(署名検証失敗時は -1)</summary>
        public async Task<int> HandleWebhookAsync(string body, string? signature)
        {
            if (!VerifyWebhookSignature(body, signature))
            {
                logger.LogWarning("LINE webhook: 署名検証に失敗しました(ChannelSecret未設定または不一致)");
                return -1;
            }
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                if (!json.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                {
                    return 0;
                }
                var handled = 0;
                foreach (var ev in events.EnumerateArray())
                {
                    var type = ev.TryGetProperty("type", out var t) ? t.GetString() : null;
                    var userId = ev.TryGetProperty("source", out var src) && src.TryGetProperty("userId", out var uid)
                        ? uid.GetString()
                        : null;
                    if (type == "unfollow" && !string.IsNullOrEmpty(userId))
                    {
                        // ブロック・友だち解除されたので連携を失効させる(再度届くことはない)
                        var count = await db.Tickets
                            .Where(t => t.LineUserId == userId)
                            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LineUserId, (string?)null));
                        handled += count;
                        logger.LogInformation("LINE webhook: unfollow を検知し {Count} 件の連携を解除しました", count);
                    }
                    else
                    {
                        handled++;
                    }
                }
                return handled;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LINE webhookの処理でエラー");
                return 0;
            }
        }

        // ===== 診断(一時的な診断用エンドポイント向け。シークレットは返さない) =====

        public async Task<Dictionary<string, object?>> DiagnoseAsync()
        {
            var result = new Dictionary<string, object?>
            {
                ["channelAccessTokenSet"] = !string.IsNullOrEmpty(ChannelAccessToken),
                ["loginClientId"] = LoginClientId, // クライアントIDは公開値なのでそのまま返す(LINEコンソールとの照合用)
                ["loginClientSecretSet"] = !string.IsNullOrEmpty(LoginClientSecret),
                ["redirectUri"] = RedirectUri, // LINE Developers コンソールのコールバックURLと照合する用
            };

            // state 署名の自己検証(BuildAuthorizeUrl → 検証の往復が通るか)
            if (LoginClientSecret == null)
            {
                result["stateSelfTest"] = new { ok = false, error = "LoginClientSecret 未設定のため署名検証不可" };
            }
            else
            {
                var url = BuildAuthorizeUrl(Guid.NewGuid());
                var state = Uri.UnescapeDataString(url.Split("state=")[1].Split('&')[0]);
                result["stateSelfTest"] = new { ok = TryParseState(state, out _, out var reason), reason };
            }

            // ChannelAccessToken の有効性(Messaging API bot info)
            if (ChannelAccessToken == null)
            {
                result["botInfoTest"] = new { ok = false, error = "ChannelAccessToken 未設定" };
            }
            else
            {
                try
                {
                    var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/bot/info");
                    request.Headers.Authorization = new("Bearer", ChannelAccessToken);
                    var res = await client.SendAsync(request);
                    var body = await res.Content.ReadAsStringAsync();
                    result["botInfoTest"] = res.IsSuccessStatusCode
                        ? new { ok = true }
                        : new
                        {
                            ok = false,
                            status = (int)res.StatusCode,
                            error = body,
                            hint = (int)res.StatusCode == 401
                                ? "ChannelAccessToken が無効・期限切れです。LINE Developers で再発行して appsettings.json を更新してください"
                                : "Messaging API の応答がエラーです。チャネル設定を確認してください",
                        };
                }
                catch (Exception ex)
                {
                    result["botInfoTest"] = new { ok = false, error = $"{ex.GetType().Name}: {ex.Message}" };
                }
            }

            // RedirectUri が https か(LINE は本番で https を要求)
            result["redirectUriIsHttps"] = RedirectUri?.StartsWith("https://") == true;

            return result;
        }
    }
}
