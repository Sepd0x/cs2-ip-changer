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

    public static async Task<List<Route>> ListRoutes()
    {
        var r = await Proc.Run("netsh", "interface ipv4 show route store=active");
        var list = new List<Route>();
        foreach (var line in r.Output.Split('\n'))
        {
            var m = RouteLine.Match(" " + line);
            if (m.Success) list.Add(new Route(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value));
        }
        return list;
    }

    public static async Task<ProcResult> AddRoute(string prefix, Adapter via, int metric = 1)
    {
        var hop = via.Gateway ?? "0.0.0.0";
        var r = await Proc.Run("netsh", $"interface ipv4 add route prefix={prefix} interface={via.Index} nexthop={hop} metric={metric} store=active");
        // "Already exists" (the regex also matches the Portuguese Windows text) counts as success: the real check comes next.
        if (!r.Ok && Regex.IsMatch(r.Output, "exist|existe", RegexOptions.IgnoreCase)) return new ProcResult(0, r.Output);
        return r;
    }

    public static Task<ProcResult> DeleteRoute(Route route) =>
        Proc.Run("netsh", $"interface ipv4 delete route prefix={route.Prefix} interface={route.Index} nexthop={route.NextHop} store=active");

    public static Task<ProcResult> SetInterfaceMetric(int index, int metric) =>
        Proc.Run("netsh", $"interface ipv4 set interface interface={index} metric={metric} store=active");
}
