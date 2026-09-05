using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;

namespace QRQueue.Services
{
    public class TicketPdfGenerator(IQrCodeGenerator qrCodeGenerator) : ITicketPdfGenerator
    {
        public byte[] GenerateQrPosterPdf(string eventName, string kindLabel, string url, string instruction)
        {
            // 掲示物は遠目から読み取るため高解像度で生成
            var qrPng = qrCodeGenerator.GeneratePng(url, 1000, 1000);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontFamily("Noto Sans JP"));

                    page.Content().Column(col =>
                    {
                        // タイトル(参加登録QR / チェックインQR)
                        col.Item().AlignCenter().Text(kindLabel).FontSize(44).Bold();

                        // イベント名
                        col.Item().AlignCenter().PaddingTop(8)
                            .Text(eventName).FontSize(26).SemiBold();

                        // 大きな QR(中央)
                        col.Item().PaddingTop(30).AlignCenter().Width(440)
                            .Image(qrPng, ImageScaling.FitWidth);

                        // 読み取り案内
                        col.Item().AlignCenter().PaddingTop(28)
                            .Text(instruction).FontSize(18).Medium();

                        // 補足: フォールバック用の素 URL(券ではなく掲示物 §8)
                        col.Item().AlignCenter().PaddingTop(14)
                            .Text($"直接入力: {url}").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf();
        }
    }
}
