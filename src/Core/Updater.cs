using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Update from the GitHub releases: only installs an exe whose SHA-256 matches the .sha256 file of the same release,
/// and only if that .sha256 (with the version) is signed with the project's key (.sha256.sig). The private half of the key stays
/// off GitHub, so someone who gets into the repo or its releases can't sign an exe of their own (tools/release-key.mjs).
/// </summary>
public static partial class Updater
{
    public const string Repo = "Sepd0x/cs2-ip-changer";
    public const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    public const string ExeName = "CS2IPChanger.exe";

    /// <summary>A release on GitHub. <see cref="Size"/> is the exe's size in bytes (0 = not known).</summary>
    public sealed record Release(string Version, string Tag, string PageUrl, string ExeUrl, string ShaUrl, long Size = 0, string SigUrl = null);

    // ------------------------------------------------------------ Versions (SemVer 2.0)

    static readonly Regex SemVer = new(@"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$", RegexOptions.Compiled);

    /// <summary>"v0.1.0-alpha.3+dev" -> "0.1.0-alpha.3".</summary>
    public static string Clean(string v)
    {
        v = (v ?? "").Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v[1..];
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    public static bool IsValid(string v) => SemVer.IsMatch(Clean(v));

    /// <summary>&lt; 0 if a is older than b. An invalid version is older than any valid one.</summary>
    public static int Compare(string a, string b)
    {
        var ma = SemVer.Match(Clean(a));
        var mb = SemVer.Match(Clean(b));
        if (!ma.Success || !mb.Success) return ma.Success.CompareTo(mb.Success);
        for (var i = 1; i <= 3; i++)
        {
            var c = NumCmp(ma.Groups[i].Value, mb.Groups[i].Value);
            if (c != 0) return c;
        }
        var pa = ma.Groups[4].Success ? ma.Groups[4].Value.Split('.') : null;
        var pb = mb.Groups[4].Success ? mb.Groups[4].Value.Split('.') : null;
        if (pa == null || pb == null) return (pa == null).CompareTo(pb == null);   // a release beats its pre-releases
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = pa[i].All(char.IsAsciiDigit);
            var nb = pb[i].All(char.IsAsciiDigit);
            var c = na && nb ? NumCmp(pa[i], pb[i])
                : na != nb ? (na ? -1 : 1)
                : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return Math.Sign(c);
        }
        return pa.Length.CompareTo(pb.Length);
    }

    // Digits of any length: "10" > "9" without overflowing.
    static int NumCmp(string a, string b)
    {
        a = a.TrimStart('0'); b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : Math.Sign(string.CompareOrdinal(a, b));
    }

    public static bool IsNewer(string latest, string current) => IsValid(latest) && Compare(latest, current) > 0;

    // ------------------------------------------------------------ The release list

