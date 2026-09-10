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
    /// 参加登録QRの掲示用PDF(A4・1QR)。読み取り先は {base}/entry/{eventDisplayId}。
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

    // issue #76: 固定のチェックインQR掲示PDFは廃止。
    // 到着確認コードは30秒で回転するため、印刷した固定QRは共有・再利用対策にならない。
    // 代わりに受付画面 /checkin-qr/{eventDisplayId} で自動更新QRを表示する。
}
