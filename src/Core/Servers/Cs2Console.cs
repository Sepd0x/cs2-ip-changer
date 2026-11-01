using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// How CS2 left a server. Steam = Steam or VAC refused the account (a new IP doesn't help). NoAnswer = the connection: the server
/// stopped answering (or never did). Vpn = the server blocks VPNs and proxies (said in the console before the kick or ban).
/// Full = the server is full (or made room for a reserved slot). MapChange and Quit are not about you: nothing to offer.
/// </summary>
public enum KickKind { None, Kick, Ban, Steam, NoAnswer, Vpn, Full, MapChange, Quit }

/// <summary>
/// One disconnect from a community server, as the app understood it. Reason: in words, from the code or the server's message
/// (null if neither says). Minutes: how long the ban is, if the server said (0 = permanent). BanCode: the engine code itself
/// was a ban (BANADDED, KICKBANADDED, REJECT_BANNED, STEAM_BANNED), also under a VPN block.
/// </summary>
public sealed record Disconnect(KickKind Kind, string Line, string Server, string Reason = null, int? Minutes = null, bool BanCode = false, int? SignOn = null)
{
    /// <summary>"for 30 minutes", "permanently" or empty.</summary>
    public string For => Minutes switch
    {
        null => "",
        0 => " permanently",
        < 60 => $" for {Fmt.Count(Minutes.Value, "minute")}",
        < 60 * 24 when Minutes % 60 == 0 => $" for {Fmt.Count(Minutes.Value / 60, "hour")}",
        _ when Minutes % (60 * 24) == 0 => $" for {Fmt.Count(Minutes.Value / (60 * 24), "day")}",
        _ => $" for {Fmt.Count(Minutes.Value, "minute")}",
    };

