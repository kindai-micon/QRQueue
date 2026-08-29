using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace QRQueue.Services;

/// <summary>
/// QRコードに埋め込む BaseURL の解決を共通化する(設計 §8)。
/// 解決順: 設定 `LotteryBaseUrl` → リクエスト Host。
/// host が localhost/127.0.0.1 のときは LAN のローカルIPへ変換し、
/// 非標準ポートのみポート番号を付与する(既存 `TicketPdfController` の規則と同一)。
/// </summary>
public interface IBaseUrlResolver
{
    /// <summary>末尾スラッシュなしの BaseURL(例: `http://192.168.0.5:5080`)を返す</summary>
    string Resolve(HttpRequest request);
}

public class BaseUrlResolver : IBaseUrlResolver
{
    private readonly IConfiguration _configuration;

    public BaseUrlResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Resolve(HttpRequest request)
    {
        var baseUrl = _configuration["LotteryBaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            return baseUrl.TrimEnd('/');
        }

        var useHttps = _configuration.GetValue<bool?>("UseHttpsForQrCode");
        var scheme = useHttps.HasValue
            ? (useHttps.Value ? "https" : "http")
            : request.Scheme;

        var host = request.Host.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            host = GetLocalIPAddress();
        }

        var port = request.Host.Port ?? (scheme == "https" ? 443 : 80);
        var portString = (scheme == "https" && port != 443) || (scheme == "http" && port != 80)
            ? $":{port}"
            : "";

        return $"{scheme}://{host}{portString}";
    }

    private static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "localhost";
    }
}
