using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Update from the GitHub releases: only installs an exe whose SHA-256 matches the .sha256 file of the same release.
/// </summary>
public static class Updater
{
    public const string Repo = "Sepd0x/cs2-ip-changer";
    public const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    public const string ExeName = "CS2IPChanger.exe";

    /// <summary>A release on GitHub. <see cref="Size"/> is the exe's size in bytes (0 = not known).</summary>
    public sealed record Release(string Version, string Tag, string PageUrl, string ExeUrl, string ShaUrl, long Size = 0);

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
            string exe = null, sha = null;
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
                }
            var page = Str(r, "html_url");
            var rel = new Release(Clean(tag), tag, IsRepoUrl(page, "/releases/") ? page : ReleasesPage, exe, sha, size);
            if (best == null || Compare(rel.Version, best.Version) > 0) best = rel;
        }
        return best;
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool IsRepoUrl(string url, string under) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com"
        && u.AbsolutePath.StartsWith("/" + Repo + under, StringComparison.OrdinalIgnoreCase);

    /// <summary>"&lt;sha256&gt;  CS2IPChanger.exe" (sha256sum format, also with "*"). Null if there's no valid hash for the file.</summary>
    public static string ParseSha256(string text, string fileName = ExeName)
    {
        foreach (var raw in (text ?? "").Replace("﻿", "").Split('\n'))
        {
            var m = Regex.Match(raw.Trim(), @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$");
            if (!m.Success) continue;
            var name = m.Groups[2].Success ? Path.GetFileName(m.Groups[2].Value.Trim()) : null;
            if (name == null || name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    static HttpClient Http(int timeoutSec)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("CS2IPChanger/0.1");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static async Task<Release> Latest(CancellationToken ct = default)
    {
        using var http = Http(15);
        return PickLatest(await http.GetStringAsync(ApiUrl + "?per_page=50", ct));
    }

    // ------------------------------------------------------------ Download and swap

    /// <summary>Only the single-file exe can replace itself (a dev build has its DLLs next to it).</summary>
    public static bool CanSelfUpdate =>
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
        var want = ParseSha256(await http.GetStringAsync(r.ShaUrl, ct))
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
    public static void Install(string sha, Action beforeStart)
    {
        var exe = Environment.ProcessPath!;
        if (Sha256Of(NewExe) != sha) throw new InvalidDataException("the downloaded file changed after the check");
        if (File.Exists(OldExe)) File.Delete(OldExe);
        File.Move(exe, OldExe);   // Windows lets a running exe be renamed, not overwritten
        try { File.Move(NewExe, exe); }
        catch { File.Move(OldExe, exe); throw; }
        try
        {
            beforeStart?.Invoke();
            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false, WorkingDirectory = Dir });
        }
        catch
        {
            try { File.Move(exe, NewExe, true); File.Move(OldExe, exe); } catch { }
            throw;
        }
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
