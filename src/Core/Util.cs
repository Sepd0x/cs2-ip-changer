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
    /// <summary>A versão do csproj (ex.: 0.1.0-alpha.1); a build de desenvolvimento acrescenta "+dev".</summary>
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
}

public static class Paths
{
    // Portátil: tudo fica ao lado do .exe, numa pasta própria.
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
            Log.Warn($"Não consegui ler {Path.GetFileName(path)} ({ex.Message}); guardei uma cópia .bak.");
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

public sealed class LogEntry
{
    public DateTime Time { get; init; }
    public LogLevel Level { get; init; }
    public string Text { get; init; }
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
    /// <summary>Corre um programa sem janela e sem bloquear a interface.</summary>
    public static async Task<ProcResult> Run(string file, string args, int timeoutMs = 15000, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file, args)
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

        // Espera pela SAÍDA do processo, não pelo fim das streams: o adb deixa um servidor em segundo plano
        // que herda a saída e nunca a fecha. WaitForExitAsync esperaria por ela para sempre.
        var timeout = Task.Delay(timeoutMs, ct);
        if (await Task.WhenAny(exited.Task, timeout) != exited.Task)
        {
            try { p.Kill(); } catch { }
            return new ProcResult(-2, "Tempo esgotado: " + Path.GetFileName(file) + " " + args);
        }
        await Task.WhenAny(Task.WhenAll(outDone.Task, errDone.Task), Task.Delay(400));
        lock (sb) return new ProcResult(p.ExitCode, sb.ToString().Trim());
    }
}

public static class Fmt
{
    public static string Ago(DateTime? t)
    {
        if (t == null) return "nunca";
        var d = DateTime.Now - t.Value;
        if (d.TotalSeconds < 60) return "agora";
        if (d.TotalMinutes < 60) return $"há {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"há {(int)d.TotalHours} h";
        return $"há {(int)d.TotalDays} d";
    }

    public static string Left(TimeSpan d)
    {
        if (d.TotalSeconds <= 0) return "terminou";
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

    /// <summary>"3d 4h", "21h", "90m" ou só horas ("36").</summary>
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
        if (!any && double.TryParse((text ?? "").Trim(), out var hours)) { total = TimeSpan.FromHours(hours); any = true; }
        return any && total.TotalMinutes >= 1 && total.TotalDays <= 3650 ? total : null;
    }
}