    /// <summary>Only a ban or a VPN block brings New IP + next account; a kick only a rejoin.</summary>
    public bool WantsSwitch => Kind is KickKind.Ban or KickKind.Vpn;
}

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
    // Untrusted, convicted, insecure (-insecure), input automation and Valve's cooldown are about the account or the game too.
    static readonly Regex SteamStrong = new(@"NETWORK_DISCONNECT_KICKED_VAC\w*|NETWORK_DISCONNECT_(KICKED_)?VACBANNED|NETWORK_DISCONNECT_STEAM_(?!BANNED\b)\w*"
        + @"|NETWORK_DISCONNECT_KICKED_(UNTRUSTEDACCOUNT|CONVICTEDACCOUNT|INSECURECLIENT|INPUTAUTOMATION|COMPETITIVECOOLDOWN)\b|NETWORK_DISCONNECT_REJECT_STEAM\b", O);

    // The line is about the connection ending or being refused.
    static readonly Regex Context = new(
        @"\bdisconnect(ed|ing|ion)?\b|NETWORK_DISCONNECT_|closed by peer|\b(connection|connect)\s+(was\s+)?rejected|\brejected\b.{0,40}\bconnect|server rejected", O);

    // Enough on their own (engine reason codes, or a message addressed to you). Ban codes: SimpleAdmin and CS2Fixes ban with
    // KICKBANADDED, a banned player joining again gets REJECT_BANNED (SimpleAdmin) or KICKBANADDED (CS2Fixes); STEAM_BANNED is
    // the server's ban list (seen on 1 Nov. 2026, in game).
    static readonly Regex BanCode = new(@"NETWORK_DISCONNECT_(KICK)?BANADDED\b|NETWORK_DISCONNECT_(REJECT_|STEAM_)?BANNED\b", O);
    static readonly Regex[] BanStrong =
    {
        BanCode,
        new(@"\byou\s+(have\s+been|are|were|got)\s+(permanently\s+|temporarily\s+)?banned\b(?!\s+from\s+(the\s+|using\s+)?(chat|voice|mic))", O),
    };
    static readonly Regex[] KickStrong =
    {
        new(@"NETWORK_DISCONNECT_KICKED\w*", O),
        // The game refused the join (a plugin's ClientConnect said no) or the server dropped you, with no reason given
        new(@"NETWORK_DISCONNECT_(REJECTED_BY_GAME|DISCONNECT_BY_SERVER)\b", O),
        new(@"\byou\s+(have\s+been|were|got)\s+kicked\b", O),
    };

    // Full: the engine's own refusal, or a reserved slots plugin making room (it kicks with the same code)
    static readonly Regex FullCode = new(@"NETWORK_DISCONNECT_REJECT_(SERVERFULL|RESERVED_FOR_LOBBY)\b", O);

    // The connection to the server timed out or broke: it never answered or stopped answering, or the network dropped.
    static readonly Regex NoAnswer = new(@"NETWORK_DISCONNECT_(CONNECT_REQUEST_TIMEDOUT|TIMEDOUT|CONNECTION_FAILURE|LOST|REMOTE_TIMEOUT(_CONNECTING)?|LOCALPROBLEM_\w+)\b|Connection failed after \d+ retries", O);

    // Not about you: a map change (a workshop map is downloaded, then CS2 joins again), a restart, a new server.
    static readonly Regex MapChangeCode = new(@"NETWORK_DISCONNECT_(LOOPDEACTIVATE|LOOP_LEVELLOAD_ACTIVATE|RECONNECTION|SERVER_SHUTDOWN|HOST_ENDGAME|CLIENT_DIFFERENT_MAP|CHANGELEVEL)\b", O);
    // You left: the menu, the console's disconnect, quitting the game.
    static readonly Regex QuitCode = new(@"NETWORK_DISCONNECT_(DISCONNECT_BY_USER|EXITING|REQUEST_HOSTSTATE_IDLE|LOOPSHUTDOWN|SHUTDOWN)\b", O);

    static readonly Regex SignOn = new(@"Sign-on state:\s*(\d+)", O);

    /// <summary>"Sign-on state: 6 (SIGNONSTATE_FULL)" on a disconnect line: how far the join got (6 = in the game). Null if not on the line.</summary>
    public static int? SignOnState(string line)
    {
        var m = SignOn.Match(line ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    /// <summary>Which of two results of one disconnect counts: a VPN block over a ban over a Steam refusal over full over a kick over a timeout.</summary>
    public static KickKind Stronger(KickKind a, KickKind b) => Rank(b) > Rank(a) ? b : a;

    static int Rank(KickKind k) => k switch
    {
        KickKind.Vpn => 8, KickKind.Ban => 7, KickKind.Steam => 6, KickKind.Full => 5, KickKind.Kick => 4, KickKind.NoAnswer => 3,
        KickKind.MapChange => 2, KickKind.Quit => 1, _ => 0,
    };

    /// <summary>Kinds the app says something about (a map change, quitting or nothing are silent).</summary>
    public static bool Acts(KickKind k) => k is KickKind.Kick or KickKind.Ban or KickKind.Steam or KickKind.NoAnswer or KickKind.Vpn or KickKind.Full;

    // Only with a disconnect or reject on the same line.
    static readonly Regex BanWeak = new(@"\b(banned|ban(id|ip)?|banadded)\b", O);
    static readonly Regex KickWeak = new(@"\bkick(ed)?\b", O);

    static readonly Regex Connected = new(@"Connected to '(\d{1,3}(?:\.\d{1,3}){3}:\d{1,5})'", O);

    /// <summary>"CL:  Connected to '1.2.3.4:27015'" (loopback, local maps and Valve's servers return null).</summary>
    public static string ConnectedServer(string line)
    {
        var m = Connected.Match(line ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    static readonly Regex AnyConnect = new(@"Connected to '[^']*'", O);

    /// <summary>Any "Connected to '…'": a server, the main menu (loopback) or a Valve relay. CS2 is somewhere else now.</summary>
    public static bool IsConnectLine(string line) => AnyConnect.IsMatch(line ?? "");

    // Valve's own servers (matchmaking) are reached through Valve's relays (SDR) by an anonymous game server SteamID:
    // "Connected to '=[A:1:4005189648:51168]'". A community server is an address ("1.2.3.4:27015").
    static readonly Regex ValveConnect = new(@"Connected to '=?\[A:1:\d+:\d+\]", O);

    /// <summary>A Valve server (matchmaking): kicks and bans there aren't the community's, so the app leaves them alone.</summary>
    public static bool IsValveServer(string line) => ValveConnect.IsMatch(line ?? "");

    static readonly Regex NoSteamLogon = new(@"NOSTEAMLOGIN|NOSTEAMTICKET|no steam logon|NETWORK_DISCONNECT_STEAM_LOGON\b", O);

    /// <summary>The "No Steam logon" kick: Steam lost its session (another network, a crash). Not the admin, and a new IP won't help.</summary>
    public static bool IsNoSteamLogon(string line) => !string.IsNullOrWhiteSpace(line) && NoSteamLogon.IsMatch(line) && !Ignore.Take(3).Any(r => r.IsMatch(line));

    public static KickKind Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || Ignore.Any(r => r.IsMatch(line))) return KickKind.None;
        if (SteamStrong.IsMatch(line)) return KickKind.Steam;
        if (BanStrong.Any(r => r.IsMatch(line))) return KickKind.Ban;
        if (FullCode.IsMatch(line)) return KickKind.Full;
        if (KickStrong.Any(r => r.IsMatch(line))) return KickKind.Kick;
        if (NoAnswer.IsMatch(line)) return KickKind.NoAnswer;
        if (MapChangeCode.IsMatch(line)) return KickKind.MapChange;
        if (QuitCode.IsMatch(line)) return KickKind.Quit;
        if (!Context.IsMatch(line)) return KickKind.None;
        if (BanWeak.IsMatch(line)) return KickKind.Ban;
        if (KickWeak.IsMatch(line)) return KickKind.Kick;
        return KickKind.None;
    }

    /// <summary>The engine's own code was a ban (not a message): the account is banned there, also when the reason was a VPN.</summary>
    public static bool IsBanCode(string line) => !string.IsNullOrEmpty(line) && BanCode.IsMatch(line);

    static readonly Regex Code = new(@"NETWORK_DISCONNECT_[A-Z_]+", O);

    /// <summary>What a code means, in words for the toast and the Log (null when the code says no more than "kicked").</summary>
    public static string ReasonOf(string line)
    {
        var c = Code.Match(line ?? "").Value.ToUpperInvariant();
        return c switch
        {
            "NETWORK_DISCONNECT_KICKED_VOTEDOFF" => "kicked by a vote",
            "NETWORK_DISCONNECT_KICKED_IDLE" => "kicked for being AFK",
            "NETWORK_DISCONNECT_KICKED_TEAMKILLING" or "NETWORK_DISCONNECT_KICKED_TK_START" => "kicked for team kills",
            "NETWORK_DISCONNECT_KICKED_TEAMHURTING" => "kicked for team damage",
            "NETWORK_DISCONNECT_KICKED_HOSTAGEKILLING" => "kicked for killing hostages",
            "NETWORK_DISCONNECT_KICKED_SUICIDE" => "kicked for suicides",
            "NETWORK_DISCONNECT_REJECTED_BY_GAME" => "the server refused the join",
            "NETWORK_DISCONNECT_DISCONNECT_BY_SERVER" => "the server dropped you",
            "NETWORK_DISCONNECT_REJECT_BANNED" => "the server refused you: banned",
            "NETWORK_DISCONNECT_STEAM_BANNED" => "on the server's ban list",
            "NETWORK_DISCONNECT_REJECT_RESERVED_FOR_LOBBY" => "reserved for a lobby",
            "NETWORK_DISCONNECT_CONNECT_REQUEST_TIMEDOUT" or "NETWORK_DISCONNECT_REMOTE_TIMEOUT_CONNECTING" => "the server never answered",
            "NETWORK_DISCONNECT_SERVER_SHUTDOWN" => "the server shut down or restarted",
            _ => null,
        };
    }

    // ------------------------------------------------------------ what the server says before it kicks

    // The kick text itself never reaches CS2 (the engine sends only a code), but plugins tell the player first, in chat or the
    // middle of the screen: SimpleAdmin "You have been banned for {reason} for {N} minutes by {admin}!" (5 s before the kick),
    // VPN blockers "VPN/Proxy detected! Disconnecting...", AFK managers "You're Idle/AFK. Move or you'll be kicked". Such a line
    // only describes a disconnect that comes with a code; on its own it never counts (another player's ban is said to everyone).
    static readonly Regex VpnText = new(@"\b(vpn|proxy|proxies|vps)\b.{0,40}\b(detected|not allowed|blocked|forbidden|disable|usage|prohibited)\b|\b(detected|no|anti)[\s-]?(vpn|proxy)\b", O);
    static readonly Regex YouBanned = new(@"\byou\s+(have\s+been|are|were|got)\s+(permanently\s+|temporarily\s+)?banned\b(?!\s+from\s+(the\s+|using\s+)?(chat|voice|mic))", O);
    static readonly Regex YouKicked = new(@"\byou\s+(have\s+been|were|got)\s+kicked\b(?:\s+for\s+(?<why>.+?))?(?:\s+by\s|[!.]|$)", O);
    static readonly Regex Afk = new(@"\byou('re|\s+are)\s+(idle|afk)\b|\byou\b.{0,40}\b(afk|idle)\b|\b(afk|idle)\b.{0,40}\byou('ll|\s+will)\b", O);
    static readonly Regex Reserved = new(@"\breserved\s+slot", O);
    static readonly Regex Duration = new(@"\bfor\s+(\d{1,7})\s*(seconds?|secs?|minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|w|months?|years?)\b", O);
    static readonly Regex Permanent = new(@"\bpermanent(ly)?\b|\bforever\b", O);
    // Messages to everyone about someone else ("Foo was kicked: VPN detected", "Player Foo ...") are never about you
    static readonly Regex ThirdPerson = new(@"\b(was|has\s+been|got|is\s+being)\s+(kicked|banned|removed|disconnected)\b|\bplayer\b", O);
    static readonly Regex You = new(@"\byou(r|'re)?\b", O);
    // The console's time and the plugin's tags in front ("11/01 18:54:41 [AntiVPN] "), so a message that starts with "VPN" can be seen
    static readonly Regex Lead = new(@"^\s*(\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}\s+)?((\[[^\]]{1,30}\]|\{[^}]{1,30}\})\s*)*", O);
    static readonly Regex VpnFirst = new(@"^(vpn|proxy)\b[\w/ ]{0,20}?\s*(detected|is not allowed|not allowed|blocked)\b", O);

    /// <summary>
    /// What a server message before the kick adds: VPN, a ban and its length, the kick's reason. Null for other lines, and for
    /// lines about another player: only a message to you ("you", or a VPN notice that starts the message) counts.
    /// </summary>
    public static Hint HintOf(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || Ignore.Take(3).Any(r => r.IsMatch(line)) || ThirdPerson.IsMatch(line)) return null;
        var you = You.IsMatch(line);
        if (VpnText.IsMatch(line) && (you || VpnFirst.IsMatch(Lead.Replace(line, ""))))
            return new Hint(Vpn: true, Ban: YouBanned.IsMatch(line), Minutes: MinutesIn(line), Reason: "VPN or proxy blocked");
        if (YouBanned.IsMatch(line)) return new Hint(false, true, MinutesIn(line), null);
        var k = YouKicked.Match(line);
        if (k.Success) return new Hint(false, false, null, k.Groups["why"].Success ? "kicked for " + k.Groups["why"].Value.Trim().TrimEnd('!', '.') : null);
        if (Afk.IsMatch(line)) return new Hint(false, false, null, "kicked for being AFK");
        if (you && Reserved.IsMatch(line) && KickWeak.IsMatch(line)) return new Hint(false, false, null, "kicked to make room for a reserved slot", Full: true);
        return null;
    }

    /// <summary>"for 30 minutes" = 30, "for 2 hours" = 120, "permanently" = 0; the last length on the line (the reason can say "for" too). A length said wins over "permanent".</summary>
    public static int? MinutesIn(string line)
    {
        var all = Duration.Matches(line ?? "");
        if (all.Count == 0) return Permanent.IsMatch(line ?? "") ? 0 : null;
        var m = all[^1];
        if (!long.TryParse(m.Groups[1].Value, out var n)) return null;
        var u = m.Groups[2].Value.ToLowerInvariant();
        long minutes = u.StartsWith("s") ? (n + 59) / 60 : u.StartsWith("mo") ? n * 60 * 24 * 30 : u.StartsWith("m") ? n
            : u.StartsWith("h") ? n * 60 : u.StartsWith("d") ? n * 60 * 24 : u.StartsWith("w") ? n * 60 * 24 * 7 : n * 60 * 24 * 365;
        return minutes is > 0 and <= int.MaxValue ? (int)minutes : null;
    }

    /// <summary>Any disconnect line that is not chat (logged as a sample to learn the real format).</summary>
    public static bool IsDisconnect(string line) =>
        !string.IsNullOrWhiteSpace(line) && Context.IsMatch(line) && !Ignore.Take(3).Any(r => r.IsMatch(line));
}

