using System.Collections;
using System.Net;
using System.Reflection;

namespace SRT.Core;

/// <summary>
/// Firewall do Windows por COM (HNetCfg): muito mais rápido do que Get-NetFirewallRule.
///
/// Cada regra bloqueia o tráfego de saída que NÃO tenha como origem o IP local da saída protegida.
/// Assim, se o Wi-Fi do PC saltar do ponto de acesso para o router de casa (ou o cabo cair), o IP local
/// muda e a Firewall bloqueia no mesmo instante, sem esperar pela app.
/// </summary>
public static class Firewall
{
    public const string RuleName = "ServerRouteTool v9 - kill switch";
    public const string EchoRuleName = "ServerRouteTool v9 - teste de IP";
    public const string Cs2RuleName = "ServerRouteTool v9 - CS2 so pela saida";

    const int NET_FW_RULE_DIR_OUT = 2;
    const int NET_FW_ACTION_BLOCK = 0;
    const int NET_FW_IP_PROTOCOL_ANY = 256;
    const int NET_FW_PROFILE2_ALL = 0x7FFFFFFF;
    const int ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);
    const string AllIpv6 = "::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";

    static object Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true));

    static object Get(object o, string name, params object[] args) =>
        o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, args);

    static void SetP(object o, string name, object value) =>
        o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { value });

    static object Call(object o, string name, params object[] args) =>
        o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);

    /// <summary>Devolve null se o Firewall estiver ativo em todos os perfis em uso; senão, a explicação.</summary>
    public static string CheckEnabled()
    {
        try
        {
            var p = Policy();
            var current = (int)Get(p, "CurrentProfileTypes");
            var off = new List<string>();
            foreach (var (bit, name) in new[] { (1, "Domínio"), (2, "Privada"), (4, "Pública") })
                if ((current & bit) != 0 && !(bool)Get(p, "FirewallEnabled", bit)) off.Add(name);
            return off.Count == 0 ? null : "Firewall do Windows desligado no perfil " + string.Join(", ", off);
        }
        catch (Exception ex) { return "Não consegui ler o Firewall do Windows: " + ex.Message; }
    }

    /// <summary>Regra pelo nome, ou null se não existir. "Item" é um método COM (não uma propriedade).</summary>
    static object Item(object rules, string name)
    {
        try { return Call(rules, "Item", name); }
        catch (TargetInvocationException ex) when (ex.InnerException?.HResult == ERROR_FILE_NOT_FOUND) { return null; }
    }

    /// <summary>Apaga todas as regras com este nome (a 9.0 deixava duplicados). Devolve quantas.</summary>
    static int RemoveAllNamed(object rules, string name)
    {
        var n = 0;
        while (n < 100000 && Item(rules, name) != null)
        {
            Call(rules, "Remove", name);
            n++;
        }
        return n;
    }

    static uint ToUInt(IPAddress a) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(a.GetAddressBytes());

    static string ToIp(uint x)
    {
        var b = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, x);
        return new IPAddress(b).ToString();
    }

    /// <summary>Todos os IPv4 exceto <paramref name="allowed"/>, em intervalos (mais todo o IPv6).</summary>
    public static string AllExcept(string allowed)
    {
        var v = ToUInt(IPAddress.Parse(allowed));
        var parts = new List<string>();
        if (v > 0) parts.Add($"0.0.0.0-{ToIp(v - 1)}");
        if (v < uint.MaxValue) parts.Add($"{ToIp(v + 1)}-255.255.255.255");
        parts.Add(AllIpv6);
        return string.Join(",", parts);
    }

    /// <summary>
    /// Cria (ou recria) uma regra de bloqueio. <paramref name="remoteIps"/> null = qualquer destino;
    /// <paramref name="program"/> null = qualquer programa; <paramref name="allowLocalIp"/> null = bloqueia tudo.
    /// </summary>
    public static void Apply(string name, IReadOnlyCollection<string> remoteIps, string program, string allowLocalIp, string description)
    {
        var rules = Get(Policy(), "Rules");
        var old = RemoveAllNamed(rules, name);
        if (old > 1) Log.Ok($"Limpei {old - 1} regras repetidas ({name}).");
        if (remoteIps != null && remoteIps.Count == 0) return;

        var rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
        SetP(rule, "Name", name);
        SetP(rule, "Description", description);
        SetP(rule, "Grouping", "ServerRouteTool");
        SetP(rule, "Direction", NET_FW_RULE_DIR_OUT);
        SetP(rule, "Action", NET_FW_ACTION_BLOCK);
        SetP(rule, "Protocol", NET_FW_IP_PROTOCOL_ANY);
        if (program != null) SetP(rule, "ApplicationName", program);
        if (remoteIps != null) SetP(rule, "RemoteAddresses", string.Join(",", remoteIps));
        if (allowLocalIp != null) SetP(rule, "LocalAddresses", AllExcept(allowLocalIp));
        SetP(rule, "Profiles", NET_FW_PROFILE2_ALL);
        SetP(rule, "Enabled", true);
        Call(rules, "Add", rule);
    }

    /// <summary>Lê a regra de volta: ativa, de bloqueio, com os destinos e a origem permitida certos.</summary>
    public static bool Verify(string name, IReadOnlyCollection<string> remoteIps, string allowLocalIp)
    {
        try
        {
            var rule = Item(Get(Policy(), "Rules"), name);
            if (rule == null) return remoteIps != null && remoteIps.Count == 0;
            if (!(bool)Get(rule, "Enabled") || (int)Get(rule, "Action") != NET_FW_ACTION_BLOCK) return false;
            if (remoteIps != null)
            {
                var remote = ((string)Get(rule, "RemoteAddresses") ?? "").Split(',').Select(s => s.Split('/')[0].Trim()).ToHashSet();
                if (!remoteIps.All(remote.Contains)) return false;
            }
            var local = (string)Get(rule, "LocalAddresses") ?? "*";
            if (allowLocalIp == null) return local == "*";
            if (local == "*") return false;
            // A origem permitida tem de ficar de fora de todos os intervalos bloqueados.
            var a = ToUInt(IPAddress.Parse(allowLocalIp));
            foreach (var part in local.Split(','))
            {
                var ends = part.Split('-');
                if (ends.Length == 1 && IPAddress.TryParse(ends[0].Split('/')[0], out var single) &&
                    single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ToUInt(single) == a) return false;
                if (ends.Length != 2 || !IPAddress.TryParse(ends[0], out var lo) || !IPAddress.TryParse(ends[1], out var hi)) continue;
                if (lo.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                if (a >= ToUInt(lo) && a <= ToUInt(hi)) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Não consegui ler a regra {name}: " + (ex.InnerException ?? ex).Message);
            return false;
        }
    }

    public static void Remove(string name = RuleName)
    {
        try { RemoveAllNamed(Get(Policy(), "Rules"), name); }
        catch (Exception ex) { Log.Warn($"Remover {name}: " + (ex.InnerException ?? ex).Message); }
    }

    public static void RemoveAll()
    {
        foreach (var n in new[] { RuleName, EchoRuleName, Cs2RuleName }) Remove(n);
    }

    /// <summary>Remove as regras antigas da v6/v7/v8 (uma por IP e por placa).</summary>
    public static int RemoveLegacy()
    {
        var n = 0;
        try
        {
            var rules = Get(Policy(), "Rules");
            var names = new List<string>();
            foreach (var r in (IEnumerable)rules)
            {
                // Por COM, "Name" é o nome visível; as versões antigas usavam "ServerRouteTool: ..." e o grupo "ServerRouteTool v…".
                var name = (string)Get(r, "Name");
                var group = (string)Get(r, "Grouping") ?? "";
                if (name == null || name.StartsWith("ServerRouteTool v9")) continue;
                if (name.StartsWith("SRTv") || name.StartsWith("ServerRouteTool") || group.StartsWith("ServerRouteTool v6") ||
                    group.StartsWith("ServerRouteTool v7") || group.StartsWith("ServerRouteTool v8")) names.Add(name);
            }
            foreach (var name in names.Distinct()) { RemoveAllNamed(rules, name); n++; }
        }
        catch (Exception ex) { Log.Warn("Limpeza das regras antigas falhou: " + ex.Message); }
        return n;
    }
}
