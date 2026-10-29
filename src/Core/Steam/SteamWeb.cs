using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SRT.Core;

// ------------------------------------------------------------ Steam web: sign-in and profile editing (Profiles page)
// Same calls as Steam's own login page and the profile edit page, the way node "steam-session" and "steamcommunity"
// make them (notes in publish/perfis-pesquisa.md). Only Valve hosts, HTTPS only, normal certificate checks.

/// <summary>A Steam answer the user can read. The message never holds a token, a cookie or a login.</summary>
public sealed class SteamWebException : Exception
{
    public int? Status { get; }
    public int? EResult { get; }
    /// <summary>Steam is limiting requests (HTTP 429 or EResult 84).</summary>
    public bool Limited => Status == 429 || EResult == 84;
    /// <summary>The saved sign-in no longer works (expired, revoked, logged out).</summary>
    public bool Expired { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public SteamWebException(string message, int? status = null, int? eresult = null) : base(message) { Status = status; EResult = eresult; }
}

/// <summary>
/// The only way the Profiles code reaches the network. Refuses anything that isn't HTTPS to a Valve host, never follows
/// redirects by itself (a redirect could leave Valve) and keeps no cookies: each call adds the ones it needs.
/// </summary>
public sealed class SteamWeb : IDisposable
{
    public static readonly string[] Hosts =
    {
        "steamcommunity.com", "api.steampowered.com", "login.steampowered.com",
        "avatars.steamstatic.com", "avatars.akamai.steamstatic.com", "avatars.cloudflare.steamstatic.com", "avatars.fastly.steamstatic.com",
    };

    public static bool Allowed(Uri u) => u is { IsAbsoluteUri: true } && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort
        && string.IsNullOrEmpty(u.UserInfo) && Hosts.Contains(u.IdnHost, StringComparer.OrdinalIgnoreCase);

    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 CS2IPChanger/0.1";
    readonly HttpClient _http;

    /// <summary>Tests pass a fake handler; the app uses the default one (certificate checks on, no cookies, no redirects).</summary>
    public SteamWeb(HttpMessageHandler handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler, true) { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task<HttpResponseMessage> Send(HttpRequestMessage req, CancellationToken ct)
    {
        if (!Allowed(req.RequestUri)) throw new SteamWebException("Blocked a request that wasn't to Steam over HTTPS.");
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        try { return await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new SteamWebException("Couldn't reach Steam (" + (ex.HttpRequestError == HttpRequestError.Unknown ? "network error" : ex.HttpRequestError.ToString()) + ")."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new SteamWebException("Steam took too long to answer."); }
    }

    /// <summary>Throws a readable error for 429 and other non-2xx answers (a 3xx is left to the caller).</summary>
    public static void Check(HttpResponseMessage r, string what)
    {
        var code = (int)r.StatusCode;
        if (code == 429)
            throw new SteamWebException("Steam is limiting requests right now.", 429) { RetryAfter = r.Headers.RetryAfter?.Delta ?? (r.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) };
        if (code >= 400) throw new SteamWebException($"Steam answered {code} to {what}.", code);
    }

    /// <summary>
    /// Multipart form without a content type on each part, like a browser form. The boundary is unquoted, like a
    /// browser's: api.steampowered.com closes the connection when it is quoted (the .NET default).
    /// </summary>
    public static MultipartFormDataContent Form(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var boundary = "----WebKitFormBoundary" + RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 16);
        var f = new MultipartFormDataContent(boundary);
        f.Headers.ContentType.Parameters.Single(p => p.Name == "boundary").Value = boundary;
        foreach (var (k, v) in fields)
        {
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes(v ?? ""));
            f.Add(part, k);
        }
        return f;
    }

