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
        if (egress == null) { LastEgressError = "no phone connection"; return null; }
        if (BestInterface(EchoIp) != egress.Index) { LastEgressError = $"Windows would send {EchoIp} through another adapter"; return null; }
        using var http = ClientVia(egress.Index, egress.LocalIp);
        try
        {
            var trace = await http.GetStringAsync($"https://{EchoIp}/cdn-cgi/trace", ct);
            var line = trace.Split('\n').FirstOrDefault(l => l.StartsWith("ip="));
            var ip = line?[3..].Trim();
            if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) { LastEgressError = "the answer had no IPv4"; return null; }
            LastEgressError = null;
            // The country Cloudflare saw: what the phone's line shows when ipwho.is can't be asked through the phone
            if (CountryOf(trace.Split('\n').FirstOrDefault(l => l.StartsWith("loc="))?[4..].Trim()) is string country)
                lock (InfoCache) TraceCountry[ip] = country;
            return ip;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { LastEgressError = ex is TaskCanceledException ? "timed out" : ex.GetType().Name + ": " + ex.Message; return null; }
    }

    /// <summary>Why the last check through the phone gave no IP (null when it answered). For the Log, when checks keep failing.</summary>
    public static string LastEgressError { get; private set; }

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
    static readonly Dictionary<string, string> TraceCountry = new();

    /// <summary>"PT" to "Portugal" (the name ipwho.is uses); null for anything that isn't a country code.</summary>
    public static string CountryOf(string code)
    {
        if (code == null || !Regex.IsMatch(code, "^[A-Z]{2}$") || code is "XX" or "T1") return null;
        try { return new System.Globalization.RegionInfo(code).EnglishName; }
        catch { return null; }
    }

    /// <summary>
    /// ISP, city and country of an IP (ipwho.is), asked only through the connection that IP belongs to (<paramref name="viaIf"/>):
    /// the phone's IP through the phone, the home IP through home. Asked through home, ipwho.is would see both IPs from the same
    /// place and could tie the phone's IP to your home IP. When Windows wouldn't send the request through that connection (the phone
    /// only carries the game), nothing is asked: the phone's line shows the country Cloudflare saw in the IP test, or nothing.
    /// </summary>
    public static async Task<IpInfo> Lookup(string ip, int viaIf, string bindLocalIp = null)
    {
        if (ip == null) return null;
        IpInfo Fallback() { lock (InfoCache) return TraceCountry.TryGetValue(ip, out var country) ? new IpInfo(null, null, country, false, false) : null; }
        lock (InfoCache) if (InfoCache.TryGetValue(ip, out var c)) return c;
        if (viaIf <= 0) return Fallback();
        try
        {
            // HTTPS only: the home IP and the phone's IP don't travel in the clear.
            using var http = ClientVia(viaIf, bindLocalIp, 6);
            var json = await http.GetStringAsync($"https://ipwho.is/{ip}");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
            static string S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var conn = r.TryGetProperty("connection", out var c) ? c : default;
            var isp = S(conn, "isp");
            var info = new IpInfo(string.IsNullOrWhiteSpace(isp) ? S(conn, "org") : isp, S(r, "city"), S(r, "country"), false, false);
            lock (InfoCache) InfoCache[ip] = info;
            return info;
        }
        catch { return Fallback(); }
    }
}
