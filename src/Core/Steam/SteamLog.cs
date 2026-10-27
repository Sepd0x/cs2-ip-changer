using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

public sealed record SteamLogEvent(DateTime Time, string Kind, string Line);

/// <summary>
/// Lê o registo de ligação do próprio Steam (logs\connection_log.txt) enquanto a app corre. É a fonte certa
/// para saber se a conta está ligada: o Steam escreve cada queda, religação e login, com a hora e o IP local,
/// também quando liga por UDP (que a tabela TCP não mostra).
/// </summary>
public sealed class SteamLogWatcher
{
    static readonly Regex Stamp = new(@"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\]", RegexOptions.Compiled);
    long _pos = -1;
    string _path;
    bool _loggingOff;

    public DateTime? LastLogon { get; private set; }
    public DateTime? LastDrop { get; private set; }
    /// <summary>true = sessão iniciada na Valve; false = caiu ou saiu; null = ainda não se sabe.</summary>
    public bool? Online => LastLogon == null && LastDrop == null ? null : LastDrop == null || LastLogon > LastDrop;

    /// <summary>Linhas novas desde a última leitura, já classificadas. A primeira leitura só acerta o estado.</summary>
    public List<SteamLogEvent> Poll()
    {
        var dir = Steam.InstallDir();
        return Poll(dir == null ? null : Path.Combine(dir, "logs", "connection_log.txt"));
    }

    public List<SteamLogEvent> Poll(string path)
    {
        var events = new List<SteamLogEvent>();
        if (path == null || !File.Exists(path)) return events;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var first = path != _path || _pos < 0;
            if (first) { _path = path; _pos = Math.Max(0, fs.Length - 256 * 1024); }
            if (fs.Length < _pos) _pos = 0;   // o Steam recomeçou o ficheiro
            if (fs.Length == _pos) return events;
            fs.Seek(_pos, SeekOrigin.Begin);
            var text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
            // Só se avança até à última linha completa.
            var end = text.LastIndexOf('\n');
            if (end < 0) return events;
            _pos += Encoding.UTF8.GetByteCount(text.AsSpan(0, end + 1));
            foreach (var raw in text[..(end + 1)].Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = Stamp.Match(line);
                if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                var kind = Classify(line);
                if (kind == null) continue;
                if (kind == "ligou") LastLogon = t;
                if (kind is "caiu" or "saiu") LastDrop = t;
                if (!first) events.Add(new SteamLogEvent(t, kind, line[(m.Length)..].Trim()));
            }
        }
        catch { }
        return events;
    }

    string Classify(string line)
    {
        if (line.Contains("LogOff()")) { _loggingOff = true; return "a-sair"; }
        if (line.Contains("ConnectionDisconnected(") && !line.Contains("not auto reconnecting"))
        {
            var kind = _loggingOff ? "saiu" : "caiu";
            _loggingOff = false;
            return kind;
        }
        if (line.Contains("RecvMsgClientLogOnResponse() : processing complete")) { _loggingOff = false; return "ligou"; }
        if (line.Contains("ConnectionCompleted(")) return "ligacao";
        if (line.Contains("StartAutoReconnect()")) return "religar";
        if (line.Contains("RecvMsgClientLogOnResponse()") && !line.Contains("'OK'")) return "recusado";
        return null;
    }
}