    public void Dispose() => _http.Dispose();
}

// ------------------------------------------------------------ Protobuf (the few message shapes IAuthenticationService uses)

/// <summary>Writes protobuf fields by number. Enough for IAuthenticationService requests.</summary>
public sealed class ProtoWriter
{
    readonly MemoryStream _s = new();
    void Var(ulong v) { while (v >= 0x80) { _s.WriteByte((byte)(v | 0x80)); v >>= 7; } _s.WriteByte((byte)v); }
    void Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    public ProtoWriter UInt(int f, ulong v) { Tag(f, 0); Var(v); return this; }
    public ProtoWriter Int(int f, int v) { Tag(f, 0); Var((ulong)(long)v); return this; }
    public ProtoWriter Bool(int f, bool v) => UInt(f, v ? 1UL : 0UL);
    public ProtoWriter Fixed64(int f, ulong v) { Tag(f, 1); _s.Write(BitConverter.GetBytes(v)); return this; }
    public ProtoWriter Bytes(int f, byte[] b) { if (b == null) return this; Tag(f, 2); Var((ulong)b.Length); _s.Write(b); return this; }
    public ProtoWriter Str(int f, string v) => v == null ? this : Bytes(f, Encoding.UTF8.GetBytes(v));
    public ProtoWriter Msg(int f, ProtoWriter m) => Bytes(f, m.ToArray());
    public byte[] ToArray() => _s.ToArray();
}

/// <summary>Reads a protobuf message into its fields (unknown ones are kept, never an error).</summary>
public sealed class ProtoMsg
{
    readonly List<(int Field, int Wire, ulong Num, byte[] Data)> _f = new();

    public static ProtoMsg Parse(byte[] b)
    {
        var m = new ProtoMsg();
        var i = 0;
        ulong Var()
        {
            ulong v = 0; var shift = 0;
            while (true)
            {
                if (i >= b.Length || shift > 63) throw new InvalidDataException("bad protobuf");
                var x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if (x < 0x80) return v;
                shift += 7;
            }
        }
        byte[] Take(int n)
        {
            if (n < 0 || i + n > b.Length) throw new InvalidDataException("bad protobuf");
            var r = b[i..(i + n)]; i += n; return r;
        }
        while (i < b.Length)
        {
            var key = Var();
            int field = (int)(key >> 3), wire = (int)(key & 7);
            switch (wire)
            {
                case 0: m._f.Add((field, 0, Var(), null)); break;
                case 1: { var d = Take(8); m._f.Add((field, 1, BitConverter.ToUInt64(d), d)); break; }
                case 2: m._f.Add((field, 2, 0, Take((int)Var()))); break;
                case 5: { var d = Take(4); m._f.Add((field, 5, BitConverter.ToUInt32(d), d)); break; }
                default: throw new InvalidDataException("bad protobuf");
            }
        }
        return m;
    }

    public ulong Num(int f) => _f.LastOrDefault(x => x.Field == f && x.Wire is 0 or 1 or 5).Num;
    public bool Has(int f) => _f.Any(x => x.Field == f);
    public byte[] Bytes(int f) => _f.LastOrDefault(x => x.Field == f && x.Wire == 2).Data;
    public string Str(int f) => Bytes(f) is byte[] d ? Encoding.UTF8.GetString(d) : null;
    public float Float(int f) => _f.LastOrDefault(x => x.Field == f && x.Wire == 5).Data is byte[] d ? BitConverter.ToSingle(d) : 0;
    public IEnumerable<ProtoMsg> All(int f) => _f.Where(x => x.Field == f && x.Wire == 2).Select(x => Parse(x.Data));
}

// ------------------------------------------------------------ Sign-in (IAuthenticationService, as Steam's login page does it)

public enum GuardType { Unknown = 0, None = 1, EmailCode = 2, DeviceCode = 3, DeviceConfirmation = 4, EmailConfirmation = 5, MachineToken = 6 }

public sealed class AuthStart
{
    public ulong ClientId { get; set; }
    public byte[] RequestId { get; init; }
    public double Interval { get; init; }
    public List<GuardType> Guards { get; init; } = new();
    public string SteamId { get; init; }          // credentials only
    public string ChallengeUrl { get; set; }      // QR only
}

public sealed class AuthPoll
{
    public string RefreshToken { get; init; }
    public string NewChallengeUrl { get; init; }
    public bool HadRemoteInteraction { get; init; }
}

/// <summary>
/// Sign-in as a web browser (website "Community"): the result is a refresh token for steamcommunity.com, the same one the
/// browser keeps. The device shows in Steam > Authorized devices as "CS2 IP Changer", so it can be revoked there.
/// </summary>
public sealed class SteamAuth
{
    const string Api = "https://api.steampowered.com/IAuthenticationService/";
    public const string DeviceName = "CS2 IP Changer";
    const int PlatformWebBrowser = 2;
    readonly SteamWeb _web;
    public SteamAuth(SteamWeb web) { _web = web; }