/// <summary>A server message before a disconnect: whether it was about a VPN, a ban, its length (0 = permanent), the reason, a full server.</summary>
public sealed record Hint(bool Vpn, bool Ban, int? Minutes, string Reason, bool Full = false);

/// <summary>
/// Follows CS2's console as it comes, one batch of new lines at a time: which server CS2 is on (a community server, the menu or
/// a Valve server) and the strongest disconnect of the batch, with what the server said in the half minute before it.
/// Valve's servers (matchmaking) are left alone: no kick, ban or rejoin there.
/// </summary>
public sealed class DisconnectTracker
{
    /// <summary>The community server CS2 is on now ("1.2.3.4:27015"); null in the menu, on a local map or on a Valve server.</summary>
    public string Server { get; private set; }
    /// <summary>CS2 is on a Valve server (matchmaking, through Valve's relays).</summary>
    public bool OnValve { get; private set; }

    static readonly TimeSpan HintWindow = TimeSpan.FromSeconds(30);
    Hint _hint;
    DateTime _hintAt;
    KickKind _kind;
    string _line, _server;
    bool _banCode;
    int? _signOn;

    /// <summary>One console line. Connect lines move CS2 somewhere else; a disconnect line counts if it's the strongest of the batch.</summary>
    public void Feed(string line, DateTime now)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (Cs2Console.IsConnectLine(line))
        {
            OnValve = Cs2Console.IsValveServer(line);
            // The menu (loopback) or a Valve relay counts too: a later kick there is not about the last community server.
            // The kick read before it keeps the server it happened on; joining another community server drops it.
            Server = Cs2Console.ConnectedServer(line);
            // What the last server said doesn't follow CS2 to the next one
            if (Server != null || OnValve) { Reset(); _hint = null; }
            return;
        }
        if (OnValve) return;
        if (Cs2Console.HintOf(line) is Hint h) { _hint = h; _hintAt = now; }
        var k = Cs2Console.Classify(line);
        if (Cs2Console.IsBanCode(line)) _banCode = true;
        // The furthest the join got, from this disconnect's own lines
        if (k != KickKind.None) _signOn = Cs2Console.SignOnState(line) ?? _signOn;
        if (k != KickKind.None && Cs2Console.Stronger(_kind, k) != _kind) { _kind = k; _line = line; _server = Server; }
    }

    /// <summary>The disconnect of this batch, if any, and starts the next batch. Null when nothing happened (or only on a Valve server).</summary>
    public Disconnect Take(DateTime now)
    {
        if (_kind == KickKind.None) return null;
        var kind = _kind;
        var reason = Cs2Console.ReasonOf(_line);
        int? minutes = Cs2Console.MinutesIn(_line) is int lm && Cs2Console.Classify(_line) == KickKind.Ban && !_line.Contains("NETWORK_DISCONNECT_") ? lm : null;
        var hint = _hint != null && now - _hintAt <= HintWindow ? _hint : null;
        if (hint != null && kind is KickKind.Kick or KickKind.Ban or KickKind.Full)
        {
            if (hint.Vpn) { kind = KickKind.Vpn; reason = hint.Reason; }
            else if (hint.Ban && kind == KickKind.Kick) kind = KickKind.Ban;   // a ban plugin that kicks with a plain "kicked"
            else if (hint.Full && kind == KickKind.Kick) { kind = KickKind.Full; reason = hint.Reason; }
            else if (hint.Reason != null && kind == KickKind.Kick && reason == null) reason = hint.Reason;
            else if (hint.Full && kind == KickKind.Full) reason = hint.Reason;
            minutes ??= hint.Minutes;
            _hint = null;   // used: a second disconnect doesn't take it again
        }
        // Kicked before getting into the game, with no reason given: that's how ban plugins refuse a banned player who joins again.
        if (kind == KickKind.Kick && reason == null && _signOn is int st && st < 6) { kind = KickKind.Ban; reason = $"kicked while still joining (sign-on state {st})"; }
        // Full while already in the game: a reserved slots plugin made room
        if (kind == KickKind.Full && reason == null) reason = _signOn == 6 ? "kicked to make room for a reserved slot" : "the server is full";
        if (kind == KickKind.Ban && reason == null && _banCode) reason = "banned";
        var d = new Disconnect(kind, _line, _server, reason, kind is KickKind.Ban or KickKind.Vpn ? minutes : null, _banCode, _signOn);
        Reset();
        return d;
    }

    void Reset() { _kind = KickKind.None; _line = _server = null; _banCode = false; _signOn = null; }

    /// <summary>CS2 closed: the next session starts nowhere, with nothing said before.</summary>
    public void Forget() { Server = null; OnValve = false; _hint = null; Reset(); }
}

