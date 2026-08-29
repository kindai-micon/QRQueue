using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace QRQueue.Services;

public interface IQrCodeGenerator
{
    /// <summary>QRコードを生成してPNGバイト列で返す(設計§8)</summary>
    byte[] GeneratePng(string text, int width = 150, int height = 150);
}

public class QrCodeGenerator : IQrCodeGenerator
{
    public byte[] GeneratePng(string text, int width = 150, int height = 150)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions
            {
                Height = height,
                Width = width,
                Margin = 1
            }
        };

        var pixelData = writer.Write(text);

        using var image = Image.LoadPixelData<Rgba32>(
            pixelData.Pixels, pixelData.Width, pixelData.Height
        );

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
