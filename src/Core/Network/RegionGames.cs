using System.Diagnostics;

namespace SRT.Core;

public sealed record RegionGame(int AppId, string Name, string Exe, string RuleName, string CacheFile)
{
    public string ConfigUrl => $"https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid={AppId}";

    /// <summary>The game's name: the closed Game box of Regions shows the item's text, not DisplayMemberPath (theme ComboBox).</summary>
    public override string ToString() => Name;

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe));
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        }
    }
}

public static class RegionGames
{
    public const string DotaRule = AppIds.RulePrefix + " - Dota 2 regions turned off";
    public const string DeadlockRule = AppIds.RulePrefix + " - Deadlock regions turned off";

    public static readonly RegionGame Cs2 = new(730, "CS2", "cs2.exe", Firewall.RegionsRuleName, "sdrconfig.json");
    public static readonly RegionGame Dota2 = new(570, "Dota 2", "dota2.exe", DotaRule, "sdrconfig-570.json");
    public static readonly RegionGame Deadlock = new(1422450, "Deadlock", "deadlock.exe", DeadlockRule, "sdrconfig-1422450.json");
    public static IReadOnlyList<RegionGame> All { get; } = Array.AsReadOnly(new[] { Cs2, Dota2, Deadlock });

    static string Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static string FindInstalledExe(RegionGame game)
    {
        var steam = Steam.InstallDir();
        if (steam == null) return null;
        return FindExe(game, steam, Read(Path.Combine(steam, "steamapps", "libraryfolders.vdf")), Read, File.Exists);
    }

    public static string FindExe(RegionGame game, string steam, string libraryFolders, Func<string, string> readManifest, Func<string, bool> exists)
    {
        var libraries = new List<string> { steam };
        if (libraryFolders != null && VdfNode.Parse(libraryFolders).Child("libraryfolders") is { } root)
            foreach (var (_, value) in root.Items)
                if (value is VdfNode library && library.Get("path") is { } path) libraries.Add(path);

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(library) || library.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
            var apps = Path.Combine(library, "steamapps");
            var manifest = readManifest(Path.Combine(apps, $"appmanifest_{game.AppId}.acf"));
            if (manifest == null) continue;
            var state = VdfNode.Parse(manifest).Child("AppState");
            var dir = state?.Get("installdir");
            if (state?.Get("appid") != game.AppId.ToString() || string.IsNullOrWhiteSpace(dir) ||
                dir.EndsWith('.') || dir.EndsWith(' ') || dir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            var exe = Path.Combine(apps, "common", dir, "game", "bin", "win64", game.Exe);
            if (exists(exe)) return exe;
        }
        return null;
    }
}
