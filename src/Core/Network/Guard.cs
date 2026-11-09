namespace SRT.Core;

/// <summary>
/// Why the phone's connection isn't trusted right now. Unchecked: its public IP wasn't measured yet. SameIp: it is the home IP.
/// PhoneWifi: the phone's Wi-Fi is on (it may be sharing the home network). NoHome: the home IP wasn't measured, nothing to compare with.
/// </summary>
public enum Distrust { None, Unchecked, SameIp, PhoneWifi, NoHome }

public static class DistrustText
{
    /// <summary>The words for the Log, the toast and Status (null for None). The only place they are written.</summary>
    public static string Of(Distrust d) => d switch
    {
        Distrust.Unchecked => "The phone connection's public IP hasn't been checked yet.",
        Distrust.SameIp => "The public IP of the connection is the same as your home IP.",
        Distrust.PhoneWifi => "The phone's Wi-Fi is on, so it may be sharing your home network instead of mobile data.",
        Distrust.NoHome => "The home IP hasn't been measured yet.",
        _ => null,
    };
}

/// <summary>
/// The Log line each time the reason servers are blocked changes ("... Servers blocked."), said again when protection comes back on.
/// Same IP as home: when the IP check has just written it as an error, with the IP (<see cref="SameIpWritten"/>), the line here would
/// repeat it (diagnostics of 7 nov., two lines at 00:06:04); in every other way of getting there (protection turned on with the phone
/// still on the home Wi-Fi, a New IP that came back as the home IP) this is the only line, so it stays.
/// </summary>
public sealed class ProblemLog
{
    string _last;
    bool _sameIpWritten;

    /// <summary>The IP check wrote "The phone connection IP (...) is the same as your home IP. Servers and CS2 blocked." just now.</summary>
    public void SameIpWritten() => _sameIpWritten = true;

    /// <summary>The line to write for this sync, or null. <paramref name="unprotected"/>: protection off or No phone, nothing is blocked.</summary>
    public string Next(bool unprotected, string problem, Distrust distrust, DateTime? now = null)
    {
        if (unprotected) { _last = null; _sameIpWritten = false; _uncheckedAt = null; return null; }
        if (distrust != Distrust.SameIp) _sameIpWritten = false;
        if (problem == _last) return null;
        _last = problem;
        if (problem == null || distrust == Distrust.SameIp && _sameIpWritten) return null;
        // "hasn't been checked yet" comes back every time the connection drops and returns: an iPhone coming back after Airplane
        // Mode did it three times in 12 s (9 nov.). Once a minute is enough; any other problem is still said at once.
        if (distrust == Distrust.Unchecked)
        {
            var t = now ?? DateTime.Now;
            if (_uncheckedAt is DateTime at && t - at < UncheckedQuiet) return null;
            _uncheckedAt = t;
        }
        return problem + " Servers blocked.";
    }

    DateTime? _uncheckedAt;
    static readonly TimeSpan UncheckedQuiet = TimeSpan.FromMinutes(1);
}

public sealed class GuardReport
{
    public List<Adapter> Adapters { get; init; } = new();
    /// <summary>The connection trusted right now (null = everything blocked).</summary>
    public Adapter Egress { get; init; }
    /// <summary>The adapter that would be the protected connection before the IP check (so the IP keeps being measured through it).</summary>
    public Adapter Candidate { get; init; }
    /// <summary>Why there is no protected connection (e.g. the Wi-Fi jumped to the home router).</summary>
    public string EgressProblem { get; init; }
    /// <summary>The candidate is there and isn't trusted (<see cref="Distrust"/> says why).</summary>
    public bool Distrusted => Distrust != Distrust.None;
    public string FirewallProblem { get; init; }
    public bool KillSwitchOk { get; init; }
    public bool? Cs2LockOk { get; init; }
    public Dictionary<string, bool> RouteOk { get; init; } = new();
    /// <summary>"servers", "all", "home" (No phone mode) or "off" (protection turned off: Guard.OnAtStart).</summary>
    public string EffectiveMode { get; init; }
    /// <summary>Nothing is protected: protection off, or No phone mode. Only the Regions block stays.</summary>
    public bool Unprotected => EffectiveMode is "home" or "off";
    /// <summary>Whole PC: the routes, DNS and IPv6 rules are all confirmed.</summary>
    public bool FullModeOk { get; init; }
    /// <summary>Whole PC: Windows sends the rest of the PC's IPv4 through the protected connection.</summary>
    public bool FullRoutesOk { get; init; }
    /// <summary>Whole PC: DNS only leaves through the protected connection, and IPv6 is blocked (null = not in whole PC mode).</summary>
    public bool? DnsOk { get; init; }
    public bool? Ipv6Ok { get; init; }
    public string PcDefaultName { get; init; }
    public bool PcDefaultIsEgress { get; init; }
    public int PcDefaultIndex { get; init; }
    /// <summary>The phone's adapter (protected or still being checked), -1 when there is none.</summary>
    public int PhoneIndex { get; init; } = -1;
    public string Error { get; init; }
    /// <summary>Why the candidate isn't trusted (None = trusted or no candidate).</summary>
    public Distrust Distrust { get; init; }
    /// <summary>The same in words (null = trusted or no candidate).</summary>
    public string DistrustReason => DistrustText.Of(Distrust);
    /// <summary>"ifIndex|localIp" of the candidate: the app only trusts a connection whose public IP it measured.</summary>
    public string CandidateKey { get; init; }
    /// <summary>Routes to Valve's servers go through the protected connection (null = option off or no connection).</summary>
    public bool? SteamRoutesOk { get; init; }
    public SteamLink Steam { get; init; } = new();
    /// <summary>steam.exe talks to Valve through the protected connection (null = Steam closed or not connected).</summary>
    public bool? SteamOnEgress { get; init; }
    public bool AllRoutesOk => RouteOk.Count > 0 && RouteOk.Values.All(v => v);
    /// <summary>There is a home connection (another active adapter with a gateway, not the candidate and not a phone) whose IP can be compared.</summary>
    public bool HasHome { get; init; }

    // What the sync found that belongs in the settings. The Guard only reads them (on its own thread); the window saves these.
    /// <summary>cs2.exe found somewhere other than Settings.Cs2Path (null = the same, or not looked for).</summary>
    public string Cs2Found { get; init; }
    /// <summary>The rules of v6, v7 and v8 were removed in this sync (Settings.LegacyCleaned).</summary>
    public bool LegacyCleaned { get; init; }
}

/// <summary>
/// What a <see cref="Guard.Sync"/> is asked to do. Ips: the servers' IPs. Force: write the rules again even if nothing changed.
/// Distrust: why the window doesn't trust the phone's connection (the Guard adds Unchecked itself). VerifiedKey: the candidate whose
/// public IP was measured and isn't the home IP (any other stays blocked). On: protection is turned on.
/// </summary>
public sealed record SyncRequest(IReadOnlyCollection<string> Ips)
{
    public bool Force { get; init; }
    public Distrust Distrust { get; init; }
    public bool Cs2Running { get; init; }
    public string VerifiedKey { get; init; }
    public bool On { get; init; } = true;
}

