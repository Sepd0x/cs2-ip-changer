using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRT.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class AppInfo
{
    /// <summary>The csproj version (e.g. 0.1.0-alpha.1); the development build adds "+dev".</summary>
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
}

public static class Paths
{
    // Portable: everything sits next to the .exe, in its own folder.
    public static readonly string AppDir = AppContext.BaseDirectory;
    public static readonly string Data = Path.Combine(AppDir, AppIds.DataFolder);
    public static readonly string Avatars = Path.Combine(Data, "avatars");
    public static string File(string name) => Path.Combine(Data, name);

    /// <summary>
    /// Nothing is written to SRT_Dados (Program.Main: as admin next to an exe that others can change, like --restore from Downloads,
    /// and in the Install copy). The log stays in memory and every write through <see cref="NoLink"/> fails.
    /// </summary>
    public static bool ReadOnly { get; set; }

    public static void Ensure()
    {
        if (ReadOnly) return;
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Avatars);
    }

    /// <summary>
    /// The app writes, moves and deletes here as admin: a junction or link on the way (SRT_Dados, platform-tools, the file itself)
    /// would send that somewhere else, and a hard link would change another file. Throws instead. Folders are checked from
    /// <paramref name="top"/> (the exe's folder, checked at start) down.
    /// </summary>
    public static string NoLink(string path, string top = null)
    {
        if (ReadOnly) throw new IOException("This copy of the app writes nothing in its folder.");
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(top ?? AppDir));
        for (var d = Path.GetDirectoryName(full); d != null && d.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); d = Path.GetDirectoryName(d))
            if (IsLink(d)) throw new IOException($"{d} is a link, so nothing is written through it.");
        if (IsLink(full) || HardLinks(full) > 1) throw new IOException($"{full} is a link, so nothing is written through it.");
        return full;
    }

    /// <summary>A junction or symbolic link (also one whose target is gone); a path that doesn't exist isn't one.</summary>
    public static bool IsLink(string path)
    {
        try { return System.IO.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch { return true; }
    }

    static int HardLinks(string file)
    {
        try
        {
            if (!System.IO.File.Exists(file)) return 0;
            using var h = System.IO.File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(h, out var info) ? (int)info.Links : 0;
        }
        catch (IOException) { return 0; }   // held by an antivirus for a moment: the folder checks above still hold
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct FileInformation
    {
        // FILETIMEs as two DWORDs each: a long here would be aligned to 8 and move every field after it
        public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out FileInformation info);
}

public static class Store
{
    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Files that couldn't be read at start (locked by an antivirus or OneDrive, say): saving over them would replace the
    // real data with defaults, so they aren't saved until the app restarts.
    static readonly HashSet<string> Unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The file's text; a sharing violation is not corruption, so it is tried again for about 2 s before giving up.</summary>
    static string ReadText(string path, out Exception error)
    {
        error = null;
        for (var i = 0; ; i++)
        {
            try { return System.IO.File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) when (i < 20) { Thread.Sleep(100); }
            catch (Exception ex) { error = ex; return null; }
        }
    }

    /// <summary>
    /// Reads <paramref name="path"/> the way <see cref="Load"/> does (tried again while locked, the .prev copy when damaged), but only
    /// reads: no Log line, no .broken copy, no mark against saving. Default when it is missing, can't be read or has no good copy
    /// (a file that can't be read isn't damaged: its .prev is not used then, as Load doesn't). For a look before the app opens (App.StatsNoticeIn).
    /// </summary>
    public static T Peek<T>(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path) || ReadText(path, out _) is not string raw) return default;
            if (TryParse(raw, out T value)) return value;
            return System.IO.File.Exists(path + ".prev") && TryParse(System.IO.File.ReadAllText(path + ".prev", Encoding.UTF8), out value) ? value : default;
        }
        catch { return default; }
    }

    public static T Load<T>(string path, Func<T> fallback)
    {
        if (!System.IO.File.Exists(path)) return fallback();
        var raw = ReadText(path, out var error);
        if (error != null)
        {
            lock (Unreadable) Unreadable.Add(path);
            Log.Warn($"Could not open {Path.GetFileName(path)} ({error.Message}). Your changes won't be saved over it until the app restarts.");
            return fallback();
        }
        lock (Unreadable) Unreadable.Remove(path);
        if (TryParse(raw, out T value)) return value;
        // Empty, cut short by a power loss or not JSON: keep the broken file and use the last good copy
        var name = Path.GetFileName(path);
        try { System.IO.File.Copy(path, $"{path}{AppIds.Files.BrokenMark}{DateTime.UtcNow:yyyyMMdd-HHmmss}", true); } catch { }
        try
        {
            if (System.IO.File.Exists(path + ".prev") && TryParse(System.IO.File.ReadAllText(path + ".prev", Encoding.UTF8), out value))
            {
                Log.Warn($"{name} was damaged (kept as {name}.broken-…); the previous copy was loaded instead.");
                return value;
            }
        }
        catch { }
        Log.Warn($"{name} was damaged and there is no good copy (kept as {name}.broken-…); starting from the defaults.");
        return fallback();
    }

    public static bool TryParse<T>(string raw, out T value)
    {
        value = default;
        raw = raw?.TrimStart('﻿');
        if (string.IsNullOrWhiteSpace(raw?.Replace('\0', ' '))) return false;
        try { value = JsonSerializer.Deserialize<T>(raw, Opt); return value != null; }
        catch { return false; }
    }

    static readonly object SaveLock = new();

    /// <summary>Writes to a .tmp file flushed to the disk, then swaps it in and keeps the file before as .prev, so a power
    /// loss leaves either the old or the new file, never an empty one.</summary>
    public static void Save<T>(string path, T value)
    {
        lock (SaveLock)
        {
            lock (Unreadable) if (Unreadable.Contains(path)) return;
            var tmp = path + AppIds.Files.TmpSuffix;
            foreach (var f in new[] { path, tmp, path + ".prev" }) Paths.NoLink(f);
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(value, Opt));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (System.IO.File.Exists(path))
            {
                // Only a file that still reads as JSON becomes .prev, so a damaged one never replaces the last good copy
                var good = false;
                try { good = TryParse<System.Text.Json.Nodes.JsonNode>(System.IO.File.ReadAllText(path, Encoding.UTF8), out _); } catch { }
                try
                {
                    if (good) { System.IO.File.Replace(tmp, path, path + ".prev", true); return; }
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException) { }
            }
            System.IO.File.Move(tmp, path, true);
        }
    }
}

