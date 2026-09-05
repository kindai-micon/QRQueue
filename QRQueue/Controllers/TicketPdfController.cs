using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Repositories;
using QRQueue.Services;

[Route("api/pdf")]
[ApiController]
public class TicketPdfController : ControllerBase
{
    private readonly IEventRepository _eventRepository;
    private readonly IBaseUrlResolver _baseUrlResolver;
    private readonly ITicketPdfGenerator _pdfGenerator;

    public TicketPdfController(
        IEventRepository eventRepository,
        IBaseUrlResolver baseUrlResolver,
        ITicketPdfGenerator pdfGenerator)
    {
        _eventRepository = eventRepository;
        _baseUrlResolver = baseUrlResolver;
        _pdfGenerator = pdfGenerator;
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
}