    /// <summary>The highest version in the /releases answer (drafts left out). Links outside this repo are dropped.</summary>
    public static Release PickLatest(string json)
    {
        Release best = null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            var tag = Str(r, "tag_name");
            if (!IsValid(tag)) continue;
            string exe = null, sha = null, sig = null;
            long size = 0;
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (!IsRepoUrl(url, "/releases/download/")) continue;
                    if (name == ExeName)
                    {
                        exe = url;
                        if (a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n)) size = n;
                    }
                    else if (name == ExeName + ".sha256") sha = url;
                    else if (name == ExeName + ".sha256.sig") sig = url;
                }
            var page = Str(r, "html_url");
            var rel = new Release(Clean(tag), tag, IsRepoUrl(page, "/releases/") ? page : ReleasesPage, exe, sha, size, sig);
            if (best == null || Compare(rel.Version, best.Version) > 0) best = rel;
        }
        return best;
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool IsRepoUrl(string url, string under) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com"
        && u.AbsolutePath.StartsWith("/" + Repo + under, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "&lt;sha256&gt;  CS2IPChanger.exe" (sha256sum format, also with "*"), and nothing else in the file. Null otherwise.
    /// A file with more lines is refused: tools/release-key.mjs only signs the exact line the Release workflow writes, and a second
    /// line could carry another hash past it.
    /// </summary>
    public static string ParseSha256(string text, string fileName = ExeName)
    {
        var lines = (text ?? "").Replace("﻿", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count != 1) return null;
        var m = Regex.Match(lines[0], @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$");
        if (!m.Success) return null;
        var name = m.Groups[2].Success ? Path.GetFileName(m.Groups[2].Value.Trim()) : null;
        return name == null || name.Equals(fileName, StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    static HttpClient Http(int timeoutSec)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger/0.1");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public const string LatestPage = "https://github.com/" + Repo + "/releases/latest";

    /// <summary>
    /// The newest release. The API first; when it refuses (60 requests an hour per IP without a token, and mobile data shares one IP
    /// between many people, audit C6) or fails, the redirect of github.com/.../releases/latest, which has no such limit. The
    /// download is the same either way: only an exe with the project's signature is installed.
    /// </summary>
    public static async Task<Release> Latest(CancellationToken ct = default)
    {
        if (Rec.On) return null;   // the recording build never replaces itself with the normal one
        try
        {
            using var http = Http(15);
            return PickLatest(await http.GetStringAsync(ApiUrl + "?per_page=50", ct));
        }
        catch (Exception api) when (!ct.IsCancellationRequested)
        {
            try
            {
                using var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
                h.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger/0.1");
                using var resp = await h.GetAsync(LatestPage, HttpCompletionOption.ResponseHeadersRead, ct);
                if (FromLatestRedirect(resp.Headers.Location) is { } r) return r;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            throw new HttpRequestException($"{api.Message} (and github.com/…/releases/latest didn't answer)", api);
        }
    }

    /// <summary>
    /// The release that "releases/latest" redirects to (".../releases/tag/v0.1.0-beta.11"). The tag must be a version, so nothing
    /// else gets into the download links; the files are the ones each release has, next to the tag.
    /// </summary>
    public static Release FromLatestRedirect(Uri location)
    {
        if (location == null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps || location.Host != "github.com") return null;
        var prefix = "/" + Repo + "/releases/tag/";
        if (!location.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var tag = Uri.UnescapeDataString(location.AbsolutePath[prefix.Length..]);
        if (!IsValid(tag) || tag.Contains('/') || tag.Contains('+')) return null;
        var dl = $"https://github.com/{Repo}/releases/download/{tag}/{ExeName}";
        return new Release(Clean(tag), tag, $"https://github.com/{Repo}/releases/tag/{tag}", dl, dl + ".sha256", 0, dl + ".sha256.sig");
    }

    // ------------------------------------------------------------ The release signature

    /// <summary>What the key signs: "CS2IPChanger &lt;version&gt;\n" followed by the .sha256 file as it is. The version is in it,
    /// so an old signed release can't come back under a newer tag.</summary>
    public static byte[] SignedMessage(string version, byte[] shaFile) => [.. Encoding.UTF8.GetBytes($"CS2IPChanger {Clean(version)}\n"), .. shaFile];

    /// <summary>ECDSA P-256 / SHA-256, signature in DER and base64 (what Node's crypto.sign gives). False with no key in the build.</summary>
    public static bool SignedByProject(string version, byte[] shaFile, string sigBase64, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(sigBase64) || shaFile == null) return false;
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ec.VerifyData(SignedMessage(version, shaFile), Convert.FromBase64String(sigBase64.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------ Download and swap

    /// <summary>Only the single-file exe can replace itself (a dev build has its DLLs next to it).</summary>
    public static bool CanSelfUpdate => !Rec.On &&
        Environment.ProcessPath != null && !File.Exists(Path.Combine(AppContext.BaseDirectory, "CS2IPChanger.dll"));

    static string Dir => Path.GetDirectoryName(Environment.ProcessPath)!;
    static string NewExe => Path.Combine(Dir, "CS2IPChanger.new.exe");
    static string OldExe => Environment.ProcessPath + ".old";

    public static string Sha256Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>Downloads the exe as CS2IPChanger.new.exe next to the app. Returns its SHA-256, already checked.</summary>
    public static async Task<string> Download(Release r, IProgress<string> progress, CancellationToken ct = default)
    {
        using var http = Http(600);
        if (r.SigUrl == null) throw new InvalidDataException("the release isn't signed, so nothing was installed");
        var shaFile = await http.GetByteArrayAsync(r.ShaUrl, ct);
        if (!SignedByProject(r.Version, shaFile, await http.GetStringAsync(r.SigUrl, ct)))
            throw new InvalidDataException("the release isn't signed with the project's key, so nothing was installed");
        var want = ParseSha256(Encoding.UTF8.GetString(shaFile))
            ?? throw new InvalidDataException("the release's .sha256 file has no hash for " + ExeName);
        var part = NewExe + ".part";
        using (var resp = await http.GetAsync(r.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            if (resp.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("the download left HTTPS");
            var total = resp.Content.Headers.ContentLength;
            if (total > 400_000_000) throw new InvalidDataException("the file is too big");
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var fs = File.Create(part);
            var buf = new byte[81920];
            long got = 0, shown = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                if (got > 400_000_000) throw new InvalidDataException("the file is too big");
                if (got - shown >= 1_000_000)
                {
                    shown = got;
                    progress?.Report(total > 0 ? $"Downloading the update… {got * 100 / total} %" : $"Downloading the update… {got / 1_000_000} MB");
                }
            }
        }
        progress?.Report("Checking the SHA-256…");
        var have = Sha256Of(part);
        if (have != want)
        {
            try { File.Delete(part); } catch { }
            throw new InvalidDataException("the SHA-256 doesn't match the release, so nothing was installed");
        }
        File.Move(part, NewExe, true);
        return have;
    }

    /// <summary>
    /// Puts the new exe in place (the running one becomes .old) and starts it. beforeStart frees what the
    /// new instance needs (the mutex, the hotkeys). Puts everything back if something fails.
    /// </summary>
    /// <summary>
    /// Passed to the new copy with --updated: protection was off (Guard.OnArg: on). --standby is what beta.9 passed while it waited for
    /// the phone; the new copy still reads it (as off).
    /// </summary>
    public const string StandbyArg = "--standby", OffArg = "--off";

    public static void Install(string sha, Action beforeStart, string state = null)
    {
        var exe = Environment.ProcessPath!;
        if (Sha256Of(NewExe) != sha) throw new InvalidDataException("the downloaded file changed after the check");
        if (File.Exists(OldExe)) File.Delete(OldExe);
        File.Move(exe, OldExe);   // Windows lets a running exe be renamed, not overwritten
        try { File.Move(NewExe, exe); }
        catch { File.Move(OldExe, exe); throw; }
        FileStream hold = null;
        try
        {
            // Checked again in place and held with no write or delete sharing until it has started:
            // nothing can swap it between the check and the start (that copy runs as admin without a UAC prompt).
            hold = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(hold)).ToLowerInvariant() != sha.ToLowerInvariant())
                throw new InvalidDataException("the downloaded file changed after the check");
            beforeStart?.Invoke();
            Process.Start(new ProcessStartInfo(exe, state is StandbyArg or OffArg or Guard.OnArg ? "--updated " + state : "--updated") { UseShellExecute = false, WorkingDirectory = Dir });
        }
        catch
        {
            hold?.Dispose(); hold = null;
            try { File.Move(exe, NewExe, true); File.Move(OldExe, exe); } catch { }
            throw;
        }
        finally { hold?.Dispose(); }
    }

    /// <summary>On start: removes the old exe and leftovers from an update (the old process may take a moment to exit).</summary>
    public static async Task CleanupOld()
    {
        if (Environment.ProcessPath == null) return;
        foreach (var f in new[] { NewExe + ".part", NewExe }) try { if (File.Exists(f)) File.Delete(f); } catch { }
        for (var i = 0; i < 15 && File.Exists(OldExe); i++)
        {
            try { File.Delete(OldExe); return; }
            catch { await Task.Delay(1000); }
        }
    }
}