/// <summary>Crash details for bug reports (crash.txt, newest last, about 64 KB), and a marker that tells the next start
/// whether this one ended normally.</summary>
public static class Crash
{
    static readonly object Lock = new();
    static readonly HashSet<string> Seen = new();
    static string Marker => Paths.File(AppIds.Files.RunningFlag);

    public static void Write(Exception ex, string where)
    {
        if (ex == null) return;
        try
        {
            lock (Lock)
            {
                // The same error on every tick is written once per start
                var key = ex.GetType().FullName + "|" + ex.TargetSite + "|" + ex.GetBaseException().Message;
                if (!Seen.Add(key)) return;
                var path = Paths.NoLink(Paths.File("crash.txt"));
                var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {AppInfo.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
                var old = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
                var all = old + text;
                if (all.Length > 64_000) all = all[^64_000..];
                System.IO.File.WriteAllText(path, all);
            }
        }
        catch { }
    }

    /// <summary>The session before this one didn't close normally (a crash, a forced close or a power loss). For the stats (unclean_exit).</summary>
    public static bool LastUnclean { get; private set; }

    public static void Started()
    {
        try
        {
            LastUnclean = System.IO.File.Exists(Marker);
            if (LastUnclean)
                Log.Warn("The last session didn't close normally (a crash, a forced close or a power loss). If something looks wrong, Save bug report on the Log page has the details.");
            System.IO.File.WriteAllText(Paths.NoLink(Marker), DateTime.Now.ToString("O"));
        }
        catch { }
    }

    public static void Ended() { try { System.IO.File.Delete(Paths.NoLink(Marker)); } catch { } }
}

public enum LogLevel { Info, Ok, Warn, Error }

public sealed class LogEntry : Observable
{
    string _shown;
    public DateTime Time { get; init; }
    public LogLevel Level { get; init; }
    public string Text { get; init; }
    /// <summary>What the Log page shows: the text, or the text with the home IP and logins covered (Hide logins).</summary>
    public string Shown { get => _shown ?? Text; set => Set(ref _shown, value); }
    public string TimeText => Time.ToString("HH:mm:ss");
}

public static class Log
{
    public static event Action<LogEntry> Added;
    static readonly object Lock = new();

    public static void Info(string t) => Write(LogLevel.Info, t);
    public static void Ok(string t) => Write(LogLevel.Ok, t);
    public static void Warn(string t) => Write(LogLevel.Warn, t);
    public static void Error(string t) => Write(LogLevel.Error, t);

    /// <summary>
    /// A notification in the app goes to the log too, unless the code that showed it has just written its own line (on the same thread,
    /// in the last second, usually with more detail) or the same text went in the last few seconds. A line from another thread
    /// (the sync, the Steam watcher) doesn't count.
    /// </summary>
    public static void Notice(LogLevel level, string t)
    {
        var now = DateTime.Now;
        if (now - _wroteHere is var mine && mine >= TimeSpan.Zero && mine < TimeSpan.FromSeconds(1)) return;
        lock (Lock)
        {
            var since = now - _lastAt;
            if (t == _lastText && since >= TimeSpan.Zero && since < TimeSpan.FromSeconds(5)) return;
        }
        Write(level, t);
    }

