namespace SRT.Core;

public sealed class GuardReport
{
    public List<Adapter> Adapters { get; init; } = new();
    /// <summary>The connection trusted right now (null = everything blocked).</summary>
    public Adapter Egress { get; init; }
    /// <summary>The adapter that would be the protected connection before the IP check (so the IP keeps being measured through it).</summary>
    public Adapter Candidate { get; init; }
    /// <summary>Why there is no protected connection (e.g. the Wi-Fi jumped to the home router).</summary>
    public string EgressProblem { get; init; }
    public bool Distrusted { get; init; }
    public string FirewallProblem { get; init; }
    public bool KillSwitchOk { get; init; }
    public bool? Cs2LockOk { get; init; }
    public Dictionary<string, bool> RouteOk { get; init; } = new();
    public string EffectiveMode { get; init; }
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
    /// <summary>Why the candidate isn't trusted (null = trusted or no candidate).</summary>
    public string DistrustReason { get; init; }
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
    static string OwnedFile => Paths.File("owned.json");
    readonly HashSet<string> _warned = new();
    string _lastProblem;
    /// <summary>The Firewall errors of the last rewrite, so the same failure on every sync is logged once.</summary>
    string _fwErrors;

    static readonly string[] SplitPrefixes = { "0.0.0.0/1", "128.0.0.0/1" };

    /// <summary>What the app changed in Windows, saved to disk so a crash or a kill doesn't leave orphans behind.</summary>
    public sealed class OwnedState
    {
        public List<string> Routes { get; set; } = new();
        public int MetricIf { get; set; } = -1;
        /// <summary>When Windows booted: routes and the metric only live until a restart.</summary>
        public DateTime Boot { get; set; }
    }

    static DateTime BootTime => DateTime.Now.AddMilliseconds(-Environment.TickCount64);

    public Guard()
    {
        var st = Store.Load(OwnedFile, () => new OwnedState());
        // From before a restart: Windows already dropped those routes, and the interface index may now be another adapter.
        if (Math.Abs((st.Boot - BootTime).TotalMinutes) > 2) return;
        foreach (var r in st.Routes ?? new()) _ownedRoutes.Add(r);
        _metricAppliedTo = st.MetricIf;
    }

    void SaveOwned()
    {
        try { Store.Save(OwnedFile, new OwnedState { Routes = _ownedRoutes.ToList(), MetricIf = _metricAppliedTo, Boot = BootTime }); }
        catch (Exception ex) { Log.Warn("Could not save owned.json: " + ex.Message); }
    }
    void Own(string key) { if (_ownedRoutes.Add(key)) SaveOwned(); }
    bool Disown(string key) { if (!_ownedRoutes.Remove(key)) return false; SaveOwned(); return true; }

    public const string WhyUnchecked = "The phone connection's public IP hasn't been checked yet.";

    public static string KeyOf(Adapter a) => a == null ? null : $"{a.Index}|{a.LocalIp}";

    /// <param name="distrust">Why the connection can't be trusted right now (null = trusted).</param>
    /// <param name="verifiedKey">The candidate whose public IP was measured and isn't the home IP. Any other candidate stays blocked.</param>
    public Task<GuardReport> Sync(Settings s, IReadOnlyCollection<string> ips, bool force = false, string distrust = null, bool cs2Running = false, string verifiedKey = null) =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try { return await SyncCore(s, ips.Distinct().ToList(), force, distrust, cs2Running, verifiedKey); }
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

    /// <summary>
    /// Windows sends the rest of the PC through the phone while it shouldn't (game only or no phone): the phone's adapter gets a
    /// high metric so the home network wins. Only with another connection to fall back on, and only with the option on.
    /// </summary>
    public static bool NeedsPcOffPhone(string effectiveMode, bool keepPcOffPhone, int pcDefaultIndex, int phoneIndex, bool hasOtherInternet) =>
        keepPcOffPhone && effectiveMode is "servers" or "home" && phoneIndex >= 0 && pcDefaultIndex == phoneIndex && hasOtherInternet;

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

