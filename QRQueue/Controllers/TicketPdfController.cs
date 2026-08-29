using QRQueue.Models;
using QRQueue;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using QRQueue.Services;
using QRQueue.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

[Route("api/pdf")]
[ApiController]
public class TicketPdfController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITicketPdfGenerator _pdfGenerator;
    private readonly IConfiguration _configuration;
    private readonly IServer _server;
    private readonly ITicketIssuanceService _ticketIssuanceService;
    private readonly IEventRepository _eventRepository;
    private readonly IBaseUrlResolver _baseUrlResolver;


    public TicketPdfController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITicketPdfGenerator pdfGenerator,
        IConfiguration configuration,
        IServer server,
        ITicketIssuanceService ticketIssuanceService,
        IEventRepository eventRepository,
        IBaseUrlResolver baseUrlResolver)
    {
        _db = db;
        _userManager = userManager;
        _pdfGenerator = pdfGenerator;
        _configuration = configuration;
        _server = server;
        _ticketIssuanceService = ticketIssuanceService;
        _eventRepository = eventRepository;
        _baseUrlResolver = baseUrlResolver;
    }
    [Authorize(Policy = "TicketPublish")]

    [HttpPost("generate")]
    public async Task<IActionResult> GeneratePdf([FromBody] TicketRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var lotteryGroup = _db.Events
            .Include(g => g.TicketInfo)
            .FirstOrDefault(g => g.DisplayId == request.EventDisplayId);

        if (lotteryGroup == null || lotteryGroup.TicketInfo == null)
            return BadRequest("無効なイベントIDまたはチケット情報が未設定です");

        TicketIssuanceResult result;
        try
        {
            // チケット発行サービスを使用
            result = await _ticketIssuanceService.IssueTicketsAsync(
                request.EventDisplayId,
                request.Count,
                TicketStatus.Registered,
                user.UserName ?? "Unknown");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        var tickets = result.Tickets;

        // BaseURL の生成は共通化(設計§8。旧ロジックは BaseUrlResolver へ移譲)
        string baseUrl = _baseUrlResolver.Resolve(Request) + "/ticket/";

        var ticketInfo = tickets.Select(t => new TicketInfo
        {
            TicketNumber = (int)t.Number,
            Guid = t.DisplayId,
            Name = lotteryGroup.Name + " チケット",
            Description = lotteryGroup.Name,
            Warning = "当日のみ有効 本券は汚したり破らないよう大切に保管してください",
            Url = baseUrl + t.DisplayId.ToString()
        }).ToList();

        var bytes = _pdfGenerator.GenerateTicketsPdf(ticketInfo);

        return File(bytes, "application/pdf", "チケット.pdf");
    }

    public class TicketRequest
    {
        public int Count { get; set; }
        public Guid EventDisplayId { get; set; }
    }

    /// <summary>
    /// 参加登録QRの掲示用PDF(A4・1QR、設計§8)。読み取り先は {base}/entry/{eventDisplayId}。
    /// 券ではなく掲示物で、これ自体は参加証にならない。
    /// </summary>
    [Authorize(Policy = "TicketPublish")]
    [HttpGet("entry/{eventDisplayId}")]
    public async Task<IActionResult> EntryQrPoster(Guid eventDisplayId)
    {
        var ev = await _eventRepository.FindByDisplayIdAsync(eventDisplayId);
        if (ev == null)
            return NotFound("イベントが見つかりません");

        var url = $"{_baseUrlResolver.Resolve(Request)}/entry/{eventDisplayId}";
        var bytes = _pdfGenerator.GenerateQrPosterPdf(ev.Name, "参加登録QR", url, "スマートフォンのカメラで読み取って参加登録してください");
        return File(bytes, "application/pdf", $"参加登録QR_{ev.Name}.pdf");
    }

    /// <summary>
    /// チェックインQRの掲示用PDF(A4・1QR、設計§8/§4.6)。受付に掲示し、
    /// 呼び出し中グループの代表者が読み取ることで受付が確定する。読み取り先は {base}/checkin/{eventDisplayId}。
    /// </summary>
    [Authorize(Policy = "TicketPublish")]
    [HttpGet("checkin/{eventDisplayId}")]
    public async Task<IActionResult> CheckinQrPoster(Guid eventDisplayId)
    {
        var ev = await _eventRepository.FindByDisplayIdAsync(eventDisplayId);
        if (ev == null)
            return NotFound("イベントが見つかりません");

        var url = $"{_baseUrlResolver.Resolve(Request)}/checkin/{eventDisplayId}";
        var bytes = _pdfGenerator.GenerateQrPosterPdf(ev.Name, "チェックインQR", url, "そろったグループは代表者が受付で読み取ってください");
        return File(bytes, "application/pdf", $"チェックインQR_{ev.Name}.pdf");
    }

    [Authorize]
    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] Guid eventDisplayId)
    {
        var logs = _db.IssueLogs
            .Where(log => log.EventDisplayId == eventDisplayId)
            .OrderByDescending(log => log.IssuedAt)
            .ToList();

        return Ok(logs);
    }

}