    static ProtoWriter Device() => new ProtoWriter().Str(1, DeviceName).Int(2, PlatformWebBrowser);

    async Task<ProtoMsg> Call(string method, ProtoWriter body, bool get, CancellationToken ct)
    {
        var b64 = Convert.ToBase64String(body.ToArray());
        var url = $"{Api}{method}/v1/";
        var req = get
            ? new HttpRequestMessage(HttpMethod.Get, url + "?input_protobuf_encoded=" + Uri.EscapeDataString(b64))
            // Urlencoded, not multipart: to a multipart post Steam answers in JSON instead of protobuf (tested on 29 Oct 2026).
            : new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("input_protobuf_encoded", b64) }) };
        req.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
        req.Headers.Referrer = new Uri("https://steamcommunity.com/");
        using var r = await _web.Send(req, ct);
        SteamWeb.Check(r, "the sign-in");
        if ((int)r.StatusCode >= 300) throw new SteamWebException($"Steam answered {(int)r.StatusCode} to the sign-in.", (int)r.StatusCode);
        if (r.Headers.TryGetValues("x-eresult", out var er) && int.TryParse(er.FirstOrDefault(), out var e) && e != 1)
            throw new SteamWebException(EResultText(e), eresult: e);
        try { return ProtoMsg.Parse(await r.Content.ReadAsByteArrayAsync(ct)); }
        catch (InvalidDataException) { throw new SteamWebException("Steam's sign-in answer was not what the app expects."); }
    }

    /// <summary>Plain words for the EResults the sign-in gives.</summary>
    public static string EResultText(int e) => e switch
    {
        5 => "Wrong password.",
        8 => "Steam didn't accept the request.",
        9 or 27 => "The sign-in took too long. Start again.",
        15 => "Steam refused the sign-in.",
        20 => "Steam is busy. Try again in a minute.",
        63 or 65 or 88 => "Wrong code. Check it and try again.",
        84 => "Too many tries. Wait a few minutes and try again.",
        _ => $"Steam refused it (code {e}).",
    };

    /// <summary>Encrypts the password with Steam's key for this login. The plain bytes are wiped by the caller.</summary>
    public async Task<(string Encrypted, ulong Timestamp)> EncryptPassword(string login, byte[] passwordUtf8, CancellationToken ct)
    {
        var m = await Call("GetPasswordRSAPublicKey", new ProtoWriter().Str(1, login), true, ct);
        var mod = m.Str(1); var exp = m.Str(2);
        if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(exp)) throw new SteamWebException("Steam didn't send its sign-in key.");
        using var rsa = RSA.Create();
        byte[] enc;
        try
        {
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromHexString(mod.Length % 2 == 1 ? "0" + mod : mod), Exponent = Convert.FromHexString(exp.Length % 2 == 1 ? "0" + exp : exp) });
            enc = rsa.Encrypt(passwordUtf8, RSAEncryptionPadding.Pkcs1);
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { throw new SteamWebException("Steam's sign-in key was not what the app expects."); }
        return (Convert.ToBase64String(enc), m.Num(3));
    }

    public async Task<AuthStart> BeginWithCredentials(string login, string encryptedPassword, ulong timestamp, CancellationToken ct)
    {
        var body = new ProtoWriter().Str(2, login).Str(3, encryptedPassword).UInt(4, timestamp).Bool(5, true)
            .Int(6, PlatformWebBrowser).Int(7, 1).Str(8, "Community").Msg(9, Device()).UInt(11, 0);
        var m = await Call("BeginAuthSessionViaCredentials", body, false, ct);
        return new AuthStart
        {
            ClientId = m.Num(1), RequestId = m.Bytes(2), Interval = m.Float(3) is > 0 and var f ? f : 5,
            Guards = m.All(4).Select(c => (GuardType)(int)c.Num(1)).ToList(), SteamId = m.Has(5) ? m.Num(5).ToString() : null,
        };
    }

    public async Task<AuthStart> BeginWithQr(CancellationToken ct)
    {
        var body = new ProtoWriter().Str(1, DeviceName).Int(2, PlatformWebBrowser).Msg(3, Device()).Str(4, "Community");
        var m = await Call("BeginAuthSessionViaQR", body, false, ct);
        return new AuthStart
        {
            ClientId = m.Num(1), ChallengeUrl = m.Str(2), RequestId = m.Bytes(3), Interval = m.Float(4) is > 0 and var f ? f : 5,
            Guards = m.All(5).Select(c => (GuardType)(int)c.Num(1)).ToList(),
        };
    }

    public Task SubmitCode(AuthStart s, string code, GuardType type, CancellationToken ct) =>
        ulong.TryParse(s.SteamId, out var sid)
            ? Call("UpdateAuthSessionWithSteamGuardCode", new ProtoWriter().UInt(1, s.ClientId).Fixed64(2, sid).Str(3, code.Trim()).Int(4, (int)type), false, ct)
            : throw new SteamWebException("Steam's sign-in answer was not what the app expects.");

    /// <summary>Email Steam Guard on the web: Steam only sends the email after this check (no machine token here).</summary>
    public async Task SendEmailCode(AuthStart s, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://login.steampowered.com/jwt/checkdevice")
        { Content = SteamWeb.Form(new[] { KeyValuePair.Create("clientid", s.ClientId.ToString()), KeyValuePair.Create("steamid", s.SteamId) }) };
        req.Headers.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
        req.Headers.Referrer = new Uri("https://steamcommunity.com/");
        using var r = await _web.Send(req, ct);
        SteamWeb.Check(r, "the email code request");
    }

    public async Task<AuthPoll> Poll(AuthStart s, CancellationToken ct)
    {
        var m = await Call("PollAuthSessionStatus", new ProtoWriter().UInt(1, s.ClientId).Bytes(2, s.RequestId), false, ct);
        if (m.Has(1) && m.Num(1) != 0) s.ClientId = m.Num(1);
        return new AuthPoll { NewChallengeUrl = m.Str(2), RefreshToken = m.Str(3), HadRemoteInteraction = m.Num(5) != 0 };
    }
}

