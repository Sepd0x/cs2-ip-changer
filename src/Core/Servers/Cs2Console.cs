using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>Steam = Steam or VAC refused the account (a new IP doesn't help). NoAnswer = the server stopped answering (or never did).</summary>
public enum KickKind { None, Kick, Ban, Steam, NoAnswer }

/// <summary>
/// Reads CS2's console.log (written with -condebug) and spots a kick or ban from the server.
/// The exact Source 2 line format is not known yet, so the patterns are loose and the raw line always goes to the Log.
/// </summary>
public static class Cs2Console
{
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>...\game\bin\win64\cs2.exe -> ...\game\csgo\console.log</summary>
    public static string LogPath(string cs2Exe)
    {
        if (string.IsNullOrEmpty(cs2Exe)) return null;
        try
        {
            var game = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cs2Exe)!, "..", ".."));
            return Path.Combine(game, "csgo", "console.log");
        }
        catch { return null; }
    }

    // Chat, other players leaving and commands typed in the console are never about you.
    static readonly Regex[] Ignore =
    {
        new(@"^\s*(\*DEAD\*\s*)?(\[(ALL|CT|T|TEAM|SPEC|SPECTATOR)\]|\((Counter-)?Terrorist\)|\(Spectator\))", O),
        new(@"^\s*\]", O),
        new(@"left the game|player_disconnect", O),
        // "No Steam logon" is a Steam session problem, not an admin: a new IP would not help.
        new(@"NOSTEAMLOGIN|NOSTEAMTICKET|no steam logon|NETWORK_DISCONNECT_STEAM_LOGON\b", O),
    };

    // Steam or VAC said no: it's about the account, so changing the IP and rejoining is no use.
    // STEAM_BANNED is not one of them: it's the server's own ban list refusing the SteamID ("STEAM UserID ... is banned").
    static readonly Regex SteamStrong = new(@"NETWORK_DISCONNECT_KICKED_VAC\w*|NETWORK_DISCONNECT_(KICKED_)?VACBANNED|NETWORK_DISCONNECT_STEAM_(?!BANNED\b)\w*", O);

    // The line is about the connection ending or being refused.
    static readonly Regex Context = new(
        @"\bdisconnect(ed|ing|ion)?\b|NETWORK_DISCONNECT_|closed by peer|\b(connection|connect)\s+(was\s+)?rejected|\brejected\b.{0,40}\bconnect|server rejected", O);

    // Enough on their own (engine reason codes, or a message addressed to you).
    static readonly Regex[] BanStrong =
    {
        new(@"NETWORK_DISCONNECT_(KICK)?BANADDED", O),
        new(@"NETWORK_DISCONNECT_(REJECT_|STEAM_)?BANNED", O),
        new(@"\byou\s+(have\s+been|are|were|got)\s+(permanently\s+|temporarily\s+)?banned\b", O),
    };
    static readonly Regex[] KickStrong =
    {
        new(@"NETWORK_DISCONNECT_KICKED\w*", O),
        new(@"\byou\s+(have\s+been|were|got)\s+kicked\b", O),
    };

    // The connection to the server timed out: it never answered (a ban at its firewall looks like this) or stopped answering.
    static readonly Regex NoAnswer = new(@"NETWORK_DISCONNECT_(CONNECT_REQUEST_)?TIMEDOUT|NETWORK_DISCONNECT_CONNECTION_FAILURE|Connection failed after \d+ retries", O);

    static readonly Regex SignOn = new(@"Sign-on state:\s*(\d+)", O);

    /// <summary>"Sign-on state: 6 (SIGNONSTATE_FULL)" on a disconnect line: how far the join got (6 = in the game). Null if not on the line.</summary>
    public static int? SignOnState(string line)
    {
        var m = SignOn.Match(line ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    /// <summary>Which of two results of one disconnect counts: a ban over a Steam refusal over a kick over a timeout.</summary>
    public static KickKind Stronger(KickKind a, KickKind b) => Rank(b) > Rank(a) ? b : a;

    static int Rank(KickKind k) => k switch { KickKind.Ban => 4, KickKind.Steam => 3, KickKind.Kick => 2, KickKind.NoAnswer => 1, _ => 0 };

    // Only with a disconnect or reject on the same line.
    static readonly Regex BanWeak = new(@"\b(banned|ban(id|ip)?|banadded)\b", O);
    static readonly Regex KickWeak = new(@"\bkick(ed)?\b", O);

    /// <summary>Kick, ban or nothing. Timeouts, quitting and map changes are nothing.</summary>
    static readonly Regex Connected = new(@"Connected to '(\d{1,3}(?:\.\d{1,3}){3}:\d{1,5})'", O);

    /// <summary>"CL:  Connected to '1.2.3.4:27015'" (loopback and local maps return null).</summary>
    public static string ConnectedServer(string line)
    {
        var m = Connected.Match(line ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    static readonly Regex AnyConnect = new(@"Connected to '[^']*'", O);

    /// <summary>Any "Connected to '…'": a server, the main menu (loopback) or a Valve relay. CS2 is somewhere else now.</summary>
    public static bool IsConnectLine(string line) => AnyConnect.IsMatch(line ?? "");

    static readonly Regex NoSteamLogon = new(@"NOSTEAMLOGIN|NOSTEAMTICKET|no steam logon|NETWORK_DISCONNECT_STEAM_LOGON\b", O);

    /// <summary>The "No Steam logon" kick: Steam lost its session (another network, a crash). Not the admin, and a new IP won't help.</summary>
    public static bool IsNoSteamLogon(string line) => !string.IsNullOrWhiteSpace(line) && NoSteamLogon.IsMatch(line) && !Ignore.Take(3).Any(r => r.IsMatch(line));

    public static KickKind Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || Ignore.Any(r => r.IsMatch(line))) return KickKind.None;
        if (SteamStrong.IsMatch(line)) return KickKind.Steam;
        if (BanStrong.Any(r => r.IsMatch(line))) return KickKind.Ban;
        if (KickStrong.Any(r => r.IsMatch(line))) return KickKind.Kick;
        if (NoAnswer.IsMatch(line)) return KickKind.NoAnswer;
        if (!Context.IsMatch(line)) return KickKind.None;
        if (BanWeak.IsMatch(line)) return KickKind.Ban;
        if (KickWeak.IsMatch(line)) return KickKind.Kick;
        return KickKind.None;
    }

    /// <summary>Any disconnect line that is not chat (logged as a sample to learn the real format).</summary>
    public static bool IsDisconnect(string line) =>
        !string.IsNullOrWhiteSpace(line) && Context.IsMatch(line) && !Ignore.Take(3).Any(r => r.IsMatch(line));
}

/// <summary>Follows a growing text file: returns only the complete lines added since the last read.</summary>
public sealed class ConsoleTail
{
    string _path;
    long _pos = -1;

    public void Reset() { _path = null; _pos = -1; }

    public List<string> ReadNew(string path)
    {
        var lines = new List<string>();
        if (path == null || !File.Exists(path)) return lines;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // First read: what is already there is old, start from the end.
            if (path != _path || _pos < 0) { _path = path; _pos = fs.Length; return lines; }
            if (fs.Length < _pos) _pos = 0;   // CS2 started the file again
            if (fs.Length == _pos) return lines;
            var start = Math.Max(_pos, fs.Length - 512 * 1024);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            var n = 0;
            while (n < buf.Length) { var r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
            // A line still being written stays for the next read.
            var end = Array.LastIndexOf(buf, (byte)'\n', Math.Max(0, n - 1));
            if (end < 0) { if (start > _pos) _pos = start; return lines; }
            _pos = start + end + 1;
            foreach (var l in Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
            {
                var t = l.TrimEnd('\r');
                if (t.Length > 0) lines.Add(t);
            }
        }
        catch { }
        return lines;
    }
}
