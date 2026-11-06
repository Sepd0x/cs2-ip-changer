using System.Collections;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SRT.Core;

public static partial class Net
{
    // ------------------------------------------------------------ Routes (netsh, store=active: they disappear on reboot)

    public sealed record Route(string Prefix, int Index, string NextHop);

    static readonly Regex RouteLine = new(@"\s(\d{1,3}(?:\.\d{1,3}){3}/\d{1,2})\s+(\d+)\s+(\S+)", RegexOptions.Compiled);

    /// <summary>The route table, or null when netsh failed or timed out: a real table always has lines (127.0.0.0/8 on Loopback).</summary>
    public static async Task<List<Route>> ListRoutes() => ParseRoutes(await Proc.Run("netsh", "interface ipv4 show route store=active"));

    /// <summary>netsh's output as routes; null when it didn't answer or gave no route at all (not known, never "no routes").</summary>
    public static List<Route> ParseRoutes(ProcResult r)
    {
        if (r == null || !r.Ok) return null;
        var list = new List<Route>();
        foreach (var line in (r.Output ?? "").Split('\n'))
            if (ParseRouteLine(line) is Route rt) list.Add(rt);
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// One line of the table. An on-link route shows the adapter's name where the gateway goes ("Wi-Fi", "Ethernet 3"): its next hop
    /// is 0.0.0.0, as the app writes it (RouteKey.Of) and as netsh deletes it.
    /// </summary>
    public static Route ParseRouteLine(string line)
    {
        var m = RouteLine.Match(" " + line);
        if (!m.Success) return null;
        var hop = m.Groups[3].Value;
        var ip = IPAddress.TryParse(hop, out var a) && a.AddressFamily == AddressFamily.InterNetwork && a.ToString() == hop;
        return new Route(m.Groups[1].Value, int.Parse(m.Groups[2].Value), ip ? hop : "0.0.0.0");
    }

    public static async Task<ProcResult> AddRoute(string prefix, Adapter via, int metric = 1)
    {
        var hop = via.Gateway ?? "0.0.0.0";
        var r = await Proc.Run("netsh", $"interface ipv4 add route prefix={prefix} interface={via.Index} nexthop={hop} metric={metric} store=active");
        // "Already exists" counts as success. netsh says it in the Windows language, so the route table is read instead of the text.
        if (!r.Ok && (await ListRoutes())?.Any(rt => rt.Prefix == prefix && rt.Index == via.Index && (via.Gateway == null || rt.NextHop == hop)) == true) return new ProcResult(0, r.Output);
        return r;
    }

    public static Task<ProcResult> DeleteRoute(Route route) =>
        Proc.Run("netsh", $"interface ipv4 delete route prefix={route.Prefix} interface={route.Index} nexthop={route.NextHop} store=active");
}