    /// <summary>When this thread last wrote a line (a repeat counted instead of written counts too: the line is in the log).</summary>
    [ThreadStatic] static DateTime _wroteHere;

    // The same line again within a minute (an error on every 5 s tick) is counted, not written again: one line says how many.
    static string _lastText;
    static LogLevel _lastLevel;
    static DateTime _lastAt;
    static int _repeats;
    static bool _aged;
    /// <summary>The log file keeps about this long: older lines go to .old and then away.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(14);

    static void Write(LogLevel level, string text)
    {
        // The line shows the app's clock (stopped in the demo); the repeat and notice rules measure real time
        var now = DateTime.Now;
        var e = new LogEntry { Time = AppClock.Now, Level = level, Text = text };
        LogEntry repeated = null;
        _wroteHere = now;
        if (Paths.ReadOnly) { Added?.Invoke(e); return; }   // nothing goes to the log file then
        lock (Lock)
        {
            if (text == _lastText && level == _lastLevel && now - _lastAt < TimeSpan.FromMinutes(1) && now >= _lastAt)
            {
                _repeats++;
                _lastAt = now;
                return;
            }
            if (_repeats > 0) repeated = new LogEntry { Time = e.Time, Level = _lastLevel, Text = $"(the line above repeated {Fmt.Count(_repeats, "more time")})" };
            _lastText = text; _lastLevel = level; _lastAt = now; _repeats = 0;
            try
            {
                var path = Paths.NoLink(Paths.File("registo.log"));
                Paths.NoLink(path + ".old");
                if (!_aged) { _aged = true; AgeOut(path); }
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 2_000_000) System.IO.File.Move(path, path + ".old", true);
                var lines = repeated != null ? Line(repeated) : "";
                System.IO.File.AppendAllText(path, lines + Line(e));
            }
            catch { }
        }
        if (repeated != null) Added?.Invoke(repeated);
        Added?.Invoke(e);
    }

    static string Line(LogEntry e) => $"{e.Time:yyyy-MM-dd HH:mm:ss} [{e.Level}] {e.Text}{Environment.NewLine}";

    /// <summary>Once per start: the file goes to .old when it began more than <see cref="Keep"/> ago, and an .old that old goes away.</summary>
    public static void AgeOut(string path)
    {
        try
        {
            var old = path + ".old";
            if (System.IO.File.Exists(old) && Fmt.Stale(System.IO.File.GetLastWriteTime(old), Keep)) System.IO.File.Delete(old);
            if (FirstLineAt(path) is DateTime first && Fmt.Stale(first, Keep)) System.IO.File.Move(path, old, true);
        }
        catch { }
    }

    static DateTime? FirstLineAt(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        using var r = new StreamReader(path);
        var line = r.ReadLine();
        return line != null && line.Length >= 19 && DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>Settings > Delete log files: the file and its .old, and the crash file.</summary>
    public static int DeleteFiles()
    {
        var n = 0;
        lock (Lock)
            foreach (var f in new[] { "registo.log", "registo.log.old", "crash.txt" })
                try { var p = Paths.NoLink(Paths.File(f)); if (System.IO.File.Exists(p)) { System.IO.File.Delete(p); n++; } } catch { }
        return n;
    }
}

public sealed record ProcResult(int Code, string Output)
{
    public bool Ok => Code == 0;
}

public static class Proc
{
    /// <summary>
    /// "netsh" and "powershell" always come from System32. The app runs as admin, and a bare name would make
    /// Windows look in the app's own folder first (often Downloads), where anything could be planted.
    /// PowerShell modules are loaded by full path too (Guard.AutoMetricArgs).
    /// </summary>
    static string Resolve(string file)
    {
        if (Path.IsPathRooted(file)) return file;
        var sys = Environment.SystemDirectory;
        if (file.Equals("powershell", StringComparison.OrdinalIgnoreCase)) return Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
        return Path.Combine(sys, file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : file + ".exe");
    }

