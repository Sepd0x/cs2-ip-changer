using System.Collections;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SRT.Core;

public static partial class Net
{
    // ------------------------------------------------------------ Public IP (what servers see)

    /// <summary>
    /// Fixed address (Cloudflare) that returns the IP of whoever asks. Protection gives it a route through the
    /// protected connection and puts it in the kill switch, so the answer is ALWAYS the connection's IP: if the phone drops, the request fails instead of lying.
    /// </summary>
    public const string EchoIp = "1.0.0.1";

    static readonly string[] HomeEndpoints =
    {
        "https://api.ipify.org",
        "https://ipv4.icanhazip.com",
        "https://checkip.amazonaws.com",
    };

    /// <summary>
    /// HTTP client that only connects if Windows will really send the request through adapter <paramref name="requiredIf"/>.
    /// Binding to the local IP alone is not trusted: on some PCs Windows ignores it.
    /// </summary>
    static HttpClient ClientVia(int requiredIf, string bindLocalIp, int timeoutSec = 7)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(timeoutSec),
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = async (ctx, ct) =>
            {
                var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, AddressFamily.InterNetwork, ct);
                var ok = addrs.Where(a => BestInterface(a.ToString()) == requiredIf).ToArray();
                if (ok.Length == 0) throw new HttpRequestException($"Windows would not send {ctx.DnsEndPoint.Host} through adapter {requiredIf}.");
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    if (bindLocalIp != null) s.Bind(new IPEndPoint(IPAddress.Parse(bindLocalIp), 0));
                    await s.ConnectAsync(ok[0], ctx.DnsEndPoint.Port, ct);
                    return new NetworkStream(s, true);
                }
                catch { s.Dispose(); throw; }
            },
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec + 1) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger/0.1");
        return http;
    }

    /// <summary>The public IP servers see (through the protected connection).</summary>
    public static async Task<string> EgressPublicIp(Adapter egress, CancellationToken ct = default)
    {
        if (egress == null || BestInterface(EchoIp) != egress.Index) return null;
        using var http = ClientVia(egress.Index, egress.LocalIp);
        try
        {
            var trace = await http.GetStringAsync($"https://{EchoIp}/cdn-cgi/trace", ct);
            var line = trace.Split('\n').FirstOrDefault(l => l.StartsWith("ip="));
            var ip = line?[3..].Trim();
            return IPAddress.TryParse(ip, out var a) && a.AddressFamily == AddressFamily.InterNetwork ? ip : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>The public IP of the rest of the PC (home network). Only answers if the request really leaves through that adapter.</summary>
    public static async Task<string> HomePublicIp(int homeIf, CancellationToken ct = default)
    {
        if (homeIf <= 0) return null;
        using var http = ClientVia(homeIf, null);
        foreach (var url in HomeEndpoints)
        {
            try
            {
                var s = (await http.GetStringAsync(url, ct)).Trim();
                if (IPAddress.TryParse(s, out var a) && a.AddressFamily == AddressFamily.InterNetwork) return s;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        return null;
    }

    /// <summary>Public IP with no conditions (tests and diagnostics).</summary>
    public static async Task<string> AnyPublicIp()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var url in HomeEndpoints)
        {
            try
            {
                var s = (await http.GetStringAsync(url)).Trim();
                if (IPAddress.TryParse(s, out _)) return s;
            }
            catch { }
        }
        return null;
    }

    public sealed record IpInfo(string Isp, string City, string Country, bool Mobile, bool Proxy)
    {
        public string Short => string.Join(" · ", new[] { Isp, City, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    static readonly Dictionary<string, IpInfo> InfoCache = new();

    public static async Task<IpInfo> Lookup(string ip)
    {
        if (ip == null) return null;
        lock (InfoCache) if (InfoCache.TryGetValue(ip, out var c)) return c;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var json = await http.GetStringAsync($"http://ip-api.com/json/{ip}?fields=status,country,city,isp,mobile,proxy");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("status").GetString() != "success") return null;
            var info = new IpInfo(
                r.GetProperty("isp").GetString(), r.GetProperty("city").GetString(), r.GetProperty("country").GetString(),
                r.GetProperty("mobile").GetBoolean(), r.GetProperty("proxy").GetBoolean());
            lock (InfoCache) InfoCache[ip] = info;
            return info;
        }
        catch { return null; }
    }
}
