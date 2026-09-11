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
    private readonly ICheckinCodeService _checkinCodeService;

    public TicketPdfController(
        IEventRepository eventRepository,
        IBaseUrlResolver baseUrlResolver,
        ITicketPdfGenerator pdfGenerator,
        ICheckinCodeService checkinCodeService)
    {
        _eventRepository = eventRepository;
        _baseUrlResolver = baseUrlResolver;
        _pdfGenerator = pdfGenerator;
        _checkinCodeService = checkinCodeService;
    }

    /// <summary>
    /// 参加登録QRの掲示用PDF(A4・1QR・固定)。読み取り先は {base}/entry/{eventDisplayId}?rc={固定コード}。
    /// Web掲示画面とは異なり失効しないため、印刷物での参加登録が常に可能。
    /// 券ではなく掲示物で、これ自体は参加証にならない。
    /// </summary>
    [Authorize(Policy = "TicketPublish")]
    [HttpGet("entry/{eventDisplayId}")]
    public async Task<IActionResult> EntryQrPoster(Guid eventDisplayId)
    {
        var ev = await _eventRepository.FindByDisplayIdAsync(eventDisplayId);
        if (ev == null)
            return NotFound("イベントが見つかりません");

        var code = await _checkinCodeService.GetStaticPosterCodeAsync(eventDisplayId);
        var url = $"{_baseUrlResolver.Resolve(Request)}/entry/{eventDisplayId}?rc={code}";
        var bytes = _pdfGenerator.GenerateQrPosterPdf(ev.Name, "参加登録QR(固定)", url, "スマートフォンのカメラで読み取って参加登録してください");
        return File(bytes, "application/pdf", $"参加登録QR_{ev.Name}.pdf");
    }

    // issue #76: 回転する到着確認コードによる受付確認QR画面(/checkin-qr/{eventDisplayId})と併存。
    // 固定QR(下記)は失効しないため撮影・共有・再利用が可能になる点に注意。
    /// <summary>
    /// 固定チェックインQRの掲示用PDF(A4・1QR)。読み取り先は {base}/checkin/{eventDisplayId}?rc={固定コード}。
    /// 自動更新画面を設置できない受付向けの代替手段。印刷物は失効しないため、
    /// 撮影・共有されたURLからもチェックイン可能になる(運用判断で発行すること)。
    /// </summary>
    [Authorize(Policy = "TicketPublish")]
    [HttpGet("checkin/{eventDisplayId}")]
    public async Task<IActionResult> CheckinQrPoster(Guid eventDisplayId)
    {
        var ev = await _eventRepository.FindByDisplayIdAsync(eventDisplayId);
        if (ev == null)
            return NotFound("イベントが見つかりません");

        var code = await _checkinCodeService.GetStaticPosterCodeAsync(eventDisplayId);
        var url = $"{_baseUrlResolver.Resolve(Request)}/checkin/{eventDisplayId}?rc={code}";
        var bytes = _pdfGenerator.GenerateQrPosterPdf(ev.Name, "チェックインQR(固定)", url, "呼び出し中のグループの代表者がスマートフォンのカメラで読み取って受付を確定してください");
        return File(bytes, "application/pdf", $"チェックインQR_{ev.Name}.pdf");
    }
}