    async Task<GuardReport> SyncCore(Settings s, List<string> ips, bool force, string distrust, bool cs2Running, string verifiedKey)
    {
        var adapters = Net.Scan();
        var candidate = Net.PickEgress(adapters, s.EgressAdapterId, s.EgressSsid, out var problem);
        // A new connection (another adapter or local IP) is only trusted after its public IP is measured.
        if (distrust == null && candidate != null && KeyOf(candidate) != verifiedKey) distrust = WhyUnchecked;
        var egress = distrust != null ? null : candidate;
        if (distrust != null && candidate != null) problem = distrust;
        var mode = ModeFor(s.Mode, s.GameMode, cs2Running);
        // No phone blocks nothing, so "Servers blocked" would be false there; back on protection the problem is said again.
        if (mode == "home") _lastProblem = null;
        else if (problem != _lastProblem) { if (problem != null) Log.Warn(problem + " Servers blocked."); _lastProblem = problem; }
        var fwProblem = Firewall.CheckEnabled();
        if (mode == "home") return await HomeMode(s, adapters, candidate, fwProblem, force);
        var cs2 = s.LockCs2 ? Steam.Cs2Path(s) : null;

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
                echoOk = Firewall.Verify(Firewall.EchoRuleName, new[] { Net.EchoIp }, candidate?.LocalIp, Environment.ProcessPath);
                cs2Ok = cs2 != null ? Firewall.Verify(Cs2Rule(cs2, allow)) : null;
                dnsOk = full != null ? Firewall.Verify(full[0]) && Firewall.Verify(full[1]) : null;
                ipv6Ok = full != null ? Firewall.Verify(full[2]) : null;
            }
            ReadBack();
            if (force || sig != _fwSignature || !killOk || !echoOk || cs2Ok == false || dnsOk == false || ipv6Ok == false)
            {
                // Each rule on its own: one failing must not leave the others on an old allowed IP.
                var errors = new List<string>();
                void Try(Action a)
                {
                    try { a(); }
                    catch (Exception ex) { errors.Add((ex.InnerException ?? ex).Message); }
                }
                Try(() => Firewall.Apply(Firewall.RuleName, ips, null, allow, "Managed by CS2 IP Changer. Servers in the list can only be reached from the local IP of the protected connection."));
                // Only the app's own IP test: other programs (1.0.0.1 is also a public DNS server) are left alone.
                Try(() => Firewall.Apply(Firewall.EchoRuleName, new[] { Net.EchoIp }, Environment.ProcessPath, candidate?.LocalIp, "Managed by CS2 IP Changer. Makes sure the app's IP test measures the protected connection."));
                if (cs2 != null) Try(() => Firewall.Apply(Cs2Rule(cs2, allow)));
                else Firewall.Remove(Firewall.Cs2RuleName);
                if (full != null) foreach (var r in full) Try(() => Firewall.Apply(r));
                else foreach (var n in FullModeRuleNames) Firewall.Remove(n);
                _fwSignature = sig;
                // Without admin rights every sync (5 s) fails the same way: one line until it changes, and no "blocked" that isn't true.
                if (FirewallErrorLine(errors, ref _fwErrors) is string line) { if (errors.Count > 0) Log.Error(line); else Log.Ok(line); }
                if (errors.Count == 0)
                    Log.Info(egress == null
                        ? "Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + " blocked (no connection)."
                        : $"Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + $" only go out through \"{egress.Name}\" ({egress.LocalIp}).");
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
                s.LegacyCleaned = true;
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
                if (!routeOk[ip] && _warned.Add(ip)) Log.Warn($"Windows does not pick the protected connection for {ip}. Another VPN may be overriding it.");
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
            if (Net.BestInterface("1.1.1.1") != egress.Index || Net.BestInterface("200.1.1.1") != egress.Index)
            {
                foreach (var pfx in SplitPrefixes)
                {
                    var r = await Net.AddRoute(pfx, egress, 5);
                    if (r.Ok) Own($"{pfx}|{egress.Index}|{egress.Gateway}");
                }
                Log.Info(s.Mode == "all" ? "The whole PC goes through the phone." : "CS2 is open: the whole PC goes through the phone.");
            }
            fullOk = Net.BestInterface("1.1.1.1") == egress.Index;
        }
        else await RemoveSplitRoutes(adapters);