    /// <summary>Runs a program with no window and without blocking the UI.</summary>
    public static async Task<ProcResult> Run(string file, string args, int timeoutMs = 15000, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Resolve(file), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();
        var outDone = new TaskCompletionSource();
        var errDone = new TaskCompletionSource();
        p.OutputDataReceived += (_, e) => { if (e.Data == null) outDone.TrySetResult(); else lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) errDone.TrySetResult(); else lock (sb) sb.AppendLine(e.Data); };
        var exited = new TaskCompletionSource();
        p.Exited += (_, _) => exited.TrySetResult();
        try { p.Start(); }
        catch (Exception ex) { return new ProcResult(-1, ex.Message); }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // Waits for the process to EXIT, not for the streams to end: adb leaves a background server
        // that inherits the output and never closes it. WaitForExitAsync would wait for it forever.
        var timeout = Task.Delay(timeoutMs, ct);
        if (await Task.WhenAny(exited.Task, timeout) != exited.Task)
        {
            try { p.Kill(); } catch { }
            return new ProcResult(-2, "Timed out: " + Path.GetFileName(file) + " " + args);
        }
        await Task.WhenAny(Task.WhenAll(outDone.Task, errDone.Task), Task.Delay(400));
        lock (sb) return new ProcResult(p.ExitCode, sb.ToString().Trim());
    }
}

/// <summary>A streaming program is open: Hide usernames turns on by itself (Settings > Privacy), so a login never reaches a stream.</summary>
public static class Streaming
{
    // OBS Studio (64 and 32 bit) and Streamlabs Desktop. XSplit's process names aren't confirmed yet, so it isn't listed.
    static readonly string[] Names = { "obs64", "obs32", "obs", "Streamlabs OBS", "Streamlabs Desktop" };

    public static bool AppOpen(IEnumerable<string> processNames) =>
        processNames?.Any(p => Names.Contains(p?.Trim(), StringComparer.OrdinalIgnoreCase)) == true;

    /// <summary>Checks the running processes by name only (no handle to any of them).</summary>
    public static bool AppOpenNow()
    {
        try
        {
            var ps = Process.GetProcesses();
            try { return AppOpen(ps.Select(p => p.ProcessName)); }
            finally { foreach (var p in ps) p.Dispose(); }
        }
        catch { return false; }
    }
}

public static class Fmt
{
    /// <summary>"27 Oct 21:05": the same on every Windows language, never month/day mix-ups.</summary>
    public static string When(DateTime? t) => t?.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>"1 account", "3 accounts": the right word for the number, never "account(s)".</summary>
    /// <summary>Older than <paramref name="age"/>, or in the future (the clock was ahead when it was saved): due again.</summary>
    public static bool Stale(DateTime at, TimeSpan age) { var d = AppClock.Now - at; return d >= age || d < TimeSpan.Zero; }

    public static string Count(int n, string one, string many = null) => n == 1 ? "1 " + one : $"{n} {many ?? one + "s"}";

    public static string Ago(DateTime? t)
    {
        if (t == null) return "never";
        var d = AppClock.Now - t.Value;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    public static string Left(TimeSpan d)
    {
        if (d.TotalSeconds <= 0) return "ended";
        if (d.TotalDays >= 1) return $"{(int)d.TotalDays} d {d.Hours} h";
        if (d.TotalHours >= 1) return $"{(int)d.TotalHours} h {d.Minutes:00} min";
        return $"{d.Minutes} min {d.Seconds:00} s";
    }

    public static string MaskIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return "—";
        var p = ip.Split('.');
        return p.Length == 4 ? $"{p[0]}.{p[1]}.•••.•••" : "•••";
    }

    /// <summary>"3d 4h", "21h", "90m" or just hours ("36").</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        var total = TimeSpan.Zero;
        var any = false;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text ?? "", @"(\d+(?:[.,]\d+)?)\s*(d|h|m)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var n = double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            total += char.ToLowerInvariant(m.Groups[2].Value[0]) switch { 'd' => TimeSpan.FromDays(n), 'h' => TimeSpan.FromHours(n), _ => TimeSpan.FromMinutes(n) };
            any = true;
        }
        // Like the units above: "1.5" and "1,5" are both an hour and a half, whatever the PC's language (no thousands separator)
        if (!any && double.TryParse((text ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours)
            && hours > 0 && hours <= 3650 * 24) { total = TimeSpan.FromHours(hours); any = true; }
        return any && total.TotalMinutes >= 1 && total.TotalDays <= 3650 ? total : null;
    }
}

/// <summary>
/// Small secrets in the JSON files (the Steam Web API key): encrypted with DPAPI for this Windows user, as base64. Another Windows
/// user or another PC can't read them back; then <see cref="Open"/> gives null.
/// </summary>
public static class Secret
{
    static byte[] Entropy(string purpose) => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("CS2IPChanger/Secret/v1/" + purpose));

    public static string Seal(string plain, string purpose)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        try { return Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(bytes, Entropy(purpose), System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Open(string sealedText, string purpose)
    {
        if (string.IsNullOrEmpty(sealedText)) return null;
        try { return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(sealedText), Entropy(purpose), System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        catch { return null; }   // another Windows user's, another PC's, damaged or not base64
    }
}
