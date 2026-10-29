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
            return Rec.ScrubIds(text, words);
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void AddText(string name, string text)
        {
            using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            w.Write(Clean(text));
        }
        void AddTail(string name, string file, bool hideExternal = false)
        {
            try
            {
                if (file == null || !File.Exists(file)) return;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > TailBytes) fs.Seek(-TailBytes, SeekOrigin.End);
                var text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
                AddText(name, hideExternal ? ExternalAddr.Replace(text, "external address = '(hidden)'") : text);
            }
            catch (Exception ex) { AddText(name + ".error.txt", ex.Message); }
        }

        AddText("now.txt", now ?? "");
        AddText("settings.json", settingsJson ?? "");
        AddTail("app/registo.log", Paths.File("registo.log"));
        AddTail("app/registo.log.old", Paths.File("registo.log.old"));
        AddTail("app/servers.json", Paths.File("servers.json"));
        AddTail("app/owned.json", Paths.File("owned.json"));
        if (steamDir != null)
        {
            var logs = Path.Combine(steamDir, "logs");
            if (Directory.Exists(logs))
                foreach (var f in Directory.GetFiles(logs, "connection_log*.txt")) AddTail("steam/" + Path.GetFileName(f), f, true);
        }
        AddTail("cs2/console.log", cs2Console);
        return path;
    }
}
