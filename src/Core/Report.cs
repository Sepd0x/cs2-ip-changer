using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// "Save bug report" (Log page): a .zip with what a bug report needs. The Steam Web API key and the home IP are
/// left out, and Steam's own logs have the external address covered, so the zip can be posted after a look.
/// </summary>
public static class Report
{
    const long TailBytes = 5 * 1024 * 1024;
    static readonly Regex ExternalAddr = new(@"external address = '[^']*'", RegexOptions.Compiled);

    // The app's own Log lines that name a Wi-Fi network: "(connected to X)", hotspot "X", connected to "X" (audit 26 nov., V3: a
    // network no longer in use isn't among the words hidden at export, and a home SSID is enough to find the home on public maps)
    static readonly Regex SsidInLine = new(@"(?<=\(connected to )[^)\r\n]+(?=\))|(?<=hotspot "")[^""\r\n]+(?="")|(?<=connected to "")[^""\r\n]+(?="")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The Wi-Fi names in the app's Log lines, as (hidden).</summary>
    public static string HideSsids(string text) => SsidInLine.Replace(text ?? "", "(hidden)");

    // Second Codex pass, A14 (27 nov.): before 1.0.0 the Log wrote "The phone connection IP (x) is the same as your home IP" and
    // "The phone's IP (x) is the same as your home IP" with x whole. That x was the home IP then; after the provider changes it, it is
    // no longer among the IPs hidden at export, so every IPv4 address on a line that says it is the home IP is hidden too.
    static readonly Regex HomeIpLine = new(@"^.*\bsame as your home IP\b.*$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    static readonly Regex Ipv4 = new(@"(?<![\d.])\d{1,3}(?:\.\d{1,3}){3}(?![\d])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The IPv4 addresses on Log lines that say they are the home IP, as home-ip (any of them, not only the IPs known now).</summary>
    public static string HideHomeLines(string text) => HomeIpLine.Replace(text ?? "", m => Ipv4.Replace(m.Value, "home-ip"));

    /// <summary>
    /// Creates the zip on the Desktop (or in SRT_Dados if there's no Desktop) and returns its path.
    /// <paramref name="settingsJson"/> already comes without the key and the home IP; every IP in <paramref name="hide"/> becomes "home-ip".
    /// </summary>
    public static string Export(string now, string settingsJson, string steamDir, string cs2Console, IEnumerable<string> hide, string outDir = null, IEnumerable<string> personal = null)
    {
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dir = outDir ?? (!string.IsNullOrEmpty(desk) && Directory.Exists(desk) ? desk : Paths.Data);
        var path = Path.Combine(dir, $"CS2IPChanger-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var secrets = (hide ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        // Logins, Steam names, players, SSIDs, SteamIDs, e-mails and the Windows user become "(hidden)": the zip says what broke, not who you are.
        var words = Rec.WordRegex(personal ?? Enumerable.Empty<string>());

        string Clean(string text)
        {
            foreach (var ip in secrets) text = Regex.Replace(text, @"(?<![\d.])" + Regex.Escape(ip) + @"(?![\d])", "home-ip");
            return Rec.ScrubIds(HideSsids(HideHomeLines(text)), words);
        }

        // Built in memory and written by handle (SafeFile): the Desktop is a folder other programs can change.
        using var ms = new MemoryStream();
        var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true);
        void AddText(string name, string text)
        {
            using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            w.Write(Clean(text));
        }
        // Steam's and CS2's logs are in folders other programs can change: read only as themselves, never through a link
        // (a file only admins can read must not end up in a zip on your Desktop).
        void AddTail(string name, string file, bool hideExternal = false, bool outside = false)
        {
            try
            {
                if (file == null || !File.Exists(file)) return;
                using var fs = outside ? SafeFile.OpenRead(file) : new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > TailBytes) fs.Seek(-TailBytes, SeekOrigin.End);
                var text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
                AddText(name, hideExternal ? ExternalAddr.Replace(text, "external address = '(hidden)'") : text);
            }
            catch (Exception ex) { AddText(name + ".error.txt", ex.Message); }
        }

        AddText("now.txt", now ?? "");
        AddText(AppIds.Files.Settings, settingsJson ?? "");
        AddTail("app/registo.log", Paths.File("registo.log"));
        AddTail("app/registo.log.old", Paths.File("registo.log.old"));
        AddTail("app/crash.txt", Paths.File("crash.txt"));
        AddTail("app/" + AppIds.Files.Servers, Paths.File(AppIds.Files.Servers));
        AddTail("app/" + AppIds.Files.Owned, Paths.File(AppIds.Files.Owned));
        if (steamDir != null)
        {
            var logs = Path.Combine(steamDir, "logs");
            if (Directory.Exists(logs))
                foreach (var f in Directory.GetFiles(logs, "connection_log*.txt")) AddTail("steam/" + Path.GetFileName(f), f, true, outside: true);
        }
        AddTail("cs2/console.log", cs2Console, outside: true);
        zip.Dispose();
        SafeFile.Write(path, ms.ToArray());
        return path;
    }
}