/// <summary>
/// A route the app adds, as owned.json keeps it: "prefix|interface index|next hop" (the format files from older versions use too).
/// Built only by <see cref="Of(Net.Route)"/> (a route in the table) and <see cref="Of(string, Adapter)"/> (a route the app adds).
/// </summary>
public readonly record struct RouteKey(string Prefix, int Index, string Hop)
{
    public override string ToString() => $"{Prefix}|{Index}|{Hop}";

    public static RouteKey Of(Net.Route r) => new(r.Prefix, r.Index, r.NextHop);

    /// <summary>The route the app adds for <paramref name="prefix"/> through <paramref name="via"/> (no gateway: on-link, 0.0.0.0).</summary>
    public static RouteKey Of(string prefix, Adapter via) => new(prefix, via.Index, via.Gateway ?? "0.0.0.0");

    /// <summary>Three parts with a whole number (digits only) as the index. What the parts hold is checked by the caller.</summary>
    public static bool TryParse(string s, out RouteKey key)
    {
        key = default;
        var p = s?.Split('|');
        if (p == null || p.Length != 3 || !int.TryParse(p[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var idx)) return false;
        key = new RouteKey(p[0], idx, p[2]);
        return true;
    }
}

/// <summary>
/// Keeps protection up, in three layers:
///  1. Firewall: the servers (and cs2.exe itself) can only go out with the local IP of the protected connection.
///  2. The connection only counts if it is the right network (on Wi-Fi, the saved hotspot).
///  3. If the connection's public IP equals the home one, it is no longer trusted (the window signals this with <c>distrust</c>).
/// Everything runs off the window thread, one operation at a time.
/// </summary>
public sealed class Guard
{
    readonly SemaphoreSlim _gate = new(1, 1);
    string _fwSignature;
    int _metricAppliedTo = -1;
    DateTime _metricTriedAt;
    readonly HashSet<string> _ownedRoutes = new(); // "prefix|ifIndex|nexthop"
    readonly string _ownedFile;
    readonly HashSet<string> _warned = new();
    /// <summary>The Firewall errors of the last rewrite, so the same failure on every sync is logged once.</summary>
    string _fwErrors;
    /// <summary>The app is closing with the block kept, or Windows is signing out: from then on no sync writes anything.</summary>
    volatile bool _released;

    public static readonly string[] SplitPrefixes = { "0.0.0.0/1", "128.0.0.0/1" };

    /// <summary>What the app changed in Windows, saved to disk so a crash or a kill doesn't leave orphans behind.</summary>
    public sealed class OwnedState
    {
        public List<string> Routes { get; set; } = new();
        public int MetricIf { get; set; } = -1;
        /// <summary>When Windows booted: routes and the metric only live until a restart.</summary>
        public DateTime Boot { get; set; }
    }

    // In UTC: a clock change (summer time, a big NTP correction) must not look like a restart
    static DateTime BootTime => DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);

    public Guard() : this(Paths.File(AppIds.Files.Owned)) { }

    /// <summary>Runs PowerShell for the metric (Save mobile data). The tests pass one that fails, to see what the Guard does then.</summary>
    readonly Func<string, int, Task<ProcResult>> _powershell;

    /// <summary>Reads and deletes routes: netsh in the app. The tests pass a table in memory, to see what the Guard deletes and keeps.</summary>
    public sealed record RouteTable(Func<Task<List<Net.Route>>> List, Func<Net.Route, Task<ProcResult>> Delete)
    {
        public static readonly RouteTable Windows = new(Net.ListRoutes, Net.DeleteRoute);
    }
    readonly RouteTable _routes;

    /// <summary>
    /// The phone's connection of the last sync (the candidate: protected or still being checked), so the app's routes on a hotspot
    /// or a USB network are found too, not only those on a phone adapter. Null = none, or not synced yet (--restore).
    /// </summary>
    Adapter _exit;

    /// <summary>With owned.json somewhere else (the admin tests use an admin-only folder).</summary>
    public Guard(string ownedFile, Func<string, int, Task<ProcResult>> powershell = null, RouteTable routes = null)
    {
        _powershell = powershell ?? ((args, ms) => Proc.Run("powershell", args, ms));
        _routes = routes ?? RouteTable.Windows;
        _ownedFile = ownedFile;
        var st = LoadOwned(ownedFile);
        // From before a restart: Windows already dropped those routes, and the interface index may now be another adapter.
        // ToUniversalTime: files from before beta.7 kept the boot time in local time
        if (st == null || Math.Abs((st.Boot.ToUniversalTime() - BootTime).TotalMinutes) > 2) return;
        foreach (var r in st.Routes) _ownedRoutes.Add(r);
        _metricAppliedTo = st.MetricIf;
    }

    /// <summary>
    /// owned.json decides which routes the app deletes as admin and which adapter gets its automatic metric back. Read only when no
    /// program without admin rights could have written it (not a link, in a folder only admins change), and only what it holds that
    /// looks like the app's own: anything else is left out. Without it, the restore still finds the app's routes on the phone.
    /// </summary>
    static OwnedState LoadOwned(string file)
    {
        try
        {
            Paths.NoLink(file);
            if (Adb.Elevated && Adb.UsersCanWrite(Path.GetDirectoryName(Path.GetFullPath(file))!)) throw new UnauthorizedAccessException(Path.GetDirectoryName(file) + " can be changed by programs without admin rights");
        }
        catch (Exception ex) { Log.Warn("owned.json was not read: " + ex.Message); return null; }
        var st = Trusted(Store.Load(file, () => new OwnedState()), out var dropped);
        if (dropped > 0) Log.Warn($"owned.json: {Fmt.Count(dropped, "entry", "entries")} that the app never writes left out.");
        return st;
    }

    /// <summary>
    /// Only routes shaped like the app's: "prefix|interface|gateway" with a server /32, a whole-PC half or one of Valve's blocks,
    /// a real interface index and an IPv4 gateway. The metric adapter only as a real index.
    /// </summary>
    public static OwnedState Trusted(OwnedState st, out int dropped)
    {
        var routes = (st?.Routes ?? new()).Where(r => r != null).Distinct().ToList();
        var ok = routes.Where(OwnedRouteShape).ToList();
        dropped = routes.Count - ok.Count + (st?.MetricIf < -1 ? 1 : 0);
        return new OwnedState { Routes = ok, MetricIf = st?.MetricIf > 0 ? st.MetricIf : -1, Boot = st?.Boot ?? default };
    }

    static bool OwnedRouteShape(string key)
    {
        if (!RouteKey.TryParse(key, out var k) || k.Index <= 0) return false;
        if (!System.Net.IPAddress.TryParse(k.Hop, out var gw) || gw.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || k.Hop != gw.ToString()) return false;
        if (SplitPrefixes.Contains(k.Prefix) || Net.ValvePrefixes.Contains(k.Prefix)) return true;
        var host = k.Prefix.EndsWith("/32") ? k.Prefix[..^3] : null;
        return host != null && System.Net.IPAddress.TryParse(host, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && host == a.ToString()
            && !System.Net.IPAddress.Any.Equals(a);
    }

    void SaveOwned()
    {
        try { Store.Save(_ownedFile, new OwnedState { Routes = _ownedRoutes.ToList(), MetricIf = _metricAppliedTo, Boot = BootTime }); }
        catch (Exception ex) { Log.Warn("Could not save owned.json: " + ex.Message); }
    }
    void Own(RouteKey key) { if (_ownedRoutes.Add(key.ToString())) SaveOwned(); }
    bool Disown(RouteKey key) { if (!_ownedRoutes.Remove(key.ToString())) return false; SaveOwned(); return true; }
    IEnumerable<RouteKey> OwnedKeys() => _ownedRoutes.Select(r => RouteKey.TryParse(r, out var k) ? k : (RouteKey?)null).OfType<RouteKey>();

    /// <summary>
    /// The one rule for "this route is the app's": it is in owned.json, or it sits on the phone's side and has the shape of the
    /// app's routes (a server /32 of <paramref name="serverIps"/>, a whole-PC half or one of Valve's blocks). The phone's side is
    /// a phone adapter, or the phone's connection the Guard knows (<paramref name="exitIndex"/>: a hotspot or a USB network).
    /// <paramref name="anyHostOnPhone"/>: every /32 on a phone adapter counts (the list may have changed); never on the
    /// connection, which can also carry other programs' routes (a VPN's server).
    /// </summary>
    public static bool IsOurs(Net.Route rt, IReadOnlySet<string> owned, IEnumerable<Adapter> adapters, int exitIndex, IReadOnlySet<string> serverIps, bool anyHostOnPhone = false)
    {
        if (owned.Contains(RouteKey.Of(rt).ToString())) return true;
        var onPhone = adapters.Any(a => a.Index == rt.Index && a.IsPhone);
        if (!onPhone && (exitIndex <= 0 || rt.Index != exitIndex)) return false;
        if (SplitPrefixes.Contains(rt.Prefix) || Net.ValvePrefixes.Contains(rt.Prefix)) return true;
        return rt.Prefix.EndsWith("/32") && (onPhone && anyHostOnPhone && ViaGateway(rt) || serverIps.Contains(rt.Prefix[..^3]));
    }

    /// <summary>
    /// The route goes through a gateway (an IPv4 next hop that isn't 0.0.0.0). Windows' own /32 routes on the phone's adapter (its
    /// local IP, the broadcast, 255.255.255.255) are on-link, and the app's routes on a phone always have the gateway (PickEgress
    /// wants one), so "any /32 on the phone" never takes Windows' own.
    /// </summary>
    static bool ViaGateway(Net.Route rt) =>
        System.Net.IPAddress.TryParse(rt.NextHop, out var hop) && hop.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !hop.Equals(System.Net.IPAddress.Any);

    /// <summary>
    /// The index of the phone's connection for <see cref="IsOurs"/>: the last sync's, only while the same adapter still has the
    /// same local IP (a Wi-Fi that moved to the home router is not it any more), and never a VPN's adapter. -1 = none.
    /// </summary>
    public static int ExitIndex(Adapter exit, IEnumerable<Adapter> adapters) =>
        exit != null && !exit.IsVpn && adapters.Any(a => a.Index == exit.Index && a.LocalIp == exit.LocalIp && !a.IsVpn) ? exit.Index : -1;

    /// <summary>
    /// The one way the app deletes routes: each one <paramref name="pick"/> keeps that is the app's (<see cref="IsOurs"/>). A route
    /// Windows doesn't delete stays the app's (owned.json too), so the next try, the check after a restore and --restore still see
    /// it; the failure goes to the Log once per route, not on every sync. Returns how many were deleted.
    /// </summary>
    async Task<int> DeleteOurs(List<Net.Route> routes, List<Adapter> adapters, Func<Net.Route, bool> pick, IReadOnlySet<string> serverIps, bool anyHostOnPhone = false)
    {
        if (routes == null) return 0;   // the table couldn't be read: nothing is deleted or forgotten
        var exit = ExitIndex(_exit, adapters);
        var deleted = 0;
        foreach (var rt in routes)
        {
            if (!pick(rt) || !IsOurs(rt, _ownedRoutes, adapters, exit, serverIps, anyHostOnPhone)) continue;
            var key = RouteKey.Of(rt);
            var r = await _routes.Delete(rt);
            if (!r.Ok && RouteAlreadyGone(r.Output))
            {
                // The route went with its adapter (9 nov.: the iPhone in Airplane Mode left three "Couldn't delete the route ...:
                // Element not found" in the Log). Nothing is left to delete, so it is no longer the app's and nothing is said.
                _warned.Remove("route " + key);
                Disown(key);
                continue;
            }
            if (!r.Ok)
            {
                if (_warned.Add("route " + key)) Log.Warn($"Couldn't delete the route {rt.Prefix} on adapter {rt.Index}: {(r.Output ?? "").Trim()}");
                continue;
            }
            _warned.Remove("route " + key);
            Disown(key);
            deleted++;
        }
        return deleted;
    }

    static readonly IReadOnlySet<string> NoServers = new HashSet<string>();

    public static string KeyOf(Adapter a) => a == null ? null : $"{a.Index}|{a.LocalIp}";

    /// <summary>Applies protection for <paramref name="r"/>. <paramref name="s"/> is only read: what belongs in it comes back in the report.</summary>
    public Task<GuardReport> Sync(Settings s, SyncRequest r) =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync();
            // Closing (Keep) or Windows signing out: a sync that was already on its way doesn't put back what the close took off
            if (_released) { _gate.Release(); return new GuardReport { Error = "The app is closing." }; }
            try { return await SyncCore(s, r.Ips.Distinct().ToList(), r.Force, r.Distrust, r.Cs2Running, r.VerifiedKey, r.On); }
            catch (Exception ex)
            {
                Log.Error("Sync: " + ex.Message);
                return new GuardReport { Error = ex.Message };
            }
            finally { _gate.Release(); }
        });

    /// <summary>
    /// cs2.exe's game traffic (UDP: servers, invites, Valve's relays) only through the protected connection.
    /// TCP stays free: CS2's web requests and what other programs load into the game (over HTTPS) would fail in Game only,
    /// where only the listed servers and Steam go through the phone. Listed servers stay blocked on any protocol by the kill switch.
    /// </summary>
    /// <summary>
    /// What protection does right now: "home" (no phone: nothing goes through the phone, nothing is blocked), "all" (whole PC,
    /// always or while CS2 is open) or "servers" (game only). Old settings without "home" keep what they had.
    /// </summary>
    public static string ModeFor(string mode, bool gameMode, bool cs2Running) =>
        mode == "home" ? "home" : mode == "all" || (gameMode && cs2Running) ? "all" : "servers";

    // ------------------------------------------------------------ On or off (beta.10: one rule instead of the beta.9 wait for the phone)
    //  1. Protection is on only when you turn it on: Turn on protection (Status, the tray icon), or Settings > Turn on protection when
    //     the app starts. Off, the app puts nothing in the Firewall or the routes except the Regions block, and every page works.
    //  2. Once on, it stays on until you turn it off: a phone drop blocks your servers and CS2, it never turns protection off.
    //  3. Each start is off, except: that setting; an update made while it was on; and CS2 still open with the block of the last run
    //     in place (the app closed with Keep, or crashed): the match isn't moved to the home IP behind your back.
    //  4. Closing with it on: the question asked once (keep the block or turn it off), the answer in Settings.

    /// <summary>Rule 3. <paramref name="leftOn"/>: the app's kill switch rule is still in the Firewall from the last run.</summary>
    public static bool OnAtStart(bool justUpdated, IReadOnlyCollection<string> args, bool onAtStart, bool leftOn, bool cs2Running, bool noPhone)
    {
        if (noPhone) return false;
        // From beta.9: nothing = it was protected, --standby = it was still waiting for the phone (never turned on), --off = off
        if (justUpdated) return args.Contains(AppIds.Args.On) || !(args.Contains(AppIds.Args.Off) || args.Contains(AppIds.Args.Standby));
        return onAtStart || (leftOn && cs2Running);
    }

    /// <summary>
    /// Windows sends the rest of the PC through the phone while it shouldn't (game only or no phone): the phone's adapter gets a
    /// high metric so the home network wins. Only with another connection to fall back on, and only with the option on.
    /// </summary>
    public static bool NeedsPcOffPhone(string effectiveMode, bool keepPcOffPhone, int pcDefaultIndex, int phoneIndex, bool hasOtherInternet) =>
        keepPcOffPhone && effectiveMode is "servers" or "home" or "off" && phoneIndex >= 0 && pcDefaultIndex == phoneIndex && hasOtherInternet;

    public enum PcOffPhoneStep { None, Apply, Undo }

    /// <summary>
    /// What Save mobile data does to the phone's adapter on this sync. Undo with the option off, in Whole PC (the phone is the PC's
    /// way out) and without another connection (the PC is never left with no default route). Nothing while CS2 has the whole PC in
    /// game mode (0/1 + 128/1 carry it). Apply when it isn't applied to this adapter yet, or, once a minute at most, when it is but
    /// Windows still picks the phone or the last try failed.
    /// </summary>
    public static PcOffPhoneStep PcOffPhoneNext(bool keepPcOffPhone, string userMode, string effectiveMode, Adapter phone, bool hasOtherInternet,
        int appliedTo, bool pcOnPhone, double secondsSinceTry)
    {
        if (!keepPcOffPhone || userMode == "all" || phone == null || !(phone.IsPhone || phone.IsWifi) || !hasOtherInternet)
            return appliedTo >= 0 ? PcOffPhoneStep.Undo : PcOffPhoneStep.None;
        if (effectiveMode == "all") return PcOffPhoneStep.None;
        if (appliedTo == phone.Index && !pcOnPhone) return PcOffPhoneStep.None;
        if (secondsSinceTry < 60 && (appliedTo == phone.Index || appliedTo < 0)) return PcOffPhoneStep.None;
        return PcOffPhoneStep.Apply;
    }

    static Firewall.FwRule Cs2Rule(string cs2, string allow) => new(Firewall.Cs2RuleName, null, cs2, allow,
        "Managed by CS2 IP Changer. CS2's game traffic (UDP) can only go out through the protected connection: servers not in the list never see your home IP.",
        Firewall.NET_FW_IP_PROTOCOL_UDP);

    /// <summary>
    /// The app's own IP test (<see cref="Net.EchoIp"/>, the app's exe only) can only leave through <paramref name="allow"/>: the
    /// candidate, so the IP measured is the phone's. Other programs (1.0.0.1 is also a public DNS server) are left alone.
    /// </summary>
    static Firewall.FwRule EchoRule(string allow) => new(Firewall.EchoRuleName, new[] { Net.EchoIp }, Environment.ProcessPath, allow,
        "Managed by CS2 IP Changer. Makes sure the app's IP test measures the protected connection.");

    /// <summary>Writes one Firewall rule; a failure is kept for the Log line instead of stopping the other rules.</summary>
    static void TryRule(List<string> errors, Action write)
    {
        try { write(); }
        catch (Exception ex) { errors.Add((ex.InnerException ?? ex).Message); }
    }

    /// <summary>The Log lines for why servers are blocked (<see cref="ProblemLog"/>).</summary>
    public ProblemLog Problems { get; } = new();

    string _killLine;

    /// <summary>
    /// The kill switch line after the rules are written, or null when it would say again what the last one said: each forced sync
    /// (every change of trust) rewrote the rules and wrote the same line, three times in 3 s at 00:06:01-00:06:04 in the diagnostics
    /// of 7 nov. Rules that were found missing or wrong (<paramref name="repaired"/>) are always said.
    /// </summary>
    /// <summary>Windows answered that the route isn't there (English or Portuguese Windows, or the error code): it already went.</summary>
    public static bool RouteAlreadyGone(string output) =>
        output != null && (output.Contains("Element not found", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Elemento não encontrado", StringComparison.OrdinalIgnoreCase)
            || output.Contains("0x80070490", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The kill switch line, once. While everything stays blocked, the reason going back and forth ("no connection", "the phone isn't
    /// trusted yet") as an iPhone comes back is no news (9 nov.: six lines in 16 s): one blocked line until it opens again.
    /// </summary>
    public static string KillLine(string text, ref string last, bool repaired)
    {
        if (text == last && !repaired) return null;
        if (!repaired && IsBlockedLine(text) && IsBlockedLine(last)) return null;
        last = text;
        return text;
    }

    static bool IsBlockedLine(string line) => line != null && line.Contains(" blocked (", StringComparison.Ordinal);

    async Task<GuardReport> SyncCore(Settings s, List<string> ips, bool force, Distrust distrust, bool cs2Running, string verifiedKey, bool on)
    {
        var adapters = Net.Scan();
        var candidate = Net.PickEgress(adapters, s.EgressAdapterId, s.EgressSsid, out var problem);
        _exit = candidate;
        // A new connection (another adapter or local IP) is only trusted after its public IP is measured.
        if (distrust == Distrust.None && candidate != null && KeyOf(candidate) != verifiedKey) distrust = Distrust.Unchecked;
        var egress = distrust != Distrust.None ? null : candidate;
        // Why there is no protected connection, in words: the distrust's, when there is a candidate
        if (distrust != Distrust.None && candidate != null) problem = DistrustText.Of(distrust);
        var mode = PhoneLink.EffectiveMode(on, s.Mode, s.GameMode, cs2Running);
        // Off and No phone block nothing, so "Servers blocked" would be false there; back on protection the problem is said again.
        if (Problems.Next(mode is "home" or "off", problem, distrust) is string blocked) Log.Warn(blocked);
        if (mode is "home" or "off") _killLine = null;   // back on protection the kill switch line is said again
        var fwProblem = Firewall.CheckEnabled();
        if (mode is "home" or "off") return await HomeMode(s, ips, adapters, candidate, fwProblem, force, mode, distrust);
        var cs2 = s.LockCs2 && (s.PhoneSeen || egress != null) ? Steam.FindCs2(s.Cs2Path) : null;
        var legacyCleaned = false;

        // 1) Firewall first: there is never a moment when a server is reachable with the home IP.
        var killOk = false;
        bool? cs2Ok = null, dnsOk = null, ipv6Ok = null;
        // Whole PC moves only IPv4 with the routes: DNS to the home router (a directly connected network) and IPv6 would
        // still leave from home. So DNS may only leave from the protected connection, and IPv6 is blocked everywhere.
        var full = mode == "all" && egress?.Gateway != null ? FullModeRules(egress.LocalIp) : null;
        if (fwProblem == null)
        {
            var allow = egress?.LocalIp;
            var sig = string.Join(",", ips.OrderBy(i => i)) + "|" + allow + "|" + candidate?.LocalIp + "|" + cs2 + "|" + (full != null);
            var echoOk = false;
            // Every rule read back once per sync (and again only after they are rewritten).
            void ReadBack()
            {
                killOk = Firewall.Verify(Firewall.RuleName, ips, allow);
                echoOk = Firewall.Verify(EchoRule(candidate?.LocalIp));
                cs2Ok = cs2 != null ? Firewall.Verify(Cs2Rule(cs2, allow)) : null;
                dnsOk = full != null ? Firewall.Verify(full[0]) && Firewall.Verify(full[1]) : null;
                ipv6Ok = full != null ? Firewall.Verify(full[2]) : null;
            }
            ReadBack();
            var repaired = !killOk || !echoOk || cs2Ok == false || dnsOk == false || ipv6Ok == false;
            if (force || sig != _fwSignature || repaired)
            {
                // Each rule on its own: one failing must not leave the others on an old allowed IP.
                var errors = new List<string>();
                void Try(Action a) => TryRule(errors, a);
                Try(() => Firewall.Apply(Firewall.RuleName, ips, null, allow, "Managed by CS2 IP Changer. Servers in the list can only be reached from the local IP of the protected connection."));
                Try(() => Firewall.Apply(EchoRule(candidate?.LocalIp)));
                if (cs2 != null) Try(() => Firewall.Apply(Cs2Rule(cs2, allow)));
                else Firewall.Remove(Firewall.Cs2RuleName);
                if (full != null) foreach (var r in full) Try(() => Firewall.Apply(r));
                else foreach (var n in FullModeRuleNames) Firewall.Remove(n);
                _fwSignature = sig;
                // Without admin rights every sync (5 s) fails the same way: one line until it changes, and no "blocked" that isn't true.
                if (FirewallErrorLine(errors, ref _fwErrors) is string line) { if (errors.Count > 0) Log.Error(line); else Log.Ok(line); }
                if (errors.Count == 0 && KillLine(egress == null
                        ? "Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + (candidate != null ? " blocked (the phone isn't trusted yet)." : " blocked (no connection).")
                        : $"Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + $" only go out through \"{egress.Name}\" ({egress.LocalIp}).", ref _killLine, repaired) is string kill)
                    Log.Info(kill);
                ReadBack();
            }
            if (dnsOk == false && _warned.Add("dns")) Log.Warn("Whole PC: the DNS rule was not confirmed, so name lookups may still go to the home router.");
            if (ipv6Ok == false && _warned.Add("ipv6")) Log.Warn("Whole PC: the IPv6 block was not confirmed, so sites over IPv6 may still see your home connection.");
            if (dnsOk == true) _warned.Remove("dns");
            if (ipv6Ok == true) _warned.Remove("ipv6");

            if (!s.LegacyCleaned)
            {
                var n = Firewall.RemoveLegacy();
                if (n > 0) Log.Ok($"Removed {n} old rules from v6/v7/v8.");
                legacyCleaned = true;
            }
        }

        // 2) /32 routes: the IP test always goes through the candidate; servers only through the trusted connection.
        if (candidate != null) await EnsureRoutes(new List<string> { Net.EchoIp }, candidate, adapters);
        var routeOk = new Dictionary<string, bool>();
        // Also before the phone is trusted: with the rest of the PC on the home network, the home IP can be read and compared.
        await KeepPcOffPhone(s, mode, egress ?? candidate, adapters);
        if (egress != null)
        {
            await EnsureRoutes(ips, egress, adapters);
            foreach (var ip in ips)
            {
                routeOk[ip] = Net.BestInterface(ip) == egress.Index;
                // Only while the connection is still there: in the second it goes (Airplane Mode) Windows picks another adapter and
                // there is no VPN (9 nov., the iPhone)
                if (!routeOk[ip] && StillUp(egress) && _warned.Add(ip)) Log.Warn($"Windows does not pick the protected connection for {ip}. Another VPN may be overriding it.");
                if (routeOk[ip]) _warned.Remove(ip);
            }
        }
        else foreach (var ip in ips) routeOk[ip] = false;

        // 2b) Steam on the same connection: Valve's blocks go through the phone before CS2 opens, and game mode
        // no longer changes Steam's path mid-game (the cause of the "No Steam logon" kick).
        bool? steamRoutesOk = null;
        if (s.SteamOnEgress && egress != null)
        {
            await EnsurePrefixRoutes(Net.ValvePrefixes, egress, adapters);
            steamRoutesOk = Net.ValvePrefixes.All(p => Net.BestInterface(Net.ProbeIp(p)) == egress.Index);
        }
        else await RemovePrefixRoutes(Net.ValvePrefixes, adapters);

        // 3) Whole PC (or game mode): 0/1 + 128/1 beat the default route without deleting it (as VPNs do).
        var fullOk = false;
        if (mode == "all" && egress?.Gateway != null)
        {
            if (Net.BestInterface(Net.PcProbeIp) != egress.Index || Net.BestInterface(Net.PcProbeIpHigh) != egress.Index)
            {
                foreach (var pfx in SplitPrefixes)
                {
                    var r = await Net.AddRoute(pfx, egress, 5);
                    if (r.Ok) Own(RouteKey.Of(pfx, egress));
                }
                Log.Info(s.Mode == "all" ? "The whole PC goes through the phone." : "CS2 is open: the whole PC goes through the phone.");
            }
            fullOk = Net.BestInterface(Net.PcProbeIp) == egress.Index;
        }
        else await RemoveSplitRoutes(adapters);

        var pcIdx = Net.BestInterface(Net.PcProbeIp);
        var pc = adapters.FirstOrDefault(a => a.Index == pcIdx);
        var steam = Net.ReadSteamLink();
        bool? steamOnEgress = !steam.Connected || egress == null ? null : steam.LocalIps.All(ip => ip == egress.LocalIp);
        return new GuardReport
        {
            Adapters = adapters,
            Egress = egress,
            Candidate = candidate,
            EgressProblem = problem,
            Distrust = candidate != null ? distrust : Distrust.None,
            CandidateKey = KeyOf(candidate),
            FirewallProblem = fwProblem,
            KillSwitchOk = killOk,
            Cs2LockOk = cs2Ok,
            RouteOk = routeOk,
            EffectiveMode = mode,
            FullModeOk = fullOk && dnsOk == true && ipv6Ok == true,
            FullRoutesOk = fullOk,
            DnsOk = dnsOk,
            Ipv6Ok = ipv6Ok,
            PcDefaultName = pc?.Name ?? "—",
            PcDefaultIsEgress = egress != null && pcIdx == egress.Index,
            PcDefaultIndex = pcIdx,
            PhoneIndex = (egress ?? candidate)?.Index ?? -1,
            SteamRoutesOk = steamRoutesOk,
            Steam = steam,
            SteamOnEgress = steamOnEgress,
            HasHome = PhoneLink.HasHome(adapters, candidate),
            Cs2Found = cs2 != s.Cs2Path ? cs2 : null,
            LegacyCleaned = legacyCleaned,
        };
    }

    /// <summary>
    /// The phone's adapter ignores its default route (and gets metric 900), so the rest of the PC stays on the home network. The
    /// metric alone was not enough: on a real PC Windows kept the phone for the rest of the PC whatever its metric. Every mode reaches
    /// the phone through explicit routes to its gateway, so none needs that default route. Applied again whenever Windows still picks
    /// the phone (a cable that comes back can keep its interface index but loses the setting); undone as soon as it is not wanted.
    /// </summary>
    async Task KeepPcOffPhone(Settings s, string mode, Adapter phone, List<Adapter> adapters, string userMode = null)
    {
        var other = phone != null && PhoneLink.HasHome(adapters, phone);
        var onPhone = phone != null && Net.BestInterface(Net.PcProbeIp) == phone.Index;
        var step = PcOffPhoneNext(s.KeepPcOffPhone, userMode ?? s.Mode, mode, phone, other, _metricAppliedTo, onPhone, (DateTime.Now - _metricTriedAt).TotalSeconds);
        if (step == PcOffPhoneStep.Undo && MetricGone(_metricAppliedTo, adapters))
        {
            // The cable came out (or tethering went off): Windows removed the adapter and its setting with it, so there is nothing
            // to give back, and PowerShell on an index that is gone would fail on every sync
            Log.Info("The phone's connection went away (cable, Personal Hotspot or Airplane Mode). Save mobile data is set again when it comes back.");
            _metricAppliedTo = -1;
            SaveOwned();
            return;
        }
        if (step == PcOffPhoneStep.Undo)
        {
            await RestoreMetric();
            if (s.KeepPcOffPhone && phone != null && !other) Log.Info("No home connection: the rest of the PC goes through the phone again.");
            return;
        }
        if (step != PcOffPhoneStep.Apply) return;
        if (_metricAppliedTo >= 0 && _metricAppliedTo != phone.Index)
        {
            if (MetricGone(_metricAppliedTo, adapters)) { _metricAppliedTo = -1; SaveOwned(); }
            else await RestoreMetric();
        }
        _metricTriedAt = DateTime.Now;
        var r = await _powershell(PcOffPhoneArgs(phone.Index, true), 20000);
        if (!r.Ok) { if (_warned.Add("metric")) Log.Warn("Couldn't keep the rest of the PC off the phone: " + PsError(r.Output)); return; }
        _warned.Remove("metric");
        _metricAppliedTo = phone.Index;
        SaveOwned();
        Log.Info(mode == "home" ? "No phone mode: the PC keeps using the home network, not the phone."
            : mode == "off" ? "Protection off: the PC keeps using the home network, not the phone."
            : "The PC keeps using the home network; only the game goes through the phone.");
    }

    /// <summary>
    /// PowerShell's error as one Log line: the first line without the cmdlet name ("Access is denied."), not the five lines of
    /// position and CategoryInfo that follow it.
    /// </summary>
    public static string PsError(string output)
    {
        var line = (output ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var i = line.IndexOf(" : ", StringComparison.Ordinal);
        return i > 0 && line[..i].Contains("-Net", StringComparison.Ordinal) ? line[(i + 3)..] : line;
    }

    /// <summary>Save mobile data is applied to an adapter right now.</summary>
    public bool PcOffPhoneApplied => _metricAppliedTo >= 0;

    /// <summary>The routes the app added ("prefix|interface|next hop") and the adapter whose metric it changed (-1 = none), for Log > What the app changed.</summary>
    public (List<string> Routes, int MetricIf) Owned() => (_ownedRoutes.ToList(), _metricAppliedTo);

    /// <summary>
    /// Closing without Clean up on exit: protection stays, but the phone's adapter gets its default route back. Otherwise, once
    /// the home network is gone, the PC has no internet through the phone until a restart.
    /// </summary>
    public Task ReleasePcOffPhone() => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try { await RestoreMetric(); }
        finally { _gate.Release(); }
    });

    /// <summary>
    /// Closing with Keep (also "Keep protection and close" with CS2 open, and Quit after an update with CS2 open): the kill switch and
    /// the CS2 lock stay, as the question says. What would leave the PC without internet once the phone is gone goes: the Whole PC
    /// rules (DNS only from the phone's local IP and IPv6 blocked, which survive a restart), the phone's default route (Save mobile
    /// data) and, with CS2 closed, the 0/1 and 128/1 routes (a match still open needs them; they go with the phone's adapter or a
    /// restart). No sync writes anything after this one.
    /// </summary>
    public Task KeepOnClose(bool cs2Running) => Task.Run(async () =>
    {
        _released = true;
        await _gate.WaitAsync();
        try
        {
            var rules = Firewall.Remaining();
            foreach (var n in Firewall.WholePcRules) if (rules.Error != null || rules.Contains(n)) Firewall.Remove(n);
            if (!cs2Running) await RemoveSplitRoutes(Net.Scan());
            await RestoreMetric();
        }
        finally { _gate.Release(); }
    });

    /// <summary>
    /// Windows is signing out or shutting down and closes the app without waiting: only the Firewall rules <paramref name="names"/>
    /// (COM in the app, fast, and they are what survives a restart), within <paramref name="limit"/>. Routes and the metric go with
    /// a restart. No sync writes anything after this. True when it finished in time.
    /// </summary>
    public bool RemoveRulesNow(IReadOnlyCollection<string> names, TimeSpan limit)
    {
        _released = true;
        var work = Task.Run(async () =>
        {
            var got = await _gate.WaitAsync(limit / 2);
            try { foreach (var n in names) Firewall.Remove(n); }
            finally { if (got) _gate.Release(); }
        });
        try { return work.Wait(limit); } catch { return false; }
    }

    /// <summary>
    /// Gives the phone's adapter its default route and automatic metric back. When PowerShell fails the adapter stays marked as
    /// changed (in owned.json too), so the next start or --restore still gives it back, and the Log says so.
    /// </summary>
    async Task RestoreMetric()
    {
        if (_metricAppliedTo < 0) return;
        var r = await _powershell(PcOffPhoneArgs(_metricAppliedTo, false), 20000);
        if (!r.Ok)
        {
            // Once per adapter: the next start, Turn off and --restore still try, but the Log doesn't say it again each time
            if (_warned.Add("metric-back " + _metricAppliedTo))
                Log.Warn($"Couldn't give the phone's adapter its default route back: {PsError(r.Output)} Without the home network the PC may have no internet through the phone until --restore or a restart.");
            return;
        }
        _warned.Remove("metric-back " + _metricAppliedTo);
        _metricAppliedTo = -1;
        SaveOwned();
    }

    /// <summary>Save mobile data is marked on an adapter Windows no longer lists (the phone was unplugged): nothing is left to give back.</summary>
    public static bool MetricGone(int appliedTo, IEnumerable<Adapter> adapters) => appliedTo >= 0 && !adapters.Any(a => a.Index == appliedTo);

    /// <summary>
    /// No phone mode: the kill switch, the IP test rule, the CS2 lock, whole PC and every route of the app are taken off, so CS2 and
    /// the servers use the home connection. The Regions rule is not the guard's and stays. Done again only when something changed.
    /// </summary>
    /// <summary>
    /// No phone mode ("home") and protection off ("off"): the kill switch, the CS2 lock and the routes go, the Regions block stays.
    /// Off still measures the phone's IP (the app's own test route and rule through it), so Status shows it, Change IP works and the
    /// phone is already checked when you turn protection on. Save mobile data keeps the rest of the PC on the home network in both,
    /// whatever mode is picked: so the home IP gets measured on a first start in Whole PC always too (review finding 7).
    /// </summary>
    async Task<GuardReport> HomeMode(Settings s, List<string> ips, List<Adapter> adapters, Adapter phone, string fwProblem, bool force, string mode, Distrust distrust)
    {
        var off = mode == "off";
        // With Windows Firewall off the old rules can't be removed: the signature changes when it is back on, and they go then
        var sig = (off ? "off|" + phone?.LocalIp : "home") + (fwProblem != null ? "|fw-off" : "");
        if (HomeRewriteDue(force, _fwSignature != sig, DateTime.Now, _homeRetryAt))
        {
            var was = _fwSignature;
            if (fwProblem == null) Firewall.RemoveProtection();
            await DeleteOurs(await _routes.List(), adapters, _ => true, ips.Append(Net.EchoIp).ToHashSet(), anyHostOnPhone: true);
            var errors = new List<string>();
            // A rule Windows didn't remove (the Firewall busy for a moment) is tried again, once a minute, and said once
            if (fwProblem == null && Firewall.BlockLeft(Firewall.Remaining(), unreadableCounts: false))
            {
                errors.Add("a protection rule is still there");
                _homeRetryAt = DateTime.Now.AddSeconds(60);
                if (_warned.Add("home-left")) Log.Warn("A rule of the app's protection couldn't be removed, so servers or CS2 may still be blocked at home. The app tries again every minute; Log > What the app changed shows it.");
            }
            else { _homeRetryAt = default; _warned.Remove("home-left"); }
            if (off && phone != null && fwProblem == null)
            {
                TryRule(errors, () => Firewall.Apply(EchoRule(phone.LocalIp)));
                if (FirewallErrorLine(errors, ref _fwErrors) is string line) { if (errors.Count > 0) Log.Error(line); else Log.Ok(line); }
            }
            // A rule that wasn't written is tried again on the next sync (the Log line above is only said once)
            if (errors.Count == 0)
            {
                if (off && was?.StartsWith("off|") != true) Log.Info("Protection is off: no kill switch, no CS2 lock and no routes. CS2 and servers see your home IP.");
                if (!off && was?.StartsWith("home") != true) Log.Info("No phone mode: the kill switch and the routes are off. CS2 and servers see your home IP.");
                _fwSignature = sig;
            }
        }
        if (off && phone != null) await EnsureRoutes(new List<string> { Net.EchoIp }, phone, adapters);
        await KeepPcOffPhone(s, mode, phone, adapters, userMode: "servers");
        var pcIdx = Net.BestInterface(Net.PcProbeIp);
        return new GuardReport
        {
            Adapters = adapters,
            Candidate = off ? phone : null,
            CandidateKey = off ? KeyOf(phone) : null,
            Distrust = off && phone != null ? distrust : Distrust.None,
            FirewallProblem = fwProblem,
            EffectiveMode = mode,
            PcDefaultName = adapters.FirstOrDefault(a => a.Index == pcIdx)?.Name ?? "—",
            PcDefaultIndex = pcIdx,
            PhoneIndex = phone?.Index ?? -1,
            Steam = Net.ReadSteamLink(),
            HasHome = PhoneLink.HasHome(adapters, phone),
        };
    }

    static readonly string[] FullModeRuleNames = Firewall.WholePcRules;

    /// <summary>When protection off (or No phone) may rewrite after a rule was left behind (default: no wait).</summary>
    DateTime _homeRetryAt;

    /// <summary>
    /// Protection off and No phone write again when asked (<paramref name="force"/>) or when the state changed; after a protection
    /// rule was left behind, not before <paramref name="retryAt"/> (once a minute, not every 5 s with 6 Firewall calls each).
    /// </summary>
    public static bool HomeRewriteDue(bool force, bool changed, DateTime now, DateTime retryAt) => force || changed && now >= retryAt;

    /// <summary>DNS (UDP and TCP 53) only from the protected connection's local IP; all outbound IPv6 blocked.</summary>
    static Firewall.FwRule[] FullModeRules(string allowLocalIp) => new[]
    {
        new Firewall.FwRule(Firewall.DnsUdpRuleName, null, null, allowLocalIp, "Managed by CS2 IP Changer. Whole PC mode: DNS only goes out through the protected connection.",
            Firewall.NET_FW_IP_PROTOCOL_UDP, "53"),
        new Firewall.FwRule(Firewall.DnsTcpRuleName, null, null, allowLocalIp, "Managed by CS2 IP Changer. Whole PC mode: DNS only goes out through the protected connection.",
            Firewall.NET_FW_IP_PROTOCOL_TCP, "53"),
        new Firewall.FwRule(Firewall.Ipv6RuleName, null, null, null, "Managed by CS2 IP Changer. Whole PC mode: the routes only move IPv4, so IPv6 is blocked (Windows falls back to IPv4).",
            LocalAddresses: Firewall.AllIpv6),
    };

    /// <summary>
    /// The Log line for the errors of one Firewall rewrite, or null when it would repeat the last one. The same error on several
    /// rules is one line ("Access is denied (3 rules)"); the first rewrite that works after errors says so once.
    /// </summary>
    public static string FirewallErrorLine(IReadOnlyList<string> errors, ref string last)
    {
        var now = errors.Count == 0 ? null : string.Join("; ", errors.Select(e => (e ?? "").Trim().TrimEnd('.')).GroupBy(e => e)
            .Select(g => g.Count() > 1 ? $"{g.Key} ({g.Count()} rules)" : g.Key));
        if (now == last) return null;
        var had = last != null;
        last = now;
        return now != null ? $"Firewall: {now}. The rules were not written." : had ? "Firewall: rules written again." : null;
    }

    /// <summary>Fast path: the Windows API says whether each IP already leaves through the adapter. Routes are only touched if one is missing.</summary>
    Task EnsureRoutes(List<string> ips, Adapter via, List<Adapter> adapters) =>
        EnsureVia(ips.Select(ip => ip + "/32").ToList(), p => p[..^3], via, adapters);

    /// <summary>Like EnsureRoutes, but for whole blocks (e.g. Valve's).</summary>
    async Task EnsurePrefixRoutes(string[] prefixes, Adapter via, List<Adapter> adapters)
    {
        var added = await EnsureVia(prefixes, Net.ProbeIp, via, adapters);
        if (added > 0) Log.Info($"Steam through the phone: {Fmt.Count(added, "Valve block")} {(added == 1 ? "goes" : "go")} through \"{via.Name}\".");
    }

    /// <summary>
    /// EnsureRoutes and EnsurePrefixRoutes: the prefixes Windows doesn't send through <paramref name="via"/> yet (asked for the IP
    /// <paramref name="probe"/> gives) get the app's routes for them on another adapter deleted (the cable came back with another
    /// index, the Wi-Fi network changed) and a route through <paramref name="via"/>. Returns how many were added.
    /// </summary>
    async Task<int> EnsureVia(IReadOnlyList<string> prefixes, Func<string, string> probe, Adapter via, List<Adapter> adapters)
    {
        var missing = prefixes.Where(p => Net.BestInterface(probe(p)) != via.Index).ToList();
        if (missing.Count == 0) return 0;
        var want = missing.ToHashSet();
        var hosts = missing.Where(p => p.EndsWith("/32")).Select(p => p[..^3]).ToHashSet();
        await DeleteOurs(await _routes.List(), adapters, rt => want.Contains(rt.Prefix) && RouteKey.Of(rt) != RouteKey.Of(rt.Prefix, via), hosts);
        var added = 0;
        foreach (var p in missing)
        {
            var r = await Net.AddRoute(p, via);
            if (r.Ok) { Own(RouteKey.Of(p, via)); added++; _warned.Remove("add " + p); }
            else if (RouteAlreadyThere(r.Output)) Own(RouteKey.Of(p, via));   // the same route is there already: it is the app's
            // An adapter on its way out (Airplane Mode, the cable) refuses routes: no news, the drop says it (9 nov., 5 lines)
            else if (!RouteAlreadyGone(r.Output) && StillUp(via) && _warned.Add("add " + p)) Log.Warn($"Route {(p.EndsWith("/32") ? p[..^3] : p)}: {r.Output}");
        }
        return added;
    }

    /// <summary>Windows answered that the route exists already (English or Portuguese Windows, or the error code).</summary>
    public static bool RouteAlreadyThere(string output) =>
        output != null && (output.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || output.Contains("já existe", StringComparison.OrdinalIgnoreCase)
            || output.Contains("0x80071392", StringComparison.OrdinalIgnoreCase));

    /// <summary>The adapter is still there and up with the same local IP (read again now, not from the sync's start).</summary>
    static bool StillUp(Adapter a) => a != null && StillUp(a, Net.Scan());

    public static bool StillUp(Adapter a, IEnumerable<Adapter> now) =>
        a != null && now?.Any(x => x.Index == a.Index && x.Up && x.LocalIp == a.LocalIp) == true;

    async Task RemovePrefixRoutes(string[] prefixes, List<Adapter> adapters)
    {
        if (!OwnedKeys().Any(k => prefixes.Contains(k.Prefix))) return;
        var routes = await _routes.List();
        var removed = await DeleteOurs(routes, adapters, rt => prefixes.Contains(rt.Prefix), NoServers) > 0;
        // Routes Windows already dropped with their adapter (tethering turned off) are no longer ours, or this ran (and logged) every sync.
        DisownGone(prefixes, routes);
        if (removed) Log.Info("Steam through the phone turned off: Steam goes back to the home network.");
    }

    void DisownGone(string[] prefixes, List<Net.Route> routes)
    {
        foreach (var key in GoneFrom(OwnedKeys().Where(k => prefixes.Contains(k.Prefix)), routes)) Disown(key);
    }

    /// <summary>
    /// The app's routes Windows already dropped (not in <paramref name="routes"/>), which are no longer the app's. With a table that
    /// couldn't be read (null) none: a netsh that timed out is not "no routes".
    /// </summary>
    public static List<RouteKey> GoneFrom(IEnumerable<RouteKey> owned, List<Net.Route> routes)
    {
        if (routes == null) return new();
        var present = routes.Select(RouteKey.Of).ToHashSet();
        return owned.Where(k => !present.Contains(k)).ToList();
    }

    async Task RemoveSplitRoutes(List<Adapter> adapters)
    {
        if (!OwnedKeys().Any(k => SplitPrefixes.Contains(k.Prefix))) return;
        var routes = await _routes.List();
        DisownGone(SplitPrefixes, routes);
        await DeleteOurs(routes, adapters, rt => SplitPrefixes.Contains(rt.Prefix), NoServers);
        Log.Info("The rest of the PC goes back to the home network.");
    }

    /// <summary>Removes routes for servers that left the list (only the app's own or those on the phone's side).</summary>
    public Task RemoveIps(IEnumerable<string> ips, Settings s) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            var set = ips.ToHashSet();
            await DeleteOurs(await _routes.List(), Net.Scan(), rt => rt.Prefix.EndsWith("/32") && set.Contains(rt.Prefix[..^3]), set);
            _fwSignature = null;
        }
        finally { _gate.Release(); }
    });

    /// <summary>
    /// PowerShell arguments that keep the rest of the PC off an adapter (on: its default route ignored, metric 900) or give them back
    /// (off: default route and automatic metric). Active store only, so a restart undoes it too.
    /// </summary>
    public static string PcOffPhoneArgs(int index, bool on) => NetTcpIpArgs($"NetTCPIP\\Set-NetIPInterface -InterfaceIndex {index} -AddressFamily IPv4 " +
        (on ? "-InterfaceMetric 900 -IgnoreDefaultRoutes Enabled" : "-AutomaticMetric Enabled -IgnoreDefaultRoutes Disabled") + " -PolicyStore ActiveStore -ErrorAction Stop");

    /// <summary>
    /// For --restore: every phone adapter still ignoring its default route (the app's, if owned.json was lost) gets it back.
    /// </summary>
    public static string ResetPhonesArgs(IEnumerable<int> indexes) => NetTcpIpArgs($"NetTCPIP\\Get-NetIPInterface -InterfaceIndex {string.Join(",", indexes)} " +
        "-AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction SilentlyContinue | Where-Object IgnoreDefaultRoutes -eq 'Enabled' | " +
        "NetTCPIP\\Set-NetIPInterface -AutomaticMetric Enabled -IgnoreDefaultRoutes Disabled");

    /// <summary>
    /// NetTCPIP is loaded from System32 by full path and its cmdlets are called by the module name: a module of the same name in
    /// your Documents (first in PSModulePath) never runs as admin.
    /// </summary>
    static string NetTcpIpArgs(string command)
    {
        var module = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules", "NetTCPIP", "NetTCPIP.psd1");
        return $"-NoProfile -NonInteractive -Command \"Import-Module '{module}'; {command}\"";
    }

    /// <summary>
    /// Undoes everything the app created: rules, routes and whole-PC mode. Returns what is left (null = all clean).
    /// <paramref name="keepRegions"/>: Turn off protection and closing the app leave the Regions rule; --restore removes it too.
    /// </summary>
    public Task<string> RestoreAll(IReadOnlyCollection<string> ips, bool keepRegions) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            var adapters = Net.Scan();
            var set = ips.Append(Net.EchoIp).ToHashSet();
            var routes = await _routes.List();
            await DeleteOurs(routes, adapters, _ => true, set);
            // What Windows already dropped is no longer the app's; a route it refused to delete stays (the check below says so)
            foreach (var key in GoneFrom(OwnedKeys(), routes)) _ownedRoutes.Remove(key.ToString());
            SaveOwned();
            Firewall.RemoveAll(keepRegions);
            await RestoreMetric();
            var phones = adapters.Where(a => a.IsPhone).Select(a => a.Index).ToList();
            if (!keepRegions && phones.Count > 0 && await _powershell(ResetPhonesArgs(phones), 20000) is { Ok: false } reset)
                Log.Warn("Couldn't give the phone adapters their default route back: " + PsError(reset.Output));
            _fwSignature = null;

            // Reads everything back: the restore only counts if nothing of ours is left.
            var left = new List<string>();
            adapters = Net.Scan();
            var exit = ExitIndex(_exit, adapters);
            // A table that couldn't be read isn't "no routes left" either
            if (await _routes.List() is not { } after) left.Add("routes (could not read the route table)");
            else foreach (var rt in after)
                if (IsOurs(rt, _ownedRoutes, adapters, exit, set)) left.Add("route " + rt.Prefix);
            var rules = Firewall.Remaining();
            // A Firewall that couldn't be read isn't "nothing left": the restore isn't confirmed
            if (rules.Error != null) left.Add("rule \"" + rules.ErrorText + "\"");
            left.AddRange(rules.Where(n => !keepRegions || !n.StartsWith(Firewall.RegionsRuleName)).Select(n => "rule \"" + n + "\""));
            if (_metricAppliedTo >= 0) left.Add($"the metric of adapter {_metricAppliedTo}");
            if (left.Count == 0) { Log.Ok("Network restored: app rules and routes removed, checked."); return null; }
            var what = string.Join(", ", left.Take(5)) + (left.Count > 5 ? $" and {left.Count - 5} more" : "");
            Log.Error("Turn off protection couldn't remove: " + what + ".");
            return what;
        }
        finally { _gate.Release(); }
    });
}
