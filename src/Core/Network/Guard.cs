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
    public bool FullModeOk { get; init; }
    public string PcDefaultName { get; init; }
    public bool PcDefaultIsEgress { get; init; }
    public int PcDefaultIndex { get; init; }
    public string Error { get; init; }
    /// <summary>Routes to Valve's servers go through the protected connection (null = option off or no connection).</summary>
    public bool? SteamRoutesOk { get; init; }
    public SteamLink Steam { get; init; } = new();
    /// <summary>steam.exe talks to Valve through the protected connection (null = Steam closed or not connected).</summary>
    public bool? SteamOnEgress { get; init; }
    public bool AllRoutesOk => RouteOk.Count > 0 && RouteOk.Values.All(v => v);
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
    readonly HashSet<string> _warned = new();
    string _lastProblem;

    static readonly string[] SplitPrefixes = { "0.0.0.0/1", "128.0.0.0/1" };

    public Task<GuardReport> Sync(Settings s, IReadOnlyCollection<string> ips, bool force = false, bool distrust = false, bool cs2Running = false) =>
        Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try { return await SyncCore(s, ips.Distinct().ToList(), force, distrust, cs2Running); }
            catch (Exception ex)
            {
                Log.Error("Sync: " + ex.Message);
                return new GuardReport { Error = ex.Message };
            }
            finally { _gate.Release(); }
        });

    async Task<GuardReport> SyncCore(Settings s, List<string> ips, bool force, bool distrust, bool cs2Running)
    {
        var adapters = Net.Scan();
        var candidate = Net.PickEgress(adapters, s.EgressAdapterId, s.EgressSsid, out var problem);
        var egress = distrust ? null : candidate;
        if (distrust && candidate != null) problem = "The public IP of the connection is the same as your home IP.";
        if (problem != _lastProblem) { if (problem != null) Log.Warn(problem + " Servers blocked."); _lastProblem = problem; }
        var fwProblem = Firewall.CheckEnabled();
        var mode = s.GameMode && cs2Running ? "all" : s.Mode;
        var cs2 = s.LockCs2 ? Steam.Cs2Path(s) : null;

        // 1) Firewall first: there is never a moment when a server is reachable with the home IP.
        var killOk = false;
        bool? cs2Ok = null;
        if (fwProblem == null)
        {
            var allow = egress?.LocalIp;
            var sig = string.Join(",", ips.OrderBy(i => i)) + "|" + allow + "|" + candidate?.LocalIp + "|" + cs2;
            if (force || sig != _fwSignature || !Firewall.Verify(Firewall.RuleName, ips, allow))
            {
                Firewall.Apply(Firewall.RuleName, ips, null, allow, "Managed by CS2 IP Changer. Servers in the list can only be reached from the local IP of the protected connection.");
                Firewall.Apply(Firewall.EchoRuleName, new[] { Net.EchoIp }, null, candidate?.LocalIp, "Managed by CS2 IP Changer. Makes sure the IP test measures the protected connection.");
                if (cs2 != null) Firewall.Apply(Firewall.Cs2RuleName, null, cs2, allow, "Managed by CS2 IP Changer. CS2 can only go out through the protected connection: servers not in the list never see your home IP.");
                else Firewall.Remove(Firewall.Cs2RuleName);
                _fwSignature = sig;
                Log.Info(egress == null
                    ? "Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + " blocked (no connection)."
                    : $"Kill switch: servers in the list" + (cs2 != null ? " and CS2" : "") + $" only go out through \"{egress.Name}\" ({egress.LocalIp}).");
            }
            killOk = Firewall.Verify(Firewall.RuleName, ips, allow);
            if (cs2 != null) cs2Ok = Firewall.Verify(Firewall.Cs2RuleName, null, allow);

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
        if (egress != null)
        {
            if (s.KeepPcOffPhone && mode == "servers" && (egress.IsPhone || egress.IsWifi) && _metricAppliedTo != egress.Index && HasOtherInternet(adapters, egress))
            {
                var r = await Net.SetInterfaceMetric(egress.Index, 900);
                if (r.Ok) { _metricAppliedTo = egress.Index; Log.Info("The PC keeps using the home network; only the game goes through the phone."); }
            }
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
                    if (r.Ok) _ownedRoutes.Add($"{pfx}|{egress.Index}|{egress.Gateway}");
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
            Distrusted = distrust && candidate != null,
            FirewallProblem = fwProblem,
            KillSwitchOk = killOk,
            Cs2LockOk = cs2Ok,
            RouteOk = routeOk,
            EffectiveMode = mode,
            FullModeOk = fullOk,
            PcDefaultName = pc?.Name ?? "—",
            PcDefaultIsEgress = egress != null && pcIdx == egress.Index,
            PcDefaultIndex = pcIdx,
            SteamRoutesOk = steamRoutesOk,
            Steam = steam,
            SteamOnEgress = steamOnEgress,
        };
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
                _ownedRoutes.Remove($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        foreach (var ip in missing)
        {
            var r = await Net.AddRoute(ip + "/32", via);
            if (r.Ok) _ownedRoutes.Add($"{ip}/32|{via.Index}|{via.Gateway ?? "0.0.0.0"}");
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
                _ownedRoutes.Remove($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
            }
        }
        var added = 0;
        foreach (var pfx in missing)
        {
            var r = await Net.AddRoute(pfx, via);
            if (r.Ok) { _ownedRoutes.Add($"{pfx}|{via.Index}|{hop}"); added++; }
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
                _ownedRoutes.Remove($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
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
                _ownedRoutes.Remove($"{rt.Prefix}|{rt.Index}|{rt.NextHop}");
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
                if (_ownedRoutes.Remove($"{rt.Prefix}|{rt.Index}|{rt.NextHop}") || adapters.Any(a => a.Index == rt.Index && a.IsPhone))
                    await Net.DeleteRoute(rt);
            }
            _fwSignature = null;
        }
        finally { _gate.Release(); }
    });

    /// <summary>Undoes everything the app created: rules, routes and whole-PC mode.</summary>
    public Task RestoreAll(IReadOnlyCollection<string> ips) => Task.Run(async () =>
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
            Firewall.RemoveAll();
            if (_metricAppliedTo >= 0)
            {
                await Proc.Run("powershell", $"-NoProfile -Command \"Set-NetIPInterface -InterfaceIndex {_metricAppliedTo} -AddressFamily IPv4 -AutomaticMetric Enabled -PolicyStore ActiveStore\"", 20000);
                _metricAppliedTo = -1;
            }
            _fwSignature = null;
            Log.Ok("Network restored: app rules and routes removed.");
        }
        finally { _gate.Release(); }
    });
}