/// <summary>
/// What CS2 writes to its console when it leaves a server ("Source2 engine networking summary"): UPSTREAM is what your PC sent,
/// DOWNSTREAM what the server sent. On mobile data the ping is usually fine and the trouble is upload jitter: your messages
/// reach the server late and it smooths them, which feels like lag.
/// </summary>
public sealed class NetReport
{
    public int Seconds { get; set; }
    public int? PingP50 { get; set; }
    public int? PingP95 { get; set; }
    public int? JitterUp { get; set; }
    public int? JitterDown { get; set; }
    /// <summary>Your messages that reached the server late and were corrected or smoothed (%).</summary>
    public double? UpLate { get; set; }
    /// <summary>Your messages the server missed because they came too late (%).</summary>
    public double? UpMissedLate { get; set; }
    /// <summary>Server ticks your PC missed because they came too late (%).</summary>
    public double? DownMissedLate { get; set; }
    /// <summary>Packets lost on the way, both directions.</summary>
    public int Dropped { get; set; }

    /// <summary>
    /// Late messages or late server ticks enough to feel in game (a guess from the 1 Nov. 4G matches, 58 to 64% late, until a
    /// wired match gives a baseline). A dropped packet now and then is not.
    /// </summary>
    public bool Laggy => UpLate >= 25 || UpMissedLate >= 2 || DownMissedLate >= 2;

