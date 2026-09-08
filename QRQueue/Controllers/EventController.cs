using QRQueue.Models;
using QRQueue.Models.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EventController(
        ApplicationDbContext applicationDbContext,
        IConfiguration configuration,
        IWebHostEnvironment environment) : ControllerBase
    {
        // インスタンス毎の生成を避け、System.Text.Json のメタデータキャッシュを効かせる
        private static readonly JsonSerializerOptions TicketJsonSerializerOptions = new() { MaxDepth = 8 };

        [Authorize]
        [HttpGet(nameof(List))]
        public async Task<ActionResult<List<EventListItem>>> List()
        {
            var list = await applicationDbContext.Events.Select(x => new EventListItem(x.Name, x.DisplayId.ToString())).ToListAsync();
            return list;
        }
        [Authorize(Policy = "EventManagement")]
        [HttpPost(nameof(Create))]
        public async Task<IActionResult> Create([FromBody] string name)
        {
            // 仕様: イベント名の重複は禁止(削除・変更は DisplayId で行うため、
            // 名前は人間向けの表示ラベルとして一意であることを前提とする)
            if (applicationDbContext.Events.Any(x => x.Name == name))
            {
                return BadRequest(new ApiMessage("Event already exists"));
            }
            else
            {
                var ev = new Event()
                {
                    Name = name,
                    TicketInfo = new TicketInfo()
                };

                await applicationDbContext.Events.AddAsync(ev);
                await applicationDbContext.SaveChangesAsync();
            }
            return Ok();
        }

        /// <summary>
        /// イベント削除(issue #84)。イベント名ではなく一意なイベントID(DisplayId)で対象を特定する。
        /// 同名イベントが存在しても、意図した1件だけを削除できる。
        /// </summary>
        [Authorize(Policy = "EventManagement")]
        [HttpDelete("{eventDisplayId}")]
        public async Task<IActionResult> Delete(Guid eventDisplayId)
        {
            var ev = await applicationDbContext.Events
                .FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            // 削除前に管理者が対象を確認できるよう、関連情報を応答に含める
            var groupCount = await applicationDbContext.ParticipationGroups
                .CountAsync(g => g.EventId == ev.Id);
            applicationDbContext.Events.Remove(ev);
            await applicationDbContext.SaveChangesAsync();
            return Ok(new { id = ev.DisplayId.ToString(), name = ev.Name, deletedGroups = groupCount });
        }

        /// <summary>
        /// イベント名称変更(issue #84)。対象の特定はイベントID(DisplayId)で行い、
        /// 変更後の名前が既存イベントと重複する場合は拒否する。
        /// </summary>
        [Authorize(Policy = "EventManagement")]
        [HttpPut("{eventDisplayId}/name")]
        public async Task<IActionResult> Rename(Guid eventDisplayId, [FromBody] string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return BadRequest(new ApiMessage("イベント名が空です"));
            }
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            if (applicationDbContext.Events.Any(x => x.Name == newName && x.DisplayId != eventDisplayId))
            {
                return Conflict(new ApiMessage("同じ名前のイベントが既に存在します"));
            }
            ev.Name = newName;
            await applicationDbContext.SaveChangesAsync();
            return Ok(new { id = ev.DisplayId.ToString(), name = ev.Name });
        }
        [HttpGet(nameof(Name))]
        public async Task<ActionResult<string>> Name([FromQuery] string id)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.DisplayId.ToString() == id);
            if(ev == null)
            {
                return NotFound();
            }
            return ev.Name;
        }
        /// <summary>
        /// チケットJSONの取り込み(issue #78)。
        /// 利用者はサーバー上の任意パスを指定できない。読み込み対象は
        /// 許可ディレクトリ(TicketJson:Directory、既定は {ContentRoot}/TicketJson)直下の
        /// .json ファイルのみで、ファイル名だけで識別する。
        /// パストラバーサル・絶対パス・サイズ超過・JSON不整合は拒否し、
        /// エラー応答にサーバー内部のパスやファイル内容を含めない。
        /// </summary>
        [Authorize(Policy = "EventManagement")]
        [HttpPost(nameof(LoadTicketJson))]
        public async Task<IActionResult> LoadTicketJson([FromBody] ticketJsonRequest request)
        {
            const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

            // 1) ファイル名の検証: パス区切り・親ディレクトリ参照・絶対パスを拒否し、
            //    許可ディレクトリ直下の .json のみを受け付ける
            var fileName = request.fileName?.Trim() ?? string.Empty;
            if (fileName.Length == 0
                || fileName.Contains('/') || fileName.Contains('\\')
                || fileName.Contains("..")
                || Path.IsPathRooted(fileName)
                || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("読み込めるのは許可ディレクトリ直下の .json ファイル名のみです");
            }

            // 2) 許可ディレクトリへの正規化後パスの検証(パストラバーサルの最終防衛線)
            var baseDirectory = configuration["TicketJson:Directory"];
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.Combine(environment.ContentRootPath, "TicketJson");
            }
            // 末尾セパレータ付き設定値でも比較が壊れないよう正規化する
            var normalizedBaseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(normalizedBaseDirectory, fileName));
            }
            catch (Exception)
            {
                return BadRequest("不正なファイル名です");
            }
            if (!fullPath.StartsWith(normalizedBaseDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return BadRequest("読み込めるのは許可ディレクトリ直下の .json ファイル名のみです");
            }
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound("指定されたファイルが見つかりません");
            }
            if (new FileInfo(fullPath).Length > MaxFileSizeBytes)
            {
                return BadRequest("ファイルサイズが上限(5MB)を超えています");
            }

            // 3) 読み込みとJSON形式の検証
            string raw;
            try
            {
                raw = await System.IO.File.ReadAllTextAsync(fullPath);
            }
            catch (Exception)
            {
                return BadRequest("ファイルを読み込めませんでした");
            }
            jsonTicket[]? tickets;
            try
            {
                tickets = JsonSerializer.Deserialize<jsonTicket[]>(raw, TicketJsonSerializerOptions);
            }
            catch (JsonException)
            {
                return BadRequest("JSONの形式が正しくありません");
            }
            if (tickets == null)
            {
                return BadRequest("JSONの形式が正しくありません");
            }

            // 4) 取り込み先イベントの存在確認(イベント・グループとの整合性)
            var ev = await applicationDbContext.Events
                .FirstOrDefaultAsync(x => x.DisplayId.ToString() == request.groupId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }

            // 5) 重複検証(チケット番号・QR ID)
            if (tickets.Select(t => t.number).Distinct().Count() != tickets.Length)
            {
                return BadRequest("チケット番号が重複しています");
            }
            var requestedIds = tickets.Select(t => t.displayId).ToList();
            var hasExistingId = await applicationDbContext.Tickets
                .AnyAsync(t => requestedIds.Contains(t.DisplayId));
            if (hasExistingId)
            {
                return BadRequest("既に存在するチケットIDが含まれています");
            }

            foreach (var item in tickets)
            {
                Ticket ticket = new();
                ticket.Status = TicketStatus.Registered;
                ticket.Number = item.number;
                ticket.DisplayId = item.displayId;
                applicationDbContext.Tickets.Add(ticket);
            }
            await applicationDbContext.SaveChangesAsync();
            return Ok(new { imported = tickets.Length });
        }

    }
    public record ticketJsonRequest(string groupId, string fileName);
    public record jsonTicket(long number, Guid displayId);
}
