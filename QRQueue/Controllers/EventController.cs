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
        [Authorize(Policy = "EventManagement")]
        [HttpPost(nameof(Delete))]
        public async Task<IActionResult> Delete([FromBody] string name)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (ev == null)
            {
                return NotFound();
            }
            applicationDbContext.Events.Remove(ev);
            await applicationDbContext.SaveChangesAsync();
            return Ok();
        }
        [Authorize(Policy = "EventManagement")]
        [HttpPut(nameof(Rename))]
        public async Task<IActionResult> Rename([FromBody] RenameModel renameModel)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.Name == renameModel.Name);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Name = renameModel.NewName;
            applicationDbContext.Events.Update(ev);
            await applicationDbContext.SaveChangesAsync();
            return Ok();
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
