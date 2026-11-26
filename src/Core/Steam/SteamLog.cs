using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

public sealed record SteamLogEvent(DateTime Time, string Kind, string Line);

/// <summary>
/// Reads Steam's own connection log (logs\connection_log.txt) while the app runs. It is the right source
/// to know whether the account is online: Steam writes every drop, reconnect and logon, with the time and local IP,
/// also when it connects over UDP (which the TCP table does not show).
/// </summary>
public sealed class SteamLogWatcher
{
    static readonly Regex Stamp = new(@"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\]", RegexOptions.Compiled);
    long _pos = -1;
    string _path;
    // Audit 26 nov. (A16): the file's encoding, from its byte order mark at the start, kept for the reads after the first (they start
    // in the middle, where there is no mark), and the length of that mark
    Encoding _enc;
    int _bom;
    bool _loggingOff;

    public DateTime? LastLogon { get; private set; }
    public DateTime? LastDrop { get; private set; }
    /// <summary>
    /// true = logged on to Valve; false = dropped or logged off; null = not known yet. From the last of those lines in the file:
    /// a drop and a logon in the same second (after the app cuts Steam's old connections) end online, not offline.
    /// </summary>
    public bool? Online { get; private set; }

    /// <summary>New lines since the last read, already classified. The first read only sets the state.</summary>
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
            if (first) { _path = path; _pos = Math.Max(0, fs.Length - 256 * 1024); _enc = null; }
            if (fs.Length < _pos) { _pos = 0; _enc = null; }   // Steam restarted the file
            if (_enc == null) (_enc, _bom) = EncodingOf(fs);
            if (_pos < _bom) _pos = _bom;
            if (_enc is UnicodeEncoding && (_pos - _bom) % 2 != 0) _pos++;   // UTF-16: start on a whole character
            if (fs.Length <= _pos) return events;
            fs.Seek(_pos, SeekOrigin.Begin);
            var text = new StreamReader(fs, _enc, detectEncodingFromByteOrderMarks: false).ReadToEnd();
            // Only advance to the last complete line.
            var end = text.LastIndexOf('\n');
            if (end < 0) return events;
            _pos += _enc.GetByteCount(text.AsSpan(0, end + 1));
            foreach (var raw in text[..(end + 1)].Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = Stamp.Match(line);
                if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                var kind = Classify(line);
                if (kind == null) continue;
                if (kind == "online") { LastLogon = t; Online = true; }
                if (kind is "dropped" or "offline") { LastDrop = t; Online = false; }
                if (!first) events.Add(new SteamLogEvent(t, kind, line[(m.Length)..].Trim()));
            }
        }
        catch { }
        return events;
    }

    /// <summary>The encoding by the byte order mark at the file's start (UTF-16 LE or BE, UTF-8), UTF-8 without one, and the mark's length.</summary>
    public static (Encoding Encoding, int Bom) EncodingOf(Stream fs)
    {
        var b = new byte[3];
        fs.Seek(0, SeekOrigin.Begin);
        var n = fs.ReadAtLeast(b, 3, throwOnEndOfStream: false);
        if (n >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (new UnicodeEncoding(false, false), 2);
        if (n >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (new UnicodeEncoding(true, false), 2);
        if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (new UTF8Encoding(false), 3);
        return (new UTF8Encoding(false), 0);
    }

    string Classify(string line)
    {
        if (line.Contains("LogOff()")) { _loggingOff = true; return "logging-off"; }
        if (line.Contains("ConnectionDisconnected(") && !line.Contains("not auto reconnecting"))
        {
            var kind = _loggingOff ? "offline" : "dropped";
            _loggingOff = false;
            return kind;
        }
        if (line.Contains("RecvMsgClientLogOnResponse() : processing complete")) { _loggingOff = false; return "online"; }
        if (line.Contains("ConnectionCompleted(")) return "connected";
        if (line.Contains("StartAutoReconnect()")) return "reconnecting";
        if (line.Contains("RecvMsgClientLogOnResponse()") && !line.Contains("'OK'")) return "refused";
        return null;
    }
}