        var pcIdx = Net.BestInterface("1.1.1.1");
        var pc = adapters.FirstOrDefault(a => a.Index == pcIdx);
        var steam = Net.ReadSteamLink();
        bool? steamOnEgress = !steam.Connected || egress == null ? null : steam.LocalIps.All(ip => ip == egress.LocalIp);
        return new GuardReport
        {
            Adapters = adapters,
            Egress = egress,
            Candidate = candidate,
            EgressProblem = problem,
            Distrusted = distrust != null && candidate != null,
            DistrustReason = candidate != null ? distrust : null,
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
            HasHome = adapters.Any(a => a.Up && a.Gateway != null && a.Index != candidate?.Index && !a.IsPhone),
        };
    }

    /// <summary>
    /// The phone's adapter ignores its default route (and gets metric 900), so the rest of the PC stays on the home network. The
    /// metric alone was not enough: on a real PC Windows kept the phone for the rest of the PC whatever its metric. Every mode reaches
    /// the phone through explicit routes to its gateway, so none needs that default route. Applied again whenever Windows still picks
    /// the phone (a cable that comes back can keep its interface index but loses the setting); undone as soon as it is not wanted.
    /// </summary>
    async Task KeepPcOffPhone(Settings s, string mode, Adapter phone, List<Adapter> adapters)
    {
        var other = phone != null && HasOtherInternet(adapters, phone);
        var onPhone = phone != null && Net.BestInterface("1.1.1.1") == phone.Index;
        var step = PcOffPhoneNext(s.KeepPcOffPhone, s.Mode, mode, phone, other, _metricAppliedTo, onPhone, (DateTime.Now - _metricTriedAt).TotalSeconds);
        if (step == PcOffPhoneStep.Undo)
        {
            await RestoreMetric();
            if (s.KeepPcOffPhone && phone != null && !other) Log.Info("No home connection: the rest of the PC goes through the phone again.");
            return;
        }
        if (step != PcOffPhoneStep.Apply) return;
        if (_metricAppliedTo >= 0 && _metricAppliedTo != phone.Index) await RestoreMetric();
        _metricTriedAt = DateTime.Now;
        var r = await Proc.Run("powershell", PcOffPhoneArgs(phone.Index, true), 20000);
        if (!r.Ok) { if (_warned.Add("metric")) Log.Warn("Couldn't keep the rest of the PC off the phone: " + PsError(r.Output)); return; }
        _warned.Remove("metric");
        _metricAppliedTo = phone.Index;
        SaveOwned();
        Log.Info(mode == "home" ? "No phone mode: the PC keeps using the home network, not the phone." : "The PC keeps using the home network; only the game goes through the phone.");
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

    async Task RestoreMetric()
    {
        if (_metricAppliedTo < 0) return;
        await Proc.Run("powershell", PcOffPhoneArgs(_metricAppliedTo, false), 20000);
        _metricAppliedTo = -1;
        SaveOwned();
    }

    /// <summary>
    /// No phone mode: the kill switch, the IP test rule, the CS2 lock, whole PC and every route of the app are taken off, so CS2 and
    /// the servers use the home connection. The Regions rule is not the guard's and stays. Done again only when something changed.
    /// </summary>
    async Task<GuardReport> HomeMode(Settings s, List<Adapter> adapters, Adapter phone, string fwProblem, bool force)
    {
        if (force || _fwSignature != "home")
        {
            if (fwProblem == null) Firewall.RemoveProtection();
            await RemoveOurRoutes(new HashSet<string> { Net.EchoIp }, adapters, all: true);
            if (_fwSignature != "home") Log.Info("No phone mode: the kill switch and the routes are off. CS2 and servers see your home IP.");
            _fwSignature = "home";
        }
        await KeepPcOffPhone(s, "home", phone, adapters);
        var pcIdx = Net.BestInterface("1.1.1.1");
        return new GuardReport
        {
            Adapters = adapters,
            FirewallProblem = fwProblem,
            EffectiveMode = "home",
            PcDefaultName = adapters.FirstOrDefault(a => a.Index == pcIdx)?.Name ?? "—",
            PcDefaultIndex = pcIdx,
            PhoneIndex = phone?.Index ?? -1,
            Steam = Net.ReadSteamLink(),
            HasHome = adapters.Any(a => a.Up && a.Gateway != null && !a.IsPhone),
        };
    }

    /// <summary>
    /// Deletes the app's routes: its own (owned.json) and, on a phone adapter, server /32s, whole PC and Valve's blocks.
    /// <paramref name="all"/>: every /32 on a phone adapter counts as a server route (the list may have changed).
    /// </summary>
    async Task RemoveOurRoutes(HashSet<string> serverIps, List<Adapter> adapters, bool all = false)
    {
        foreach (var rt in await Net.ListRoutes())
        {
            var ours = _ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            var onPhone = adapters.Any(a => a.Index == rt.Index && a.IsPhone);
            var serverRoute = rt.Prefix.EndsWith("/32") && (all || serverIps.Contains(rt.Prefix[..^3]));
            if (ours || (onPhone && (serverRoute || SplitPrefixes.Contains(rt.Prefix) || Net.ValvePrefixes.Contains(rt.Prefix))))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
    }

    static readonly string[] FullModeRuleNames = { Firewall.DnsUdpRuleName, Firewall.DnsTcpRuleName, Firewall.Ipv6RuleName };

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

    static bool HasOtherInternet(List<Adapter> adapters, Adapter egress) =>
        adapters.Any(a => a.Up && a.Gateway != null && a.Index != egress.Index && !a.IsPhone);

    /// <summary>Fast path: the Windows API says whether each IP already leaves through the adapter. Routes are only touched if one is missing.</summary>
    async Task EnsureRoutes(List<string> ips, Adapter via, List<Adapter> adapters)
    {
        var missing = ips.Where(ip => Net.BestInterface(ip) != via.Index).ToList();
        if (missing.Count == 0) return;
        // Clears our routes left on another adapter (e.g. the cable came back with another index, or the Wi-Fi network changed).
        foreach (var rt in await Net.ListRoutes())
        {
            var ip = rt.Prefix.EndsWith("/32") ? rt.Prefix[..^3] : null;
            if (ip == null || !missing.Contains(ip)) continue;
            var ours = _ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            if ((ours || adapters.Any(a => a.Index == rt.Index && a.IsPhone)) && (rt.Index != via.Index || rt.NextHop != (via.Gateway ?? "0.0.0.0")))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        foreach (var ip in missing)
        {
            var r = await Net.AddRoute(ip + "/32", via);
            if (r.Ok) Own($"{ip}/32|{via.Index}|{via.Gateway ?? "0.0.0.0"}");
            else Log.Warn($"Route {ip}: {r.Output}");
        }
    }

    /// <summary>Like EnsureRoutes, but for whole blocks (e.g. Valve's).</summary>
    async Task EnsurePrefixRoutes(string[] prefixes, Adapter via, List<Adapter> adapters)
    {
        var missing = prefixes.Where(p => Net.BestInterface(Net.ProbeIp(p)) != via.Index).ToList();
        if (missing.Count == 0) return;
        var hop = via.Gateway ?? "0.0.0.0";
        foreach (var rt in await Net.ListRoutes())
        {
            if (!missing.Contains(rt.Prefix)) continue;
            var ours = _ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            if ((ours || adapters.Any(a => a.Index == rt.Index && a.IsPhone)) && (rt.Index != via.Index || rt.NextHop != hop))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        var added = 0;
        foreach (var pfx in missing)
        {
            var r = await Net.AddRoute(pfx, via);
            if (r.Ok) { Own($"{pfx}|{via.Index}|{hop}"); added++; }
            else Log.Warn($"Route {pfx}: {r.Output}");
        }
        if (added > 0) Log.Info($"Steam through the phone: {Fmt.Count(added, "Valve block")} {(added == 1 ? "goes" : "go")} through \"{via.Name}\".");
    }

    async Task RemovePrefixRoutes(string[] prefixes, List<Adapter> adapters)
    {
        if (!_ownedRoutes.Any(r => prefixes.Any(p => r.StartsWith(p + "|")))) return;
        var routes = await Net.ListRoutes();
        var removed = false;
        foreach (var rt in routes)
        {
            if (!prefixes.Contains(rt.Prefix)) continue;
            if (_ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}") || adapters.Any(a => a.Index == rt.Index && a.IsPhone))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
                removed = true;
            }
        }
        // Routes Windows already dropped with their adapter (tethering turned off) are no longer ours, or this ran (and logged) every sync.
        DisownGone(prefixes, routes);
        if (removed) Log.Info("Steam through the phone turned off: Steam goes back to the home network.");
    }

    void DisownGone(string[] prefixes, List<Net.Route> routes)
    {
        var present = routes.Select(rt => $"{rt.Prefix}|{rt.Index}|{rt.NextHop}").ToHashSet();
        foreach (var key in _ownedRoutes.Where(r => prefixes.Any(p => r.StartsWith(p + "|")) && !present.Contains(r)).ToList()) Disown(key);
    }

    async Task RemoveSplitRoutes(List<Adapter> adapters)
    {
        if (_ownedRoutes.Count == 0 || !_ownedRoutes.Any(r => r.StartsWith("0.0.0.0/1") || r.StartsWith("128.0.0.0/1"))) return;
        var routes = await Net.ListRoutes();
        DisownGone(SplitPrefixes, routes);
        foreach (var rt in routes)
        {
            if (!SplitPrefixes.Contains(rt.Prefix)) continue;
            if (_ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}") || adapters.Any(a => a.Index == rt.Index && a.IsPhone))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        Log.Info("The rest of the PC goes back to the home network.");
    }

    /// <summary>Removes routes for servers that left the list (only the app's own or those on phone adapters).</summary>
    public Task RemoveIps(IEnumerable<string> ips, Settings s) => Task.Run(async () =>
    {
        await _gate.WaitAsync();
        try
        {
            var set = ips.ToHashSet();
            var adapters = Net.Scan();
            foreach (var rt in await Net.ListRoutes())
            {
                if (!rt.Prefix.EndsWith("/32") || !set.Contains(rt.Prefix[..^3])) continue;
                if (Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}") || adapters.Any(a => a.Index == rt.Index && a.IsPhone))
                    await Net.DeleteRoute(rt);
            }
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
            foreach (var rt in await Net.ListRoutes())
            {
                var ours = _ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
                var onPhone = adapters.Any(a => a.Index == rt.Index && a.IsPhone);
                var serverRoute = rt.Prefix.EndsWith("/32") && set.Contains(rt.Prefix[..^3]);
                if (ours || (onPhone && (serverRoute || SplitPrefixes.Contains(rt.Prefix) || Net.ValvePrefixes.Contains(rt.Prefix)))) await Net.DeleteRoute(rt);
            }
            _ownedRoutes.Clear();
            SaveOwned();
            Firewall.RemoveAll(keepRegions);
            await RestoreMetric();
            var phones = adapters.Where(a => a.IsPhone).Select(a => a.Index).ToList();
            if (!keepRegions && phones.Count > 0) await Proc.Run("powershell", ResetPhonesArgs(phones), 20000);
            _fwSignature = null;

            // Reads everything back: the restore only counts if nothing of ours is left.
            var left = new List<string>();
            adapters = Net.Scan();
            foreach (var rt in await Net.ListRoutes())
            {
                var onPhone = adapters.Any(a => a.Index == rt.Index && a.IsPhone);
                var serverRoute = rt.Prefix.EndsWith("/32") && set.Contains(rt.Prefix[..^3]);
                if (onPhone && (serverRoute || SplitPrefixes.Contains(rt.Prefix) || Net.ValvePrefixes.Contains(rt.Prefix))) left.Add("route " + rt.Prefix);
            }
            left.AddRange(Firewall.Remaining().Where(n => !keepRegions || !n.StartsWith(Firewall.RegionsRuleName)).Select(n => "rule \"" + n + "\""));
            if (left.Count == 0) { Log.Ok("Network restored: app rules and routes removed, checked."); return null; }
            var what = string.Join(", ", left.Take(5)) + (left.Count > 5 ? $" and {left.Count - 5} more" : "");
            Log.Error("Turn off protection couldn't remove: " + what + ".");
            return what;
        }
        finally { _gate.Release(); }
    });
}
