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
    public static readonly string Data = Path.Combine(AppDir, "SRT_Dados");
    public static readonly string Avatars = Path.Combine(Data, "avatars");
    public static string File(string name) => Path.Combine(Data, name);

    public static void Ensure()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Avatars);
    }
}

public static class Store
{
    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                var raw = System.IO.File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
                if (!string.IsNullOrWhiteSpace(raw)) return JsonSerializer.Deserialize<T>(raw, Opt) ?? fallback();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read {Path.GetFileName(path)} ({ex.Message}); saved a .bak copy.");
            try { System.IO.File.Copy(path, path + ".bak", true); } catch { }
        }
        return fallback();
    }

    static readonly object SaveLock = new();

    public static void Save<T>(string path, T value)
    {
        lock (SaveLock)
        {
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opt), new UTF8Encoding(false));
            System.IO.File.Move(tmp, path, true);
        }
    }
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

    static void Write(LogLevel level, string text)
    {
        var e = new LogEntry { Time = DateTime.Now, Level = level, Text = text };
        try
        {
            lock (Lock)
            {
                var path = Paths.File("registo.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 2_000_000) System.IO.File.Move(path, path + ".old", true);
                System.IO.File.AppendAllText(path, $"{e.Time:yyyy-MM-dd HH:mm:ss} [{level}] {text}{Environment.NewLine}");
            }
        }
        catch { }
        Added?.Invoke(e);
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

public static class Fmt
{
    /// <summary>"27 Oct 21:05": the same on every Windows language, never month/day mix-ups.</summary>
    public static string When(DateTime? t) => t?.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>"1 account", "3 accounts": the right word for the number, never "account(s)".</summary>
    public static string Count(int n, string one, string many = null) => n == 1 ? "1 " + one : $"{n} {many ?? one + "s"}";

    public static string Ago(DateTime? t)
    {
        if (t == null) return "never";
        var d = DateTime.Now - t.Value;
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
