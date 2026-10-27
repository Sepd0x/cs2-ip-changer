namespace SRT.Core;

public sealed class GuardReport
{
    public List<Adapter> Adapters { get; init; } = new();
    /// <summary>A saída em que se confia agora (null = tudo bloqueado).</summary>
    public Adapter Egress { get; init; }
    /// <summary>A placa que seria a saída antes da verificação do IP (para continuar a medir o IP por ela).</summary>
    public Adapter Candidate { get; init; }
    /// <summary>Porque é que não há saída (ex.: o Wi-Fi saltou para o router de casa).</summary>
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
    /// <summary>As rotas dos servidores da Valve saem pela saída (null = opção desligada ou sem saída).</summary>
    public bool? SteamRoutesOk { get; init; }
    public SteamLink Steam { get; init; } = new();
    /// <summary>O steam.exe fala com a Valve pela saída (null = Steam fechado ou sem ligação).</summary>
    public bool? SteamOnEgress { get; init; }
    public bool AllRoutesOk => RouteOk.Count > 0 && RouteOk.Values.All(v => v);
}

/// <summary>
/// Mantém a proteção, em três barreiras:
///  1. Firewall: os servidores (e o próprio cs2.exe) só podem sair com o IP local da saída protegida.
///  2. A saída só conta se for a rede certa (no Wi-Fi, o ponto de acesso guardado).
///  3. Se o IP público da saída for igual ao de casa, deixa de se confiar nela (a janela avisa com <c>distrust</c>).
/// Tudo corre fora da thread da janela, uma operação de cada vez.
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
                Log.Error("Sincronização: " + ex.Message);
                return new GuardReport { Error = ex.Message };
            }
            finally { _gate.Release(); }
        });

    async Task<GuardReport> SyncCore(Settings s, List<string> ips, bool force, bool distrust, bool cs2Running)
    {
        var adapters = Net.Scan();
        var candidate = Net.PickEgress(adapters, s.EgressAdapterId, s.EgressSsid, out var problem);
        var egress = distrust ? null : candidate;
        if (distrust && candidate != null) problem = "O IP público da saída é igual ao da tua casa.";
        if (problem != _lastProblem) { if (problem != null) Log.Warn(problem + " Servidores bloqueados."); _lastProblem = problem; }
        var fwProblem = Firewall.CheckEnabled();
        var mode = s.GameMode && cs2Running ? "all" : s.Mode;
        var cs2 = s.LockCs2 ? Steam.Cs2Path(s) : null;

        // 1) Firewall primeiro: nunca há um momento em que um servidor seja acessível com o IP de casa.
        var killOk = false;
        bool? cs2Ok = null;
        if (fwProblem == null)
        {
            var allow = egress?.LocalIp;
            var sig = string.Join(",", ips.OrderBy(i => i)) + "|" + allow + "|" + candidate?.LocalIp + "|" + cs2;
            if (force || sig != _fwSignature || !Firewall.Verify(Firewall.RuleName, ips, allow))
            {
                Firewall.Apply(Firewall.RuleName, ips, null, allow, "Gerida pelo ServerRouteTool. Os servidores da lista só podem ser contactados a partir do IP local da saída protegida.");
                Firewall.Apply(Firewall.EchoRuleName, new[] { Net.EchoIp }, null, candidate?.LocalIp, "Gerida pelo ServerRouteTool. Garante que o teste de IP mede a saída protegida.");
                if (cs2 != null) Firewall.Apply(Firewall.Cs2RuleName, null, cs2, allow, "Gerida pelo ServerRouteTool. O CS2 só pode sair pela saída protegida: servidores fora da lista nunca veem o IP de casa.");
                else Firewall.Remove(Firewall.Cs2RuleName);
                _fwSignature = sig;
                Log.Info(egress == null
                    ? "Kill switch: servidores da lista" + (cs2 != null ? " e CS2" : "") + " bloqueados (sem saída)."
                    : $"Kill switch: servidores da lista" + (cs2 != null ? " e CS2" : "") + $" só saem por \"{egress.Name}\" ({egress.LocalIp}).");
            }
            killOk = Firewall.Verify(Firewall.RuleName, ips, allow);
            if (cs2 != null) cs2Ok = Firewall.Verify(Firewall.Cs2RuleName, null, allow);

            if (!s.LegacyCleaned)
            {
                var n = Firewall.RemoveLegacy();
                if (n > 0) Log.Ok($"Removidas {n} regras antigas da v6/v7/v8.");
                s.LegacyCleaned = true;
            }
        }

        // 2) Rotas /32: o teste de IP vai sempre pela candidata; os servidores só pela saída confiável.
        if (candidate != null) await EnsureRoutes(new List<string> { Net.EchoIp }, candidate, adapters);
        var routeOk = new Dictionary<string, bool>();
        if (egress != null)
        {
            if (s.KeepPcOffPhone && mode == "servers" && (egress.IsPhone || egress.IsWifi) && _metricAppliedTo != egress.Index && HasOtherInternet(adapters, egress))
            {
                var r = await Net.SetInterfaceMetric(egress.Index, 900);
                if (r.Ok) { _metricAppliedTo = egress.Index; Log.Info("O PC continua a usar a rede de casa; só o jogo vai pelo telemóvel."); }
            }
            await EnsureRoutes(ips, egress, adapters);
            foreach (var ip in ips)
            {
                routeOk[ip] = Net.BestInterface(ip) == egress.Index;
                if (!routeOk[ip] && _warned.Add(ip)) Log.Warn($"O Windows não escolhe a saída para {ip}. Outra VPN pode estar a sobrepor-se.");
                if (routeOk[ip]) _warned.Remove(ip);
            }
        }
        else foreach (var ip in ips) routeOk[ip] = false;

        // 2b) Steam na mesma saída: os blocos da Valve vão pelo telemóvel antes de o CS2 abrir, e o modo jogo
        // deixa de mudar o caminho do Steam a meio do jogo (a causa do kick "No Steam logon").
        bool? steamRoutesOk = null;
        if (s.SteamOnEgress && egress != null)
        {
            await EnsurePrefixRoutes(Net.ValvePrefixes, egress, adapters);
            steamRoutesOk = Net.ValvePrefixes.All(p => Net.BestInterface(Net.ProbeIp(p)) == egress.Index);
        }
        else await RemovePrefixRoutes(Net.ValvePrefixes, adapters);

        // 3) PC inteiro (ou modo jogo): 0/1 + 128/1 ganham à rota por defeito sem a apagar (como fazem as VPN).
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
                Log.Info(s.Mode == "all" ? "Todo o PC sai pelo telemóvel." : "O CS2 está aberto: todo o PC passa pelo telemóvel.");
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

    /// <summary>Caminho rápido: a API do Windows diz se cada IP já sai pela placa. Só se faltar algum se mexe em rotas.</summary>
    async Task EnsureRoutes(List<string> ips, Adapter via, List<Adapter> adapters)
    {
        var missing = ips.Where(ip => Net.BestInterface(ip) != via.Index).ToList();
        if (missing.Count == 0) return;
        // Limpa rotas nossas que ficaram noutra placa (ex.: o cabo voltou com outro índice, ou mudou a rede Wi-Fi).
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
            else Log.Warn($"Rota {ip}: {r.Output}");
        }
    }

    /// <summary>Como EnsureRoutes, mas para blocos inteiros (ex.: os da Valve).</summary>
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
            else Log.Warn($"Rota {pfx}: {r.Output}");
        }
        if (added > 0) Log.Info($"Steam pela saída: {added} bloco(s) da Valve passam por \"{via.Name}\".");
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
        Log.Info("Steam pela saída desligado: o Steam volta à rede de casa.");
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
        Log.Info("O resto do PC volta à rede de casa.");
    }

    /// <summary>Remove rotas de servidores que saíram da lista (só as da app ou em placas de telemóvel).</summary>
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

    /// <summary>Desfaz tudo o que a app criou: regras, rotas e o modo PC inteiro.</summary>
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
            Log.Ok("Rede restaurada: regras e rotas da app removidas.");
        }
        finally { _gate.Release(); }
    });
}
