using QRQueue.Models;

namespace QRQueue.Services
{
    public interface ITicketPdfGenerator
    {
        byte[] GenerateTicketsPdf(List<TicketInfo> tickets);

        /// <summary>
        /// A4 1ページ・1QR の掲示物PDF(設計§8: 参加登録QR / チェックインQR)。
        /// 券ではなく掲示物であり、これ自体は参加証にならない。
        /// </summary>
        byte[] GenerateQrPosterPdf(string eventName, string kindLabel, string url, string instruction);
    }
}