    /// <summary>One short Log line, to compare matches by the phone's place, its IP and the time.</summary>
    public string Line(string via, string server) =>
        $"CS2 network after {(Seconds >= 60 ? $"{Seconds / 60} min" : $"{Seconds} s")}" + (server != null ? $" on {server}" : "") + $" ({via}): "
        + $"ping {N(PingP50)} ms (P95 {N(PingP95)}), jitter {N(JitterUp)} ms up, {N(JitterDown)} ms down, "
        + $"your messages late {P(UpLate)}, lost late {P(UpMissedLate)}, server ticks lost late {P(DownMissedLate)}, "
        + Fmt.Count(Dropped, "packet") + " dropped.";

    /// <summary>
    /// What usually helps on mobile data, for a laggy match and at the end of Phone setup. Only things on the phone and the cable:
    /// no CS2 console commands (the CS:GO ones people pass around, like cl_interp, don't exist in CS2).
    /// </summary>
    public static string Tips(bool hotspot) =>
        (hotspot ? "Use the USB cable instead of the hotspot (Wi-Fi adds its own jitter). " : "")
        + "Turn off the phone's battery saver, use 5G if your plan and the area have it, and keep the phone a little away from the PC case "
        + "(near a window helps the signal).";

    static string N(int? v) => v?.ToString() ?? "?";
    static string P(double? v) => v is double d ? d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "?";
}

