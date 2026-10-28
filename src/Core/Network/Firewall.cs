using System.Collections;
using System.Net;
using System.Reflection;

namespace SRT.Core;

/// <summary>
/// Windows Firewall through COM (HNetCfg): much faster than Get-NetFirewallRule.
///
/// Each rule blocks outbound traffic that does NOT come from the local IP of the protected connection.
/// So if the PC's Wi-Fi jumps from the hotspot to the home router (or the cable drops), the local IP
/// changes and the Firewall blocks at that instant, without waiting for the app.
/// </summary>
public static class Firewall
{
    public const string RuleName = "ServerRouteTool v9 - kill switch";
    public const string EchoRuleName = "ServerRouteTool v9 - teste de IP";
    public const string Cs2RuleName = "ServerRouteTool v9 - CS2 so pela saida";
    // Whole PC mode: DNS only through the protected connection, and no IPv6 at all (the routes only move IPv4).
    public const string DnsUdpRuleName = "ServerRouteTool v9 - DNS UDP so pela saida";
    public const string DnsTcpRuleName = "ServerRouteTool v9 - DNS TCP so pela saida";
    public const string Ipv6RuleName = "ServerRouteTool v9 - IPv6 bloqueado";

    const int NET_FW_RULE_DIR_OUT = 2;
    const int NET_FW_ACTION_BLOCK = 0;
    public const int NET_FW_IP_PROTOCOL_ANY = 256, NET_FW_IP_PROTOCOL_TCP = 6, NET_FW_IP_PROTOCOL_UDP = 17;
    const int NET_FW_PROFILE2_ALL = 0x7FFFFFFF;
    const int ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);
    public const string AllIpv6 = "::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";

    /// <summary>
    /// One outbound block rule. <see cref="RemoteIps"/> null = any destination; <see cref="Program"/> null = any program;
    /// <see cref="AllowLocalIp"/> null = blocks every source. <see cref="LocalAddresses"/>, when given, is the exact source list instead.
    /// </summary>
    public sealed record FwRule(string Name, IReadOnlyCollection<string> RemoteIps, string Program, string AllowLocalIp, string Description,
        int Protocol = NET_FW_IP_PROTOCOL_ANY, string RemotePorts = null, string LocalAddresses = null);

    static object Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true));

    static object Get(object o, string name, params object[] args) =>
        o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, args);

    static void SetP(object o, string name, object value) =>
        o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { value });

    static object Call(object o, string name, params object[] args) =>
        o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);

    /// <summary>Returns null if the Firewall is on in every profile in use; otherwise, the explanation.</summary>
    public static string CheckEnabled()
    {
        try
        {
            var p = Policy();
            var current = (int)Get(p, "CurrentProfileTypes");
            var off = new List<string>();
            foreach (var (bit, name) in new[] { (1, "Domain"), (2, "Private"), (4, "Public") })
                if ((current & bit) != 0 && !(bool)Get(p, "FirewallEnabled", bit)) off.Add(name);
            return off.Count == 0 ? null : "Windows Firewall is off for the profile: " + string.Join(", ", off);
        }
        catch (Exception ex) { return "Could not read Windows Firewall: " + ex.Message; }
    }

    /// <summary>Rule by name, or null if it does not exist. "Item" is a COM method (not a property).</summary>
    static object Item(object rules, string name)
    {
        try { return Call(rules, "Item", name); }
        catch (TargetInvocationException ex) when (ex.InnerException?.HResult == ERROR_FILE_NOT_FOUND) { return null; }
    }

    /// <summary>Deletes every rule with this name (9.0 left duplicates). Returns how many.</summary>
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

    /// <summary>Every IPv4 except <paramref name="allowed"/>, as ranges (plus all of IPv6).</summary>
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
    /// Creates (or recreates) a block rule. <paramref name="remoteIps"/> null = any destination;
    /// <paramref name="program"/> null = any program; <paramref name="allowLocalIp"/> null = blocks everything.
    /// The new rule goes in under a temporary name before the old one is removed, so there is never a moment without a block.
    /// </summary>
    public static void Apply(string name, IReadOnlyCollection<string> remoteIps, string program, string allowLocalIp, string description) =>
        Apply(new FwRule(name, remoteIps, program, allowLocalIp, description));

    public static void Apply(FwRule r)
    {
        var name = r.Name;
        var rules = Get(Policy(), "Rules");
        if (r.RemoteIps != null && r.RemoteIps.Count == 0) { RemoveAllNamed(rules, name); return; }

        // A unique temporary name: if the rename at the end ever fails, that rule keeps blocking until the next Apply.
        var temp = $"{name} (updating {Guid.NewGuid():N})"[..(name.Length + 20)] + ")";
        var rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
        SetP(rule, "Name", temp);
        SetP(rule, "Description", r.Description);
        SetP(rule, "Grouping", "ServerRouteTool");
        SetP(rule, "Direction", NET_FW_RULE_DIR_OUT);
        SetP(rule, "Action", NET_FW_ACTION_BLOCK);
        SetP(rule, "Protocol", r.Protocol);   // before the ports: COM refuses ports on "any protocol"
        if (r.RemotePorts != null) SetP(rule, "RemotePorts", r.RemotePorts);
        if (r.Program != null) SetP(rule, "ApplicationName", r.Program);
        if (r.RemoteIps != null) SetP(rule, "RemoteAddresses", string.Join(",", r.RemoteIps));
        if (r.LocalAddresses != null) SetP(rule, "LocalAddresses", r.LocalAddresses);
        else if (r.AllowLocalIp != null) SetP(rule, "LocalAddresses", AllExcept(r.AllowLocalIp));
        SetP(rule, "Profiles", NET_FW_PROFILE2_ALL);
        SetP(rule, "Enabled", true);
        Call(rules, "Add", rule);

        var old = RemoveAllNamed(rules, name);
        if (old > 1) Log.Ok($"Removed {old - 1} duplicate rules ({name}).");
        RemoveTemps(rules, name, keep: temp);
        var added = Item(rules, temp);
        if (added != null) SetP(added, "Name", name);
    }

    /// <summary>Removes leftovers of an interrupted <see cref="Apply"/> (except <paramref name="keep"/>).</summary>
    static void RemoveTemps(object rules, string name, string keep = null)
    {
        var names = new List<string>();
        foreach (var r in (IEnumerable)rules)
            if ((string)Get(r, "Name") is string n && n.StartsWith(name + " (updating") && n != keep) names.Add(n);
        foreach (var n in names.Distinct()) RemoveAllNamed(rules, n);
    }

    /// <summary>
    /// Reads the rule back and compares every field that matters with what <see cref="Apply"/> sets:
    /// outbound, block, the protocol and ports, every profile and interface, the program, the destinations,
    /// and a source filter that blocks every local IPv4 and IPv6 address except the allowed one (or the exact list asked for).
    /// </summary>
    public static bool Verify(string name, IReadOnlyCollection<string> remoteIps, string allowLocalIp, string program = null) =>
        Verify(new FwRule(name, remoteIps, program, allowLocalIp, null));

    public static bool Verify(FwRule r)
    {
        var (name, remoteIps, allowLocalIp, program) = (r.Name, r.RemoteIps, r.AllowLocalIp, r.Program);
        try
        {
            var rules = Get(Policy(), "Rules");
            var rule = Item(rules, name);
            if (rule == null) return remoteIps != null && remoteIps.Count == 0;
            if (CountNamed(rules, name) != 1) return false;
            if (!(bool)Get(rule, "Enabled") || (int)Get(rule, "Action") != NET_FW_ACTION_BLOCK) return false;
            if ((int)Get(rule, "Direction") != NET_FW_RULE_DIR_OUT) return false;
            if ((int)Get(rule, "Protocol") != r.Protocol) return false;
            if (r.Protocol is NET_FW_IP_PROTOCOL_TCP or NET_FW_IP_PROTOCOL_UDP)
            {
                // A port added by hand on either side would only block part of the traffic.
                if (!IsAny((string)Get(rule, "LocalPorts"), "*")) return false;
                var ports = (string)Get(rule, "RemotePorts");
                if (r.RemotePorts == null ? !IsAny(ports, "*") : ports?.Replace(" ", "") != r.RemotePorts) return false;
            }
            if (((int)Get(rule, "Profiles") & 7) != 7) return false;
            if (!IsAny((string)Get(rule, "InterfaceTypes"), "All")) return false;
            if (Get(rule, "Interfaces") is Array ifs && ifs.Length > 0) return false;
            if ((string)Get(rule, "ServiceName") is string svc && svc.Length > 0 && svc != "*") return false;

            var app = (string)Get(rule, "ApplicationName");
            if (program == null ? !string.IsNullOrEmpty(app) : !SamePath(app, program)) return false;

            var remote = (string)Get(rule, "RemoteAddresses") ?? "*";
            if (remoteIps == null) { if (!IsAny(remote, "*")) return false; }
            else
            {
                // Extra destinations only block more; a missing one is a hole. Windows may merge neighbours into a range.
                var parts = remote.Split(',').Select(x => x.Trim()).ToList();
                if (!remoteIps.All(ip => parts.Any(p => Covers(p, ip)))) return false;
            }

            var local = (string)Get(rule, "LocalAddresses") ?? "*";
            if (r.LocalAddresses == AllIpv6) return CoversAllIpv6(local);
            if (r.LocalAddresses != null) return string.Equals(local, r.LocalAddresses, StringComparison.OrdinalIgnoreCase);
            if (allowLocalIp == null) return IsAny(local, "*");
            return CoversAllExcept(local, allowLocalIp);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read rule {name}: " + (ex.InnerException ?? ex).Message);
            return false;
        }
    }

    static bool IsAny(string value, string any) => string.IsNullOrEmpty(value) || value == "*" || string.Equals(value, any, StringComparison.OrdinalIgnoreCase);

    static bool SamePath(string a, string b)
    {
        static string N(string p) { try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p)); } catch { return p; } }
        return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if one RemoteAddresses entry (IP, IP/mask or range) contains <paramref name="ip"/>.</summary>
    static bool Covers(string part, string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var a = ToUInt(addr);
        var ends = part.Split('-');
        if (ends.Length == 2) return IPAddress.TryParse(ends[0], out var lo) && IPAddress.TryParse(ends[1], out var hi) &&
            lo.AddressFamily == addr.AddressFamily && a >= ToUInt(lo) && a <= ToUInt(hi);
        var bits = part.Split('/');
        if (!IPAddress.TryParse(bits[0], out var net) || net.AddressFamily != addr.AddressFamily) return false;
        if (bits.Length == 1) return ToUInt(net) == a;
        var prefix = bits[1].Contains('.') ? System.Numerics.BitOperations.PopCount(ToUInt(IPAddress.Parse(bits[1]))) : int.Parse(bits[1]);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return (ToUInt(net) & mask) == (a & mask);
    }

    static int CountNamed(object rules, string name)
    {
        var n = 0;
        foreach (var r in (IEnumerable)rules) if ((string)Get(r, "Name") == name) n++;
        return n;
    }

    /// <summary>
    /// True if the blocked source list covers every IPv4 address except <paramref name="allowed"/>, and all of IPv6.
    /// Windows may rewrite the ranges (single IPs, masks), so the coverage is computed instead of comparing text.
    /// </summary>
    public static bool CoversAllExcept(string local, string allowed)
    {
        var a = ToUInt(IPAddress.Parse(allowed));
        var v4 = new List<(ulong Lo, ulong Hi)>();
        var v6All = false;
        foreach (var raw in local.Split(','))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;
            if (part.Contains(':')) { v6All |= IsAllIpv6(part); continue; }
            var ends = part.Split('-');
            if (ends.Length == 2 && IPAddress.TryParse(ends[0], out var lo) && IPAddress.TryParse(ends[1], out var hi))
                v4.Add((ToUInt(lo), ToUInt(hi)));
            else if (ends.Length == 1 && part.Contains('/') && IPAddress.TryParse(part.Split('/')[0], out var net))
            {
                var mask = part.Split('/')[1];
                int bits = mask.Contains('.') ? System.Numerics.BitOperations.PopCount(ToUInt(IPAddress.Parse(mask))) : int.Parse(mask);
                var size = bits == 0 ? 1UL << 32 : 1UL << (32 - bits);
                var start = ToUInt(net) & ~(uint)(size - 1);
                v4.Add((start, start + size - 1));
            }
            else if (ends.Length == 1 && IPAddress.TryParse(part, out var single)) v4.Add((ToUInt(single), ToUInt(single)));
            else return false;
        }
        if (!v6All) return false;
        if (v4.Any(r => a >= r.Lo && a <= r.Hi)) return false;
        // Walks the sorted ranges: everything from 0 to 2^32-1 must be covered, except the allowed IP.
        ulong next = 0;
        foreach (var r in v4.OrderBy(r => r.Lo))
        {
            if (r.Lo > next && !(next == a && r.Lo == (ulong)a + 1)) return false;
            next = Math.Max(next, r.Hi + 1);
        }
        return next == (ulong)a ? a == uint.MaxValue : next >= 1UL << 32;
    }

    /// <summary>True if the source list blocks all of IPv6 ("::/0" or the full range, however Windows writes it).</summary>
    public static bool CoversAllIpv6(string local) => (local ?? "").Split(',').Any(p => IsAllIpv6(p.Trim()));

    static bool IsAllIpv6(string part)
    {
        if (part == "::/0") return true;
        var e6 = part.Split('-');
        return e6.Length == 2 && IPAddress.TryParse(e6[0], out var l6) && IPAddress.TryParse(e6[1], out var h6) &&
            l6.Equals(IPAddress.IPv6Any) && h6.Equals(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"));
    }

    public static void Remove(string name = RuleName)
    {
        try { var rules = Get(Policy(), "Rules"); RemoveAllNamed(rules, name); RemoveTemps(rules, name); }
        catch (Exception ex) { Log.Warn($"Remove {name}: " + (ex.InnerException ?? ex).Message); }
    }

    public static void RemoveAll()
    {
        foreach (var n in new[] { RuleName, EchoRuleName, Cs2RuleName, DnsUdpRuleName, DnsTcpRuleName, Ipv6RuleName }) Remove(n);
    }

    /// <summary>Names of the app's rules still in the Firewall (current ones and leftovers of an interrupted update).</summary>
    public static List<string> Remaining()
    {
        var left = new List<string>();
        try
        {
            foreach (var r in (IEnumerable)Get(Policy(), "Rules"))
                if ((string)Get(r, "Name") is string n && n.StartsWith("ServerRouteTool v9")) left.Add(n);
        }
        catch (Exception ex) { left.Add("(could not read the Firewall: " + ex.Message + ")"); }
        return left;
    }

    /// <summary>Removes the old v6/v7/v8 rules (one per IP and per adapter).</summary>
    public static int RemoveLegacy()
    {
        var n = 0;
        try
        {
            var rules = Get(Policy(), "Rules");
            var names = new List<string>();
            foreach (var r in (IEnumerable)rules)
            {
                // Over COM, "Name" is the display name; old versions used "ServerRouteTool: ..." and the group "ServerRouteTool v…".
                var name = (string)Get(r, "Name");
                var group = (string)Get(r, "Grouping") ?? "";
                if (name == null || name.StartsWith("ServerRouteTool v9")) continue;
                if (name.StartsWith("SRTv") || name.StartsWith("ServerRouteTool") || group.StartsWith("ServerRouteTool v6") ||
                    group.StartsWith("ServerRouteTool v7") || group.StartsWith("ServerRouteTool v8")) names.Add(name);
            }
            foreach (var name in names.Distinct()) { RemoveAllNamed(rules, name); n++; }
        }
        catch (Exception ex) { Log.Warn("Cleaning up old rules failed: " + ex.Message); }
        return n;
    }
}
