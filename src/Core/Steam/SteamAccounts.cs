using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SRT.Core;

/// <summary>Accounts saved in this PC's Steam, and account switching.</summary>
public static partial class Steam
{
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
        return ParseAccounts(File.ReadAllText(path));
    }

    /// <summary>
    /// The accounts in loginusers.vdf. Keys are read in any case ("timestamp" in today's client, "Timestamp" before). The account Steam
    /// logs in to is "AutoLogin" "1" in today's client and "MostRecent" "1" in older ones (today's client drops MostRecent).
    /// </summary>
    public static List<LocalAccount> ParseAccounts(string text)
    {
        var root = VdfNode.Parse(text ?? "").Child("users");
        if (root == null) return new();
        var list = new List<LocalAccount>();
        foreach (var (id, v) in root.Items)
        {
            // Only real SteamID64s: the ID becomes a file name (avatars) and part of URLs, and loginusers.vdf is in a folder any program
            // of yours can change.
            if (v is not VdfNode n || !SteamIds.IsId64(id)) continue;
            long.TryParse(n.Get("timestamp"), out var ts);
            list.Add(new LocalAccount(id, n.Get("AccountName"), n.Get("PersonaName"), n.Get("MostRecent") == "1" || n.Get("AutoLogin") == "1", ts));
        }
        return list;
    }

    /// <summary>
    /// The list on screen no longer matches loginusers.vdf: an account there that isn't shown (and wasn't hidden), or one shown that
    /// Steam no longer has. Compared by SteamID, so a read while nothing changed never reloads the list.
    /// </summary>
    public static bool AccountsDiffer(IEnumerable<string> shown, IEnumerable<LocalAccount> onDisk, Func<string, bool> hidden)
    {
        var want = (onDisk ?? Enumerable.Empty<LocalAccount>()).Select(a => a.SteamId).Where(id => !hidden(id)).ToHashSet();
        return !want.SetEquals(shown ?? Enumerable.Empty<string>());
    }

    /// <summary>
    /// Marks the account Steam logs in to next. Today's client reads "AutoLogin" (1 on that account, 0 on the others) and the newest
    /// "timestamp"; it dropped "MostRecent" and "AllowAutoLogin", so those are only written where the file still has them (older clients).
    /// A key keeps the case it has in the file; a missing timestamp is written in lowercase, like Steam does.
    /// </summary>
    public static void MarkForLogin(VdfNode users, string steamId, bool offline, long now)
    {
        var nodes = users.Items.Select(i => i.Value).OfType<VdfNode>().ToList();
        var mostRecent = nodes.Any(n => n.Has("MostRecent"));
        var allow = nodes.Any(n => n.Has("AllowAutoLogin"));
        foreach (var (id, v) in users.Items)
        {
            if (v is not VdfNode n) continue;
            var me = id == steamId;
            n.Set("AutoLogin", me ? "1" : "0");
            if (mostRecent) n.Set("MostRecent", me ? "1" : "0");
            if (!me) continue;
            if (allow) n.Set("AllowAutoLogin", "1");
            n.Set("RememberPassword", "1");
            n.Set("WantsOfflineMode", offline ? "1" : "0");
            n.Set("SkipOfflineModeWarning", "1");
            n.Set("timestamp", now.ToString(CultureInfo.InvariantCulture));
        }
    }

    public enum LoginState { Waiting, Target, Other }

    /// <summary>
    /// Who Steam logged in to after a switch. ActiveUser is the account id (SteamID64 - 76561197960265728) of the account in use; when Steam
    /// logs in it also rewrites loginusers.vdf, and the account with a timestamp newer than the app's own write is the one it took.
    /// <paramref name="activeTrusted"/>: ActiveUser may still hold the account from before the restart for a moment, so another account
    /// there only counts once it has stayed (the file counts at once).
    /// </summary>
    public static (LoginState State, string Other) LoginDecision(string target, string activeId, IReadOnlyCollection<LocalAccount> accounts, long writtenAt, bool activeTrusted)
    {
        if (activeId == target) return (LoginState.Target, null);
        var newest = (accounts ?? Array.Empty<LocalAccount>()).Where(a => a.Timestamp > writtenAt).OrderByDescending(a => a.Timestamp).FirstOrDefault();
        if (newest != null) return newest.SteamId == target ? (LoginState.Target, null) : (LoginState.Other, newest.SteamId);
        if (activeId != null && activeTrusted) return (LoginState.Other, activeId);
        return (LoginState.Waiting, null);
    }

    /// <summary>
    /// After SwitchTo: waits (up to <paramref name="max"/>) for Steam to log in, and says to whom. Another account in ActiveUser only counts
    /// after 10 s and three readings in a row.
    /// </summary>
    public static async Task<(LoginState State, string Other)> WaitForLogin(string steamId, long writtenAt, TimeSpan max, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string lastOther = null;
        var same = 0;
        while (sw.Elapsed < max)
        {
            await Task.Delay(1000, ct);
            var active = ActiveSteamId();
            List<LocalAccount> accounts;
            try { accounts = ReadAccounts(); } catch { accounts = new(); }   // Steam may be writing it right now
            same = active != null && active != steamId && active == lastOther ? same + 1 : 0;
            lastOther = active;
            var r = LoginDecision(steamId, active, accounts, writtenAt, sw.Elapsed > TimeSpan.FromSeconds(10) && same >= 2);
            if (r.State != LoginState.Waiting) return r;
        }
        return (LoginState.Waiting, null);
    }

    /// <summary>
    /// The account in use, for the page (not for checking a switch): ActiveUser, and when today's client leaves it at 0 while Steam
    /// runs, the account loginusers.vdf marks for auto login.
    /// </summary>
    public static string InUseSteamId()
    {
        var active = ActiveSteamId();
        if (active != null || !IsRunning) return active;
        try { return InUse(null, true, ReadAccounts()); } catch { return null; }
    }

    public static string InUse(string activeId, bool steamRunning, IEnumerable<LocalAccount> accounts) =>
        activeId ?? (steamRunning ? accounts?.Where(a => a.MostRecent).OrderByDescending(a => a.Timestamp).FirstOrDefault()?.SteamId : null);

    /// <summary>A switch that came up on another account is tried once more (the second Steam start usually takes it), then you're told.</summary>
    public static bool RetrySwitch(LoginState state, int attempt) => state == LoginState.Other && attempt == 1;

    /// <summary>Steam has really exited: no steam.exe and no steamwebhelper.exe. steamservice.exe is a Windows service and stays.</summary>
    public static bool SteamClosed(IEnumerable<string> processNames) =>
        !(processNames ?? Enumerable.Empty<string>()).Any(n => n.Equals("steam", StringComparison.OrdinalIgnoreCase) || n.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase));

    static bool SteamClosedNow() => SteamClosed(Process.GetProcessesByName("steam").Select(_ => "steam").Concat(Process.GetProcessesByName("steamwebhelper").Select(_ => "steamwebhelper")).ToList());

    public static string ActiveSteamId()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (k?.GetValue("ActiveUser") is int id && id != 0) return SteamIds.FromId32((uint)id);
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

    /// <summary>
    /// The CS2 friend code of an account ("SUCVS-FADA"), what CS2 shows under Friends > Add a friend. Made from the SteamID alone:
    /// the account number's nibbles mixed with bits of an MD5 of "CSGO" and that number, then written in a 32-letter alphabet
    /// without I, O, 0 and 1. Null for anything that isn't a SteamID64.
    /// </summary>
    public static string Cs2FriendCode(string steamId64)
    {
        if (!SteamIds.IsId64(steamId64) || !ulong.TryParse(steamId64, out var id)) return null;
        // Hash: MD5 of the 64-bit number "CSGO" << 32 | account ID (little-endian), first 4 bytes.
        var h = BitConverter.ToUInt32(System.Security.Cryptography.MD5.HashData(BitConverter.GetBytes((id & 0xFFFFFFFF) | 0x4353474F00000000UL)), 0);
        ulong r = 0;
        for (var i = 0; i < 8; i++)
        {
            var nibble = (uint)(id & 0xF);
            id >>= 4;
            var a = ((uint)r << 4) | nibble;
            r = ((r >> 28) << 32) | a;
            var b = ((uint)r << 1) | ((h >> i) & 1);
            r = ((r >> 31) << 32) | b;
        }
        r = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(r);
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder();
        for (var i = 0; i < 13; i++)
        {
            if (i is 4 or 9) sb.Append('-');
            sb.Append(Alphabet[(int)(r & 31)]);
            r >>= 5;
        }
        var code = sb.ToString();
        return code.StartsWith("AAAA-") ? code[5..] : code;
    }

    /// <summary>
    /// The exe a running process was started from, with the least access Windows has (PROCESS_QUERY_LIMITED_INFORMATION): the app
    /// never opens cs2.exe in a way that could read its memory (Process.MainModule asks for PROCESS_VM_READ). Null if it can't.
    /// </summary>
    public static string ImagePath(int pid)
    {
        var h = OpenProcess(QueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var n = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
        }
        finally { CloseHandle(h); }
    }

    const uint QueryLimitedInformation = 0x1000;
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    /// <summary>
    /// Path to cs2.exe: <paramref name="saved"/> (Settings.Cs2Path) if it is still there, else the one of the running process, else
    /// searched in the Steam libraries. Only reads: the window saves a new place (MainWindow.RememberCs2), the Core never writes the settings.
    /// </summary>
    public static string FindCs2(string saved)
    {
        if (saved != null && File.Exists(saved)) return saved;
        string found = null;
        try
        {
            foreach (var p in Cs2Launch.Processes())
                using (p) found ??= ImagePath(p.Id);
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
    public static bool Cs2Running => Cs2Launch.Running;

    /// <summary>
    /// Closes Steam the clean way: "-shutdown", then up to 20 s for steam.exe and steamwebhelper.exe to exit by themselves. A Steam
    /// that was killed writes its own account back into loginusers.vdf when it starts again (seen with today's client), so the kill is
    /// only for one that hangs. steamservice.exe is a Windows service and is left alone. Then a second for the files to settle.
    /// <paramref name="whileClosed"/> runs once Steam is closed and before it opens again: files Steam rewrites when it exits (CS2's
    /// launch options) can only be changed then. The caller passes it; null = nothing.
    /// </summary>
    static async Task CloseSteam(Action whileClosed)
    {
        if (SteamClosedNow()) return;
        // "-shutdown" only signals the running Steam, but it is still steam.exe started by this process: through Explorer, like the rest.
        if (ShellLaunch.OpenAsUser(Exe, "-shutdown", InstallDir()) is string err) Log.Warn($"Could not ask Steam to close ({err}); closing it the hard way.");
        for (var i = 0; i < 80 && !SteamClosedNow(); i++) await Task.Delay(250);
        if (!SteamClosedNow())
        {
            Log.Warn("Steam didn't close within 20 s; closing it the hard way.");
            foreach (var name in new[] { "steam", "steamwebhelper" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(true); } catch { }
            for (var i = 0; i < 20 && !SteamClosedNow(); i++) await Task.Delay(250);
        }
        await Task.Delay(1000);
        // Files Steam rewrites when it exits (CS2's launch options) can only be changed now, before it starts again.
        if (SteamClosedNow() && whileClosed != null)
            try { await Task.Run(whileClosed); } catch (Exception ex) { Log.Warn("While Steam was closed: " + ex.Message); }
    }

    /// <summary>Steam and its web helper are both closed.</summary>
    public static bool Closed => SteamClosedNow();

    /// <summary>Closes Steam the clean way (see <see cref="CloseSteam"/>); true when it is closed.</summary>
    public static async Task<bool> Close(Action whileClosed = null) { await CloseSteam(whileClosed); return SteamClosedNow(); }

    const string Linked = SafeFile.SteamLinked;

    /// <summary>
    /// Switches account: AutoLoginUser in the registry and AutoLogin + timestamp in loginusers.vdf (see <see cref="MarkForLogin"/>), then
    /// restarts Steam. Returns the timestamp written, for <see cref="WaitForLogin"/>. <paramref name="whileClosed"/>: see <see cref="CloseSteam"/>.
    /// </summary>
    // The file is rewritten by this parser: only when it reads back exactly, so nothing Steam keeps there gets lost.
    const string NotUnderstood = "Steam's loginusers.vdf has content this version doesn't understand, so the account wasn't changed. Log in from Steam itself.";
    const string NoSteam = "Could not find Steam installed on this PC.";

    /// <summary>
    /// Why the account can't be switched now (null = it can): no Steam, loginusers.vdf behind a link, or with content the parser can't
    /// write back. The app asks before it closes CS2 or changes the IP for a switch; <see cref="SwitchTo"/> asks again before closing Steam.
    /// </summary>
    public static string SwitchProblem(string steamId)
    {
        if (Exe == null || !File.Exists(Exe)) return NoSteam;
        var path = LoginUsersPath;
        if (path == null || !File.Exists(path) || steamId == null) return null;
        if (Redirected(path)) return Linked;
        return VdfNode.RoundTrips(File.ReadAllText(path)) ? null : NotUnderstood;
    }

    public static async Task<long> SwitchTo(string accountName, string steamId, bool offline, string extraArgs, Action whileClosed = null)
    {
        // Checked before closing Steam, and again after (Steam writes the file when it exits).
        // A link is refused before Steam is closed, so Steam isn't left closed for nothing
        if (SwitchProblem(steamId) is string why) throw why == Linked ? new IOException(why) : new InvalidOperationException(why);
        var path = LoginUsersPath;
        var edit = path != null && File.Exists(path) && steamId != null;
        await CloseSteam(whileClosed);
        // A Steam still running would get "-login" while on the old account and write that account back over the switch
        if (!SteamClosedNow()) throw new InvalidOperationException("Steam didn't close, so the account wasn't changed. Close Steam (or wait a moment) and try again.");

        string previous = null;
        var changed = false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            // From here to the last write the folder is held open and the file is used through its own handle (see PinnedFolder)
            using var folder = edit ? new PinnedFolder(Path.GetDirectoryName(path)) : null;
            var name = Path.GetFileName(path);
            using var file = folder?.Open(name, FileMode.Open, FileAccess.ReadWrite);
            byte[] bytes = null;
            string text = null;
            if (file != null)
            {
                using (var ms = new MemoryStream()) { file.CopyTo(ms); bytes = ms.ToArray(); }
                using (var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true)) text = r.ReadToEnd();
                if (!VdfNode.RoundTrips(text)) throw new InvalidOperationException(NotUnderstood);   // Steam goes back on the account it had
            }

            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"))
            {
                previous = k.GetValue("AutoLoginUser") as string;
                changed = true;
                k.SetValue("AutoLoginUser", accountName ?? "", RegistryValueKind.String);
                k.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
            }

            if (file != null)
            {
                BackupLoginUsers(folder, name, bytes);
                var doc = VdfNode.Parse(text);
                var users = doc.Child("users");
                if (users != null)
                {
                    MarkForLogin(users, steamId, offline, now);
                    var mine = new UTF8Encoding(false).GetBytes(doc.Write());
                    WriteAll(file, mine);
                    // A second, then read it again right before Steam starts: anything that wrote it meanwhile is undone.
                    await Task.Delay(1000);
                    file.Position = 0;
                    byte[] again;
                    using (var ms = new MemoryStream()) { file.CopyTo(ms); again = ms.ToArray(); }
                    if (!again.AsSpan().SequenceEqual(mine))
                    {
                        Log.Warn("Something changed loginusers.vdf right after the switch wrote it; writing it again.");
                        string t2;
                        using (var r = new StreamReader(new MemoryStream(again), Encoding.UTF8, true)) t2 = r.ReadToEnd();
                        if (!VdfNode.RoundTrips(t2)) throw new InvalidOperationException(NotUnderstood);
                        var doc2 = VdfNode.Parse(t2);
                        if (doc2.Child("users") is VdfNode u2) { MarkForLogin(u2, steamId, offline, now); WriteAll(file, new UTF8Encoding(false).GetBytes(doc2.Write())); }
                    }
                }
                TidyBackups(folder, name);
            }
        }
        catch
        {
            // Nothing half done: the account Steam had, and Steam open again
            if (changed)
                try { using var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"); k.SetValue("AutoLoginUser", previous ?? "", RegistryValueKind.String); }
                catch (Exception ex) { Log.Warn("Could not put Steam's previous account back: " + ex.Message); }
            try { Launch(""); } catch (Exception ex) { Log.Warn(ex.Message); }
            throw;
        }

        Launch(LoginArgs(accountName, extraArgs));
        return now;
    }

    /// <summary>
    /// "-login &lt;account&gt;" plus the extra arguments. Started through Explorer (see <see cref="Launch"/>), Steam ignores AutoLogin in
    /// loginusers.vdf and logs back in to the account it had; with -login it takes that account's saved sign-in (no password here).
    /// Tested on 29 Oct 2026 with the current client: Explorer without -login 0 of 4 on the new account, with -login 2 of 2.
    /// </summary>
    public static string LoginArgs(string accountName, string extra)
    {
        var login = accountName != null && Regex.IsMatch(accountName, "^[A-Za-z0-9_]{2,64}$") ? "-login " + accountName : "";
        return string.Join(" ", new[] { login, extra?.Trim() }.Where(a => !string.IsNullOrEmpty(a)));
    }

    /// <summary>The whole file replaced through the handle already open on it.</summary>
    public static void WriteAll(FileStream file, byte[] bytes)
    {
        file.Position = 0;
        file.SetLength(0);
        file.Write(bytes);
        file.Flush(true);
    }

    /// <summary>
    /// True if loginusers.vdf or its config folder is a link or junction. The Steam path comes from HKCU, which any program of yours
    /// can change; the app writes and deletes there as admin, so a link would point those writes somewhere else.
    /// Only these two: a whole Steam folder moved to another drive with a junction is common and stays allowed.
    /// </summary>
    static bool Redirected(string path) => SafeFile.Linked(path, Path.GetDirectoryName(Path.GetFullPath(path)));

    /// <summary>A dated copy before each write (loginusers.vdf.srt-backup-yyyyMMdd-HHmmss), in the same pinned folder.</summary>
    static void BackupLoginUsers(PinnedFolder folder, string name, byte[] bytes)
    {
        using var f = folder.Open($"{name}.srt-backup-{DateTime.Now:yyyyMMdd-HHmmss}", FileMode.Create, FileAccess.Write);
        f.Write(bytes);
    }

    /// <summary>Only the 10 newest backups are kept, deleted through their own handle: a link in their place goes, never what it points to.</summary>
    public static void TidyBackups(PinnedFolder folder, string name)
    {
        try
        {
            foreach (var old in Directory.GetFiles(folder.Real, name + ".srt-backup-*").OrderByDescending(f => f, StringComparer.Ordinal).Skip(10))
                folder.Delete(Path.GetFileName(old));
        }
        catch (Exception ex) { Log.Warn("Could not tidy the old loginusers.vdf backups: " + ex.Message); }
    }

    /// <summary>
    /// A folder held open, as admin, from the link check to the last write. Without FILE_SHARE_DELETE nobody can rename it or put a
    /// junction in its place while it is open, and each file is opened by the folder's real path without following a link in its name:
    /// one whose real place isn't this folder (a junction higher up changed meanwhile) is refused before anything is written.
    /// </summary>
    public sealed class PinnedFolder : IDisposable
    {
        readonly SafeFileHandle _dir;
        /// <summary>The folder's real path (\\?\...), links resolved.</summary>
        public string Real { get; }

        public PinnedFolder(string dir)
        {
            // List rights too: a handle with only attribute rights takes no part in the sharing check and wouldn't hold the folder
            _dir = SafeFile.CreateFileW(dir, ListDirectory | FileReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (_dir.IsInvalid) throw new IOException($"could not open the folder {Path.GetFileName(dir)} ({LastError()})");
            if (IsLink(_dir)) { _dir.Dispose(); throw new IOException(Linked); }
            Real = FinalPath(_dir);
        }

        public FileStream Open(string name, FileMode mode, FileAccess access)
        {
            var disposition = mode switch { FileMode.Open => OpenExisting, FileMode.Create => CreateAlways, FileMode.CreateNew => CreateNew, _ => throw new ArgumentException(mode.ToString()) };
            var rights = (access.HasFlag(FileAccess.Read) ? GenericRead : 0) | (access.HasFlag(FileAccess.Write) ? GenericWrite : 0);
            return new FileStream(Checked(name, rights, ShareRead, disposition), access);
        }

        public void Delete(string name)
        {
            using var h = Checked(name, DeleteRight, ShareRead | ShareWrite | ShareDelete, OpenExisting);
            if (!SafeFile.SetFileInformationByHandle(h, FileDispositionInfo, new byte[] { 1 }, 1)) throw new IOException($"could not delete {name} ({LastError()})");
        }

        SafeFileHandle Checked(string name, uint rights, uint share, uint disposition)
        {
            if (name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) throw new ArgumentException(name);
            // Read attributes too: the link check below needs them
            var h = SafeFile.CreateFileW(Path.Combine(Real, name), rights | FileReadAttributes, share, IntPtr.Zero, disposition, OpenReparsePoint, IntPtr.Zero);
            if (h.IsInvalid) throw new IOException($"could not open {name} ({LastError()})");
            bool ok;
            try { ok = !IsLink(h) && string.Equals(Path.GetDirectoryName(FinalPath(h)), Real, StringComparison.OrdinalIgnoreCase); }
            catch { ok = false; }
            if (!ok) { h.Dispose(); throw new IOException(Linked); }
            return h;
        }

        public void Dispose() => _dir.Dispose();

        static string LastError() => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;

        static bool IsLink(SafeFileHandle h) =>
            !SafeFile.GetFileInformationByHandleEx(h, FileAttributeTagInfo, out var info, (uint)Marshal.SizeOf<SafeFile.AttributeTag>())
            || ((FileAttributes)info.Attributes).HasFlag(FileAttributes.ReparsePoint);

        /// <summary>The real path as Windows gives it (\\?\...), kept that way: <see cref="Real"/> and each file's folder compare like for like.</summary>
        static string FinalPath(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024);
            var n = SafeFile.GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) throw new IOException("could not read the folder's real path");
            return sb.ToString();
        }

        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, DeleteRight = 0x00010000, FileReadAttributes = 0x80, ListDirectory = 0x1;
        const uint ShareRead = 1, ShareWrite = 2, ShareDelete = 4;
        const uint CreateNew = 1, CreateAlways = 2, OpenExisting = 3;
        const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
        const int FileAttributeTagInfo = 9, FileDispositionInfo = 4;
    }

    /// <summary>Closes and reopens Steam on the same account (e.g. so it connects through the protected connection).</summary>
    public static async Task Restart(string args, Action whileClosed = null)
    {
        if (Exe == null || !File.Exists(Exe)) throw new InvalidOperationException("Could not find Steam installed on this PC.");
        await CloseSteam(whileClosed);
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
    public static async Task AddNew(Action whileClosed = null)
    {
        await CloseSteam(whileClosed);
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam")) k.SetValue("AutoLoginUser", "", RegistryValueKind.String);
        Launch("");
    }
}