/// <summary>Reads the network summary from the console lines as they come; returns it once the summary ends.</summary>
public sealed class NetReportReader
{
    const RegexOptions O = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    static readonly Regex Start = new(@"Source2 engine networking summary\.\s+([\d,]+) seconds", O);
    // Report lines are indented under [Client]; "[Client] Disconnecting from server" (one space) ends the summary.
    // Lines of other channels ("[Localization System] ...") can come in between and are skipped.
    static readonly Regex Body = new(@"\[Client\]\s{3,}(\S.*)$", O);
    static readonly Regex ClientLine = new(@"\[Client\]", O);
    static readonly Regex Pct = new(@"^[\d,]+ \(([\d.]+)%\)\s+([\d,]+) \([\d.]+%\) dropped\s+[\d,]+ \(([\d.]+)%\) late", O);
    static readonly Regex P50 = new(@"\bP50:(\d+)", O);
    static readonly Regex P95 = new(@"\bP95:(\d+)", O);
    static readonly Regex Lost = new(@"^[\d,]+ \([\d.]+%\)\s+([\d,]+) \([\d.]+%\) dropped", O);

    NetReport _r;
    bool _down;
    int _skipped;

    /// <summary>
    /// Feeds one console line. Returns the finished summary on the first [Client] line after it (or a new summary), null otherwise.
    /// </summary>
    public NetReport Feed(string line)
    {
        if (line == null) return null;
        var s = Start.Match(line);
        if (s.Success)
        {
            var before = Flush();
            _r = new NetReport { Seconds = Int(s.Groups[1].Value) ?? 0 };
            return before;
        }
        if (_r == null) return null;
        var b = Body.Match(line);
        if (!b.Success)
        {
            // Another channel's line in the middle of the summary; a long run of them means the summary was cut short
            if (!ClientLine.IsMatch(line) && ++_skipped < 50) return null;
            return Flush();
        }
        var text = b.Groups[1].Value;
        var colon = text.IndexOf(':');
        var name = (colon < 0 ? text : text[..colon]).TrimEnd(' ', '.');
        var value = colon < 0 ? "" : text[(colon + 1)..].Trim();
        switch (name)
        {
            case "UPSTREAM (client->server)": _down = false; break;
            case "DOWNSTREAM (server->client)": _down = true; break;
            case "Corrected/smoothed" when !_down && Pct.Match(value) is { Success: true } m:
                _r.UpLate = Num(m.Groups[3].Value);
                break;
            case "Missed" when Pct.Match(value) is { Success: true } m:
                if (_down) _r.DownMissedLate = Num(m.Groups[3].Value); else _r.UpMissedLate = Num(m.Groups[3].Value);
                break;
            case "Misdelivered lifetime" when Lost.Match(value) is { Success: true } m:
                _r.Dropped += Int(m.Groups[1].Value) ?? 0;
                break;
            case "Jitter (ms)":
                if (_down) _r.JitterDown = Int(P50.Match(value).Groups[1].Value); else _r.JitterUp = Int(P50.Match(value).Groups[1].Value);
                break;
            case "Network ping (ms)":
                _r.PingP50 = Int(P50.Match(value).Groups[1].Value);
                _r.PingP95 = Int(P95.Match(value).Groups[1].Value);
                break;
        }
        return null;
    }

    /// <summary>
    /// The summary read so far, if it has numbers, and starts over. For CS2 closing right after it wrote one (quit from a match):
    /// no next line comes, and a half-read summary must not run into the next session.
    /// </summary>
    public NetReport Flush()
    {
        var done = _r;
        _r = null;
        _down = false;
        _skipped = 0;
        return done != null && (done.PingP50 != null || done.UpLate != null) ? done : null;
    }

    static double? Num(string v) => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    static int? Int(string v) => int.TryParse(v.Replace(",", ""), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
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
