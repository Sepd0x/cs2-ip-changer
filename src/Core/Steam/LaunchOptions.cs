using System.Text;

namespace SRT.Core;

/// <summary>
/// CS2's launch options in Steam (localconfig.vdf of each account, apps > 730 > LaunchOptions). The app adds -condebug -conclearlog
/// there so CS2 writes console.log even when Steam opens it (4 nov.: kicks and bans were only seen when the app opened CS2).
/// <list type="bullet">
/// <item>Only with Steam closed: Steam keeps the file in memory and writes it back when it exits, so a change made while it runs is lost.</item>
/// <item>Nothing already there is removed or reordered: the two options are added at the end, each only if it is missing.</item>
/// <item>A file the parser can't write back token for token is left as it is (<see cref="VdfNode.RoundTrips"/>).</item>
/// <item>A backup of each file goes to SRT_Dados\launch-options before it is written.</item>
/// </list>
/// </summary>
public static class Cs2LaunchOptions
{
    public static IReadOnlyList<string> Needed => Cs2Launch.ConsoleFlags;

    static string[] Words(string options) => (options ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Both options are there (as their own words, any case).</summary>
    public static bool Has(string options)
    {
        var w = Words(options);
        return Needed.All(n => w.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The options with the missing ones added at the end; what was there stays exactly as it was.</summary>
    public static string Add(string options)
    {
        var w = Words(options);
        var missing = Needed.Where(n => !w.Any(x => x.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count == 0) return options;
        var kept = (options ?? "").TrimEnd();
        return (kept.Length == 0 ? "" : kept + " ") + string.Join(" ", missing);
    }

    public sealed record Patched(string Text, string Before, string After, bool Changed, string Why);

    /// <summary>
    /// localconfig.vdf with 730's LaunchOptions completed. Changed false with a reason when nothing should be written (already there,
    /// or a file the app can't keep exactly). A missing apps or 730 section is made, as Steam does when you type launch options.
    /// </summary>
    public static Patched Patch(string vdf)
    {
        if (string.IsNullOrWhiteSpace(vdf)) return new(null, null, null, false, "the file is empty");
        if (!VdfNode.RoundTrips(vdf)) return new(null, null, null, false, "the file has something the app can't keep exactly, so it was left as it is");
        VdfNode root;
        try { root = VdfNode.Parse(vdf); }
        catch { return new(null, null, null, false, "the file couldn't be read"); }
        var steam = root.Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam");
        if (steam == null) return new(null, null, null, false, "it isn't a Steam localconfig.vdf");
        var apps = steam.Child("apps");
        if (apps == null) { apps = new VdfNode(); steam.Items.Add(new("apps", apps)); }
        var cs2 = apps.Child("730");
        if (cs2 == null) { cs2 = new VdfNode(); apps.Items.Add(new("730", cs2)); }
        var before = cs2.Get("LaunchOptions");
        var after = Add(before);
        if (before != null && after == before) return new(null, before, before, false, "already there");
        cs2.Set("LaunchOptions", after);
        return new(root.Write(), before, after, true, null);
    }

    public sealed record AccountResult(string Id32, bool Changed, string Before, string After, string Why);

    /// <summary>
    /// Every account folder under <paramref name="userData"/> that has config\localconfig.vdf. Steam must be closed (the caller checks,
    /// and <paramref name="steamRunning"/> is asked again right before each write). Backups go to <paramref name="backupDir"/>.
    /// </summary>
    public static List<AccountResult> ApplyAll(string userData, string backupDir, Func<bool> steamRunning)
    {
        var results = new List<AccountResult>();
        if (userData == null || !Directory.Exists(userData)) return results;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var dir in Directory.GetDirectories(userData))
        {
            var id = Path.GetFileName(dir);
            if (!uint.TryParse(id, out var n) || n == 0) continue;   // "0", "ac" and other folders Steam keeps there aren't accounts
            var cfgDir = Path.Combine(dir, "config");
            var file = Path.Combine(cfgDir, "localconfig.vdf");
            if (!File.Exists(file)) continue;
            try
            {
                // Userdata can be changed by programs without admin rights: the config folder is held (a link is refused), the file is read
                // only if it really is there, and it is written by handle into that same folder (SafeFile).
                using var held = SafeFile.Hold(cfgDir);
                string text;
                using (var f = SafeFile.OpenRead(Path.Combine(held.Real, "localconfig.vdf")))
                using (var r = new StreamReader(f, new UTF8Encoding(false)))
                    text = r.ReadToEnd();
                var p = Patch(text);
                if (!p.Changed) { results.Add(new(id, false, p.Before, p.After, p.Why)); continue; }
                if (steamRunning()) { results.Add(new(id, false, p.Before, p.Before, "Steam opened meanwhile")); break; }
                SafeFile.Write(Path.Combine(backupDir, $"{id}-localconfig-{stamp}.vdf"), new UTF8Encoding(false).GetBytes(text));
                SafeFile.Write(held.Real, "localconfig.vdf", new UTF8Encoding(false).GetBytes(p.Text));
                results.Add(new(id, true, p.Before, p.After, null));
            }
            catch (Exception ex) { results.Add(new(id, false, null, null, Cs2ConfigStore.Why(ex))); }
        }
        return results;
    }

    /// <summary>Accounts whose launch options already have both (read only, Steam may be open). Null when it can't be read.</summary>
    public static (int With, int Total)? Count(string userData)
    {
        try
        {
            if (userData == null || !Directory.Exists(userData)) return null;
            int with = 0, total = 0;
            foreach (var dir in Directory.GetDirectories(userData))
            {
                if (!uint.TryParse(Path.GetFileName(dir), out var n) || n == 0) continue;
                var file = Path.Combine(dir, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                total++;
                try
                {
                    using var f = SafeFile.OpenRead(file);
                    using var r = new StreamReader(f, new UTF8Encoding(false));
                    var root = VdfNode.Parse(r.ReadToEnd());
                    var lo = root.Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps")?.Child("730")?.Get("LaunchOptions");
                    if (Has(lo)) with++;
                }
                catch { }
            }
            return (with, total);
        }
        catch { return null; }
    }
}