/// <summary>What a Steam JWT says about itself (never checked for a signature: only used to see whose and until when).</summary>
public sealed record SteamToken(string SteamId, DateTime Expires, string[] Audience)
{
    public static SteamToken Read(string jwt)
    {
        try
        {
            var parts = (jwt ?? "").Split('.');
            if (parts.Length != 3) return null;
            var p = parts[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
            var r = doc.RootElement;
            var sub = r.TryGetProperty("sub", out var s) ? s.GetString() : null;
            var exp = r.TryGetProperty("exp", out var e) && e.TryGetInt64(out var x) ? DateTimeOffset.FromUnixTimeSeconds(x).LocalDateTime : DateTime.MinValue;
            var aud = r.TryGetProperty("aud", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(v => v.GetString()).ToArray() : Array.Empty<string>();
            return sub == null ? null : new SteamToken(sub, exp, aud);
        }
        catch { return null; }
    }

    /// <summary>A refresh token for the web (what the Profiles page signs in for).</summary>
    public bool IsWebRefresh => Audience.Contains("web") && Audience.Contains("derive");
}

// ------------------------------------------------------------ steamcommunity.com, signed in

/// <summary>Current values from the profile edit page (the form overwrites every field, so all of them are read first).</summary>
public sealed class EditConfig
{
    public string PersonaName { get; set; }
    public string RealName { get; set; }
    public string Summary { get; set; }
    public string Country { get; set; }
    public string State { get; set; }
    public string City { get; set; }
    public string CustomUrl { get; set; }
}

/// <summary>Privacy as Steam keeps it: the settings object (unknown keys kept as they are) and who can comment.</summary>
public sealed class PrivacyState
{
    public JsonObject Settings { get; set; } = new();
    public int CommentPermission { get; set; }
}

/// <summary>A profile as the public XML shows it (also used to copy a look without a sign-in).</summary>
public sealed class PublicLook
{
    public string PersonaName { get; set; }
    public string RealName { get; set; }
    public string Summary { get; set; }
    public string Location { get; set; }
    public string AvatarUrl { get; set; }
    public string CustomUrl { get; set; }
    public string SteamId64 { get; set; }
}

/// <summary>
/// A Steam profile link you paste: https://steamcommunity.com/id/&lt;name&gt; or /profiles/&lt;SteamID64&gt; (http, www. and no scheme are fine),
/// or a bare SteamID64. Anything on another host, with a login or a port in it, or another page of the site is refused.
/// </summary>
public static class ProfileLink
{
    static readonly Regex Id64 = new(@"^7656119\d{10}$", RegexOptions.Compiled);
    static readonly Regex PathRx = new(@"^/(id|profiles)/([^/]+)/?$", RegexOptions.Compiled);
    static readonly Regex Vanity = new(@"^[A-Za-z0-9_-]{2,32}$", RegexOptions.Compiled);

    /// <summary>"/profiles/7656…" or "/id/name" on steamcommunity.com, or null when it isn't a profile link.</summary>
    public static string Path(string input)
    {
        var t = (input ?? "").Trim();
        if (t.Length == 0 || t.Length > 300 || t.Contains("..")) return null;
        if (Id64.IsMatch(t)) return "/profiles/" + t;
        if (t.StartsWith("steamcommunity.com/", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.steamcommunity.com/", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return null;
        if (!string.IsNullOrEmpty(u.UserInfo) || !u.IsDefaultPort) return null;
        if (!u.IdnHost.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) && !u.IdnHost.Equals("www.steamcommunity.com", StringComparison.OrdinalIgnoreCase)) return null;
        var m = PathRx.Match(u.AbsolutePath);
        if (!m.Success) return null;
        var kind = m.Groups[1].Value;
        var who = Uri.UnescapeDataString(m.Groups[2].Value);
        if (kind == "profiles") return Id64.IsMatch(who) ? "/profiles/" + who : null;
        return Vanity.IsMatch(who) ? "/id/" + who : null;
    }
}

public sealed class CommunitySession
{
    const string Base = "https://steamcommunity.com";
    readonly SteamWeb _web;
    string _secure, _profile;
    public string SteamId { get; }
    public string SessionId { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
    CommunitySession(SteamWeb web, string steamId) { _web = web; SteamId = steamId; }

    /// <summary>Refresh token to web cookies: login.steampowered.com/jwt/finalizelogin, then the steamcommunity.com transfer only.</summary>
    public static async Task<CommunitySession> Open(SteamWeb web, string refreshToken, string steamId, CancellationToken ct)
    {
        var s = new CommunitySession(web, steamId);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://login.steampowered.com/jwt/finalizelogin")
        {
            Content = SteamWeb.Form(new[] { KeyValuePair.Create("nonce", refreshToken), KeyValuePair.Create("sessionid", s.SessionId), KeyValuePair.Create("redir", Base + "/login/home/?goto=") }),
        };
        req.Headers.TryAddWithoutValidation("Origin", Base);
        req.Headers.Referrer = new Uri(Base + "/");
        JsonNode fin;
        using (var r = await web.Send(req, ct))
        {
            SteamWeb.Check(r, "the sign-in");
            try { fin = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)); } catch { fin = null; }
        }
        if (fin?["error"] is JsonNode err)
        {
            var e = err.GetValueKind() == JsonValueKind.Number ? (int)err : 0;
            throw new SteamWebException(e == 84 ? "Steam is limiting requests right now." : "The saved sign-in no longer works. Sign in again.", eresult: e) { Expired = e != 84 };
        }
        if (fin?["steamID"]?.ToString() is string sid && sid != steamId) throw new SteamWebException("The saved sign-in is for another account. Sign in again.") { Expired = true };
        var transfer = (fin?["transfer_info"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(t => Uri.TryCreate(t["url"]?.ToString(), UriKind.Absolute, out var u) && u.Host == "steamcommunity.com" && SteamWeb.Allowed(u));
        if (transfer == null) throw new SteamWebException("Steam's sign-in answer was not what the app expects.");

        var fields = new List<KeyValuePair<string, string>> { KeyValuePair.Create("steamID", steamId) };
        if (transfer["params"] is JsonObject ps) foreach (var (k, v) in ps) fields.Add(KeyValuePair.Create(k, v?.ToString() ?? ""));
        using (var r = await web.Send(new HttpRequestMessage(HttpMethod.Post, transfer["url"]!.ToString()) { Content = SteamWeb.Form(fields) }, ct))
        {
            SteamWeb.Check(r, "the sign-in");
            s._secure = r.Headers.TryGetValues("Set-Cookie", out var cs)
                ? cs.Select(c => Regex.Match(c, @"^steamLoginSecure=([^;]+)")).FirstOrDefault(m => m.Success && m.Groups[1].Value.Length > 10)?.Groups[1].Value
                : null;
        }
        if (s._secure == null) throw new SteamWebException("The saved sign-in no longer works. Sign in again.") { Expired = true };
        return s;
    }

    HttpRequestMessage Req(HttpMethod m, string url, HttpContent body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body };
        // The cookies only ever go to steamcommunity.com (Send refuses other hosts anyway).
        if (req.RequestUri!.Host == "steamcommunity.com")
            req.Headers.TryAddWithoutValidation("Cookie", $"steamLoginSecure={_secure}; sessionid={SessionId}; Steam_Language=english");
        req.Headers.TryAddWithoutValidation("Origin", Base);
        req.Headers.Referrer = new Uri(Base + "/");
        return req;
    }

    async Task<string> Text(HttpRequestMessage req, string what, CancellationToken ct)
    {
        using var r = await _web.Send(req, ct);
        SteamWeb.Check(r, what);
        if ((int)r.StatusCode is >= 300 and < 400)
        {
            var to = r.Headers.Location?.ToString() ?? "";
            throw new SteamWebException(to.Contains("/login", StringComparison.OrdinalIgnoreCase) ? "The saved sign-in no longer works. Sign in again." : $"Steam sent the app somewhere else for {what}.") { Expired = to.Contains("/login", StringComparison.OrdinalIgnoreCase) };
        }
        return await r.Content.ReadAsStringAsync(ct);
    }

    /// <summary>"/id/name" or "/profiles/7656…", from /my. Also proves the cookies work, and that they are this account's.</summary>
    public async Task<string> ProfilePath(CancellationToken ct)
    {
        if (_profile != null) return _profile;
        using var r = await _web.Send(Req(HttpMethod.Get, Base + "/my"), ct);
        SteamWeb.Check(r, "the profile");
        var to = r.Headers.Location;
        if (to != null && !to.IsAbsoluteUri) to = new Uri(new Uri(Base), to);
        var m = to != null && to.Host == "steamcommunity.com" ? Regex.Match(to.AbsolutePath, @"^(/(?:id|profiles)/[^/]+)/?$") : Match.Empty;
        if (!m.Success) throw new SteamWebException("The saved sign-in no longer works. Sign in again.") { Expired = true };
        if (m.Groups[1].Value.StartsWith("/profiles/") && m.Groups[1].Value != "/profiles/" + SteamId)
            throw new SteamWebException("The saved sign-in is for another account. Sign in again.") { Expired = true };
        return _profile = m.Groups[1].Value;
    }

    static JsonNode EditJson(string html)
    {
        var tag = Regex.Match(html, @"<[^>]*\bid=""profile_edit_config""[^>]*>", RegexOptions.IgnoreCase);
        var attr = tag.Success ? Regex.Match(tag.Value, @"data-profile-edit=""([^""]*)""") : Match.Empty;
        if (!attr.Success) throw new SteamWebException("Steam's profile page was not what the app expects.");
        try { return JsonNode.Parse(WebUtility.HtmlDecode(attr.Groups[1].Value)); }
        catch (JsonException) { throw new SteamWebException("Steam's profile page was not what the app expects."); }
    }

    public static EditConfig ParseEditInfo(string html)
    {
        var j = EditJson(html);
        string S(JsonNode n) => n == null ? "" : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToString();
        if (j?["strPersonaName"] == null) throw new SteamWebException("Steam's profile page was not what the app expects.");
        var loc = j["LocationData"];
        return new EditConfig
        {
            PersonaName = S(j["strPersonaName"]), RealName = S(j["strRealName"]), Summary = S(j["strSummary"]), CustomUrl = S(j["strCustomURL"]),
            Country = S(loc?["locCountryCode"]), State = S(loc?["locStateCode"]), City = S(loc?["locCityCode"]),
        };
    }

    public static PrivacyState ParsePrivacy(string html)
    {
        var p = EditJson(html)?["Privacy"];
        if (p?["PrivacySettings"] is not JsonObject set) throw new SteamWebException("Steam's privacy page was not what the app expects.");
        return new PrivacyState { Settings = (JsonObject)set.DeepClone(), CommentPermission = p["eCommentPermission"] is JsonNode c && c.GetValueKind() == JsonValueKind.Number ? (int)c : 1 };
    }

    public async Task<EditConfig> ReadInfo(CancellationToken ct) => ParseEditInfo(await Text(Req(HttpMethod.Get, Base + await ProfilePath(ct) + "/edit/info"), "the profile", ct));
    public async Task<PrivacyState> ReadPrivacy(CancellationToken ct) => ParsePrivacy(await Text(Req(HttpMethod.Get, Base + await ProfilePath(ct) + "/edit/settings"), "the privacy settings", ct));

    static void Success(string json, string what)
    {
        JsonNode j;
        try { j = JsonNode.Parse(json); } catch { throw new SteamWebException($"Steam's answer to {what} was not what the app expects."); }
        var ok = j?["success"] is JsonNode s && (s.GetValueKind() == JsonValueKind.True || (s.GetValueKind() == JsonValueKind.Number && (int)s == 1));
        if (ok) return;
        var msg = j?["errmsg"]?.ToString() ?? j?["message"]?.ToString();
        msg = string.IsNullOrWhiteSpace(msg) ? null : Regex.Replace(WebUtility.HtmlDecode(msg), "<[^>]+>", " ").Trim();
        var code = j?["success"] is JsonNode n && n.GetValueKind() == JsonValueKind.Number ? (int)n : (int?)null;
        throw new SteamWebException(msg != null ? $"Steam said: {Shorten(msg, 160)}" : code == 84 ? "Steam is limiting requests right now." : $"Steam refused {what}.", eresult: code);
    }

    static string Shorten(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>The edit form (from <see cref="ProfileForm.Build"/>).</summary>
    public async Task SaveInfo(IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        Success(await Text(Req(HttpMethod.Post, Base + await ProfilePath(ct) + "/edit/", new FormUrlEncodedContent(form)), "the profile change", ct), "the profile change");
        // The custom URL may have just changed: the old /id/<name> no longer leads here, /profiles/<SteamID> always does.
        _profile = "/profiles/" + SteamId;
    }

    public async Task SetPrivacy(PrivacyState p, CancellationToken ct) =>
        Success(await Text(Req(HttpMethod.Post, Base + await ProfilePath(ct) + "/ajaxsetprivacy/", SteamWeb.Form(new[]
        {
            KeyValuePair.Create("sessionid", SessionId), KeyValuePair.Create("Privacy", p.Settings.ToJsonString()), KeyValuePair.Create("eCommentPermission", p.CommentPermission.ToString()),
        })), "the privacy change", ct), "the privacy change");

    /// <summary>Uploads an image already checked and re-encoded by <see cref="AvatarImage.Prepare"/>.</summary>
    public async Task UploadAvatar(byte[] jpeg, CancellationToken ct)
    {
        var form = SteamWeb.Form(new[]
        {
            KeyValuePair.Create("MAX_FILE_SIZE", jpeg.Length.ToString()), KeyValuePair.Create("type", "player_avatar_image"), KeyValuePair.Create("sId", SteamId),
            KeyValuePair.Create("sessionid", SessionId), KeyValuePair.Create("doSub", "1"), KeyValuePair.Create("json", "1"),
        });
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "avatar", "avatar.jpg");
        Success(await Text(Req(HttpMethod.Post, Base + "/actions/FileUploader/", form), "the picture upload", ct), "the picture upload");
    }

    /// <summary>Clears "previously known as". Permanent: Steam has no way back.</summary>
    public async Task ClearAliases(CancellationToken ct) =>
        Success(await Text(Req(HttpMethod.Post, Base + await ProfilePath(ct) + "/ajaxclearaliashistory/", new FormUrlEncodedContent(new[] { KeyValuePair.Create("sessionid", SessionId) })), "clearing the old names", ct), "clearing the old names");

    public async Task<PublicLook> ReadLook(CancellationToken ct) => ParseLook(await Text(Req(HttpMethod.Get, $"{Base}/profiles/{SteamId}/?xml=1"), "the profile", ct));

    /// <summary>The public XML, without a sign-in (Copy from account when the account has no session).</summary>
    public static Task<PublicLook> ReadPublicLook(SteamWeb web, string steamId, CancellationToken ct) => ReadPublicLookAt(web, "/profiles/" + steamId, ct);

    /// <summary>The public XML of any profile, by its path on steamcommunity.com (from <see cref="ProfileLink.Path"/>).</summary>
    public static async Task<PublicLook> ReadPublicLookAt(SteamWeb web, string profilePath, CancellationToken ct)
    {
        if (profilePath == null || !Regex.IsMatch(profilePath, @"^/(id/[A-Za-z0-9_-]{2,32}|profiles/7656119\d{10})$")) throw new SteamWebException("That isn't a Steam profile link.");
        using var r = await web.Send(new HttpRequestMessage(HttpMethod.Get, $"{Base}{profilePath}/?xml=1"), ct);
        SteamWeb.Check(r, "the profile");
        if ((int)r.StatusCode >= 300) throw new SteamWebException("Steam didn't show this profile.");
        return ParseLook(await r.Content.ReadAsStringAsync(ct));
    }

    public static PublicLook ParseLook(string xml)
    {
        System.Xml.Linq.XElement x;
        try { x = System.Xml.Linq.XDocument.Parse(xml).Root; } catch { throw new SteamWebException("Steam didn't show this profile."); }
        if (x == null || x.Element("error") != null) throw new SteamWebException("Steam didn't show this profile.");
        string E(string n) => x.Element(n)?.Value?.Trim();
        // The XML gives the bio as HTML: line breaks back, tags out.
        var sum = E("summary");
        if (sum != null) sum = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(sum, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", "")).Trim();
        if (sum == "No information given.") sum = "";
        return new PublicLook { PersonaName = E("steamID"), RealName = E("realname"), Summary = sum, Location = E("location"), AvatarUrl = E("avatarFull"), CustomUrl = E("customURL"), SteamId64 = E("steamID64") };
    }

    /// <summary>Steam's default picture (the "?"): its hash is in the avatar URL of every profile that has it.</summary>
    public const string DefaultAvatarHash = "fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb";
    public const string DefaultAvatarUrl = "https://avatars.steamstatic.com/" + DefaultAvatarHash + "_full.jpg";
    public static bool IsDefaultAvatar(string url) => url != null && url.Contains(DefaultAvatarHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>An avatar from Steam's CDN (no cookies go there). Null when it isn't a Valve URL or not an image.</summary>
    public static async Task<byte[]> DownloadAvatar(SteamWeb web, string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !SteamWeb.Allowed(u) || !u.Host.StartsWith("avatars", StringComparison.OrdinalIgnoreCase)) return null;
        using var r = await web.Send(new HttpRequestMessage(HttpMethod.Get, u), ct);
        if (!r.IsSuccessStatusCode || r.Content.Headers.ContentLength > AvatarImage.MaxInput) return null;
        var bytes = await r.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length is > 0 and <= AvatarImage.MaxInput ? bytes : null;
    }
}
