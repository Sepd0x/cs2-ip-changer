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
    /// </summary>
    public static void Apply(string name, IReadOnlyCollection<string> remoteIps, string program, string allowLocalIp, string description)
    {
        var rules = Get(Policy(), "Rules");
        var old = RemoveAllNamed(rules, name);
        if (old > 1) Log.Ok($"Removed {old - 1} duplicate rules ({name}).");
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

    /// <summary>Reads the rule back: enabled, a block rule, with the right destinations and allowed source.</summary>
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
            // The allowed source must fall outside every blocked range.
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
            Log.Warn($"Could not read rule {name}: " + (ex.InnerException ?? ex).Message);
            return false;
        }
    }

    public static void Remove(string name = RuleName)
    {
        try { RemoveAllNamed(Get(Policy(), "Rules"), name); }
        catch (Exception ex) { Log.Warn($"Remove {name}: " + (ex.InnerException ?? ex).Message); }
    }

    public static void RemoveAll()
    {
        foreach (var n in new[] { RuleName, EchoRuleName, Cs2RuleName }) Remove(n);
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
