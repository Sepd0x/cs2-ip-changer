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
    readonly HashSet<string> _ownedRoutes = new(); // "prefix|ifIndex|nexthop"
    static string OwnedFile => Paths.File("owned.json");
    readonly HashSet<string> _warned = new();
    string _lastProblem;

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

    async Task<GuardReport> SyncCore(Settings s, List<string> ips, bool force, string distrust, bool cs2Running, string verifiedKey)
    {
        var adapters = Net.Scan();
        var candidate = Net.PickEgress(adapters, s.EgressAdapterId, s.EgressSsid, out var problem);
        // A new connection (another adapter or local IP) is only trusted after its public IP is measured.
        if (distrust == null && candidate != null && KeyOf(candidate) != verifiedKey) distrust = WhyUnchecked;
        var egress = distrust != null ? null : candidate;
        if (distrust != null && candidate != null) problem = distrust;
        if (problem != _lastProblem) { if (problem != null) Log.Warn(problem + " Servers blocked."); _lastProblem = problem; }
        var fwProblem = Firewall.CheckEnabled();
        var mode = s.GameMode && cs2Running ? "all" : s.Mode;
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
                cs2Ok = cs2 != null ? Firewall.Verify(Firewall.Cs2RuleName, null, allow, cs2) : null;
                dnsOk = full != null ? Firewall.Verify(full[0]) && Firewall.Verify(full[1]) : null;
                ipv6Ok = full != null ? Firewall.Verify(full[2]) : null;
            }
            ReadBack();
            if (force || sig != _fwSignature || !killOk || !echoOk || cs2Ok == false || dnsOk == false || ipv6Ok == false)
            {
                // Each rule on its own: one failing must not leave the others on an old allowed IP.
                Try(() => Firewall.Apply(Firewall.RuleName, ips, null, allow, "Managed by CS2 IP Changer. Servers in the list can only be reached from the local IP of the protected connection."));
                // Only the app's own IP test: other programs (1.0.0.1 is also a public DNS server) are left alone.
                Try(() => Firewall.Apply(Firewall.EchoRuleName, new[] { Net.EchoIp }, Environment.ProcessPath, candidate?.LocalIp, "Managed by CS2 IP Changer. Makes sure the app's IP test measures the protected connection."));
                if (cs2 != null) Try(() => Firewall.Apply(Firewall.Cs2RuleName, null, cs2, allow, "Managed by CS2 IP Changer. CS2 can only go out through the protected connection: servers not in the list never see your home IP."));
                else Firewall.Remove(Firewall.Cs2RuleName);
                if (full != null) foreach (var r in full) Try(() => Firewall.Apply(r));
                else foreach (var n in FullModeRuleNames) Firewall.Remove(n);
                _fwSignature = sig;
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
        var offPc = egress ?? candidate;
        if (offPc != null && s.KeepPcOffPhone && mode == "servers" && (offPc.IsPhone || offPc.IsWifi) && _metricAppliedTo != offPc.Index && HasOtherInternet(adapters, offPc))
        {
            var r = await Net.SetInterfaceMetric(offPc.Index, 900);
            if (r.Ok) { _metricAppliedTo = offPc.Index; SaveOwned(); Log.Info("The PC keeps using the home network; only the game goes through the phone."); }
        }
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
            SteamRoutesOk = steamRoutesOk,
            Steam = steam,
            SteamOnEgress = steamOnEgress,
            HasHome = adapters.Any(a => a.Up && a.Gateway != null && a.Index != candidate?.Index && !a.IsPhone),
        };
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

    static void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error("Firewall: " + (ex.InnerException ?? ex).Message); }
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
        if (added > 0) Log.Info($"Steam through the phone: {added} Valve block(s) go through \"{via.Name}\".");
    }

    async Task RemovePrefixRoutes(string[] prefixes, List<Adapter> adapters)
    {
        if (!_ownedRoutes.Any(r => prefixes.Any(p => r.StartsWith(p + "|")))) return;
        foreach (var rt in await Net.ListRoutes())
        {
            if (!prefixes.Contains(rt.Prefix)) continue;
            if (_ownedRoutes.Contains($"{rt.Prefix}|{rt.Index}|{rt.NextHop}") || adapters.Any(a => a.Index == rt.Index && a.IsPhone))
            {
                await Net.DeleteRoute(rt);
                Disown($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        Log.Info("Steam through the phone turned off: Steam goes back to the home network.");
    }

    async Task RemoveSplitRoutes(List<Adapter> adapters)
    {
        if (_ownedRoutes.Count == 0 || !_ownedRoutes.Any(r => r.StartsWith("0.0.0.0/1") || r.StartsWith("128.0.0.0/1"))) return;
        foreach (var rt in await Net.ListRoutes())
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

    /// <summary>Undoes everything the app created: rules, routes and whole-PC mode. Returns what is left (null = all clean).</summary>
    public Task<string> RestoreAll(IReadOnlyCollection<string> ips) => Task.Run(async () =>
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
            Firewall.RemoveAll();
            if (_metricAppliedTo >= 0)
            {
                await Proc.Run("powershell", $"-NoProfile -Command \"Set-NetIPInterface -InterfaceIndex {_metricAppliedTo} -AddressFamily IPv4 -AutomaticMetric Enabled -PolicyStore ActiveStore\"", 20000);
                _metricAppliedTo = -1;
                SaveOwned();
            }
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
            left.AddRange(Firewall.Remaining().Select(n => "rule \"" + n + "\""));
            if (left.Count == 0) { Log.Ok("Network restored: app rules and routes removed, checked."); return null; }
            var what = string.Join(", ", left.Take(5)) + (left.Count > 5 ? $" and {left.Count - 5} more" : "");
            Log.Error("Restore network left: " + what + ".");
            return what;
        }
        finally { _gate.Release(); }
    });
}
