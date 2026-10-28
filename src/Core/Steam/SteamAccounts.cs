using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SRT.Core;

/// <summary>Accounts saved in this PC's Steam, and account switching.</summary>
public static partial class Steam
{
    const long SteamId64Base = 76561197960265728;

    public static string InstallDir()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return Path.GetFullPath(p);
        }
        catch { }
        foreach (var d in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (Directory.Exists(d)) return d;
        return null;
    }

    public static string Exe => InstallDir() is string d ? Path.Combine(d, "steam.exe") : null;
    public static string LoginUsersPath => InstallDir() is string d ? Path.Combine(d, "config", "loginusers.vdf") : null;

    public static List<LocalAccount> ReadAccounts()
    {
        var path = LoginUsersPath;
        if (path == null || !File.Exists(path)) return new();
        var root = VdfNode.Parse(File.ReadAllText(path)).Child("users");
        if (root == null) return new();
        var list = new List<LocalAccount>();
        foreach (var (id, v) in root.Items)
        {
            if (v is not VdfNode n) continue;
            long.TryParse(n.Get("Timestamp"), out var ts);
            list.Add(new LocalAccount(id, n.Get("AccountName"), n.Get("PersonaName"), n.Get("MostRecent") == "1", ts));
        }
        return list;
    }

    public static string ActiveSteamId()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (k?.GetValue("ActiveUser") is int id && id != 0) return (SteamId64Base + (uint)id).ToString();
        }
        catch { }
        return null;
    }

    public static string LocalAvatar(string steamId)
    {
        var d = InstallDir();
        if (d == null) return null;
        var p = Path.Combine(d, "config", "avatarcache", steamId + ".png");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Path to cs2.exe: the saved one, the one from the running process, or searched in the Steam libraries.</summary>
    public static string Cs2Path(SRT.Settings s)
    {
        if (s.Cs2Path != null && File.Exists(s.Cs2Path)) return s.Cs2Path;
        string found = null;
        try
        {
            foreach (var p in Process.GetProcessesByName("cs2"))
                try { found ??= p.MainModule?.FileName; } catch { }
        }
        catch { }
        if (found == null && InstallDir() is string dir)
        {
            var libs = new List<string> { dir };
            var lf = Path.Combine(dir, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(lf))
                    foreach (var (_, v) in VdfNode.Parse(File.ReadAllText(lf)).Child("libraryfolders")?.Items ?? new())
                        if (v is VdfNode n && n.Get("path") is string path) libs.Add(path);
            }
            catch { }
            found = libs.Select(l => Path.Combine(l, "steamapps", "common", "Counter-Strike Global Offensive", "game", "bin", "win64", "cs2.exe"))
                .FirstOrDefault(File.Exists);
        }
        if (found != null && found != s.Cs2Path) { s.Cs2Path = found; Log.Info("CS2 found at " + found); }
        return found;
    }

    /// <summary>
    /// Starts Steam as the signed-in user (through Explorer), so Steam, CS2 and what they open never get the app's admin rights.
    /// There is no fallback that starts it from this process: that would pass the admin rights on. Call it on the UI thread (STA).
    /// </summary>
    public static void Launch(string args)
    {
        var err = ShellLaunch.OpenAsUser(Exe, args ?? "", InstallDir());
        if (err != null) throw new InvalidOperationException($"Explorer could not start Steam ({err}). Open Steam yourself and try again.");
    }

    /// <summary>A steam:// or https:// link, opened as the signed-in user. Never opened from this process (see <see cref="Launch"/>).</summary>
    public static void OpenUrl(string url)
    {
        var err = ShellLaunch.OpenAsUser(url, "", null);
        if (err != null) throw new InvalidOperationException($"Explorer could not open {url} ({err}).");
    }

    public static bool IsRunning => Process.GetProcessesByName("steam").Length > 0;
    public static bool Cs2Running => Process.GetProcessesByName("cs2").Length > 0;

    static async Task CloseSteam()
    {
        if (!IsRunning) return;
        // "-shutdown" only signals the running Steam, but it is still steam.exe started by this process: through Explorer, like the rest.
        if (ShellLaunch.OpenAsUser(Exe, "-shutdown", InstallDir()) is string err) Log.Warn($"Could not ask Steam to close ({err}); closing it the hard way.");
        for (var i = 0; i < 40 && IsRunning; i++) await Task.Delay(250);
        foreach (var name in new[] { "steam", "steamwebhelper", "steamservice" })
            foreach (var p in Process.GetProcessesByName(name))
                try { p.Kill(true); } catch { }
        await Task.Delay(800);
    }

    /// <summary>Switches account like the known account managers: AutoLoginUser + MostRecent, then restarts Steam.</summary>
    public static async Task SwitchTo(string accountName, string steamId, bool offline, string extraArgs)
    {
        if (Exe == null || !File.Exists(Exe)) throw new InvalidOperationException("Could not find Steam installed on this PC.");
        // The file is rewritten by this parser: only when it reads back exactly, so nothing Steam keeps there gets lost.
        // Checked before closing Steam, and again after (Steam writes the file when it exits).
        const string NotUnderstood = "Steam's loginusers.vdf has content this version doesn't understand, so the account wasn't changed. Log in from Steam itself.";
        var path = LoginUsersPath;
        var edit = path != null && File.Exists(path) && steamId != null;
        if (edit && !VdfNode.RoundTrips(File.ReadAllText(path))) throw new InvalidOperationException(NotUnderstood);
        await CloseSteam();

        var text = edit ? File.ReadAllText(path) : null;
        if (edit && !VdfNode.RoundTrips(text))
        {
            Launch("");   // Steam back on the account it had
            throw new InvalidOperationException(NotUnderstood);
        }

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"))
        {
            k.SetValue("AutoLoginUser", accountName ?? "", RegistryValueKind.String);
            k.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
        }

        if (edit)
        {
            BackupLoginUsers(path);
            var doc = VdfNode.Parse(text);
            var users = doc.Child("users");
            if (users != null)
            {
                foreach (var (id, v) in users.Items)
                {
                    if (v is not VdfNode n) continue;
                    var me = id == steamId;
                    n.Set("MostRecent", me ? "1" : "0");
                    if (me)
                    {
                        n.Set("AllowAutoLogin", "1");
                        n.Set("RememberPassword", "1");
                        n.Set("WantsOfflineMode", offline ? "1" : "0");
                        n.Set("SkipOfflineModeWarning", "1");
                    }
                }
                File.WriteAllText(path, doc.Write(), new UTF8Encoding(false));
            }
        }

        Launch(extraArgs);
    }

    /// <summary>A dated copy before each write (loginusers.vdf.srt-backup-yyyyMMdd-HHmmss); only the 10 newest of those are kept.</summary>
    static void BackupLoginUsers(string path)
    {
        File.Copy(path, $"{path}.srt-backup-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            foreach (var old in Directory.GetFiles(dir, Path.GetFileName(path) + ".srt-backup-*").OrderByDescending(f => f, StringComparer.Ordinal).Skip(10))
                File.Delete(old);
        }
        catch (Exception ex) { Log.Warn("Could not tidy the old loginusers.vdf backups: " + ex.Message); }
    }

    /// <summary>Closes and reopens Steam on the same account (e.g. so it connects through the protected connection).</summary>
    public static async Task Restart(string args)
    {
        if (Exe == null || !File.Exists(Exe)) throw new InvalidOperationException("Could not find Steam installed on this PC.");
        await CloseSteam();
        Launch(args);
    }

    /// <summary>The account is set to log in in offline mode (Steam does not confirm it to servers).</summary>
    public static bool WantsOffline(string steamId)
    {
        try
        {
            var path = LoginUsersPath;
            if (steamId == null || path == null || !File.Exists(path)) return false;
            return VdfNode.Parse(File.ReadAllText(path)).Child("users")?.Child(steamId)?.Get("WantsOfflineMode") == "1";
        }
        catch { return false; }
    }

    /// <summary>Opens Steam on the login screen to add a new account.</summary>
    public static async Task AddNew()
    {
        await CloseSteam();
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam")) k.SetValue("AutoLoginUser", "", RegistryValueKind.String);
        Launch("");
    }
}
