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

public sealed class Adapter
{
    public int Index { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public string Id { get; init; }
    public bool Up { get; init; }
    public string LocalIp { get; init; }
    public string Gateway { get; init; }
    public bool IsPhone { get; init; }
    /// <summary>USB network adapter that isn't a phone (docks, USB-Ethernet dongles): only used when picked by hand.</summary>
    public bool IsUsbNet { get; init; }
    public bool IsVpn { get; init; }
    public bool IsWifi { get; init; }
    /// <summary>Name of the Wi-Fi network the adapter is connected to (Wi-Fi adapters only).</summary>
    public string Ssid { get; set; }
    public string Kind => IsPhone ? "Phone (USB)" : IsUsbNet ? "USB network" : IsWifi ? "Wi-Fi" : IsVpn ? "VPN" : "Network";
    public string Display => $"{Name}  ·  {Description}";
    public override string ToString() => Display;
}

/// <summary>Network adapters and the route Windows picks for each IP.</summary>
public static partial class Net
{
    // Only what phones (and 4G dongles that act like one) show up as. A laptop dock's USB-Ethernet is the home network.
    static readonly Regex PhonePattern = new(
        "Remote NDIS|RNDIS|Internet Sharing Device|Mobile USB|Android|Apple Mobile Device Ethernet|iPhone",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex UsbNetPattern = new("USB.*Ethernet|USB.*Network|USB.*GbE|USB.*LAN", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool LooksLikePhone(string description) => PhonePattern.IsMatch(description ?? "");
    public static bool LooksLikeUsbNet(string description) => !LooksLikePhone(description) && UsbNetPattern.IsMatch(description ?? "");

    static readonly Regex VpnPattern = new(
        "WireGuard|WARP|Cloudflare|TAP-|Wintun|OpenVPN|Proton|Mullvad|NordLynx|Nord|Surfshark|ExpressVPN|Windscribe|Tailscale|ZeroTier|VPN",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ------------------------------------------------------------ Adapters (fast, no PowerShell)

    public static List<Adapter> Scan()
    {
        var list = new List<Adapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var props = ni.GetIPProperties();
                IPv4InterfaceProperties v4 = null;
                try { v4 = props.GetIPv4Properties(); } catch { }
                if (v4 == null) continue;
                var addr = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                var local = addr?.Address.ToString();
                var gw = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    .Select(g => g.Address.ToString()).FirstOrDefault();
                var desc = ni.Description ?? "";
                // Save mobile data makes the phone's adapter ignore its default route, and Windows then lists no gateway for it.
                // The app's routes still need it: the one in the registry (DHCP's, or set by hand) if it is on the adapter's network.
                if (gw == null && addr != null && ni.OperationalStatus == OperationalStatus.Up &&
                    (PhonePattern.IsMatch(desc) || PhonePattern.IsMatch(ni.Name) || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    gw = SavedGateway(ni.Id, v4.IsDhcpEnabled, addr.Address, addr.IPv4Mask);
                list.Add(new Adapter
                {
                    Index = v4.Index,
                    Name = ni.Name,
                    Description = desc,
                    Id = ni.Id,
                    Up = ni.OperationalStatus == OperationalStatus.Up,
                    LocalIp = local,
                    Gateway = gw,
                    IsPhone = PhonePattern.IsMatch(desc) || PhonePattern.IsMatch(ni.Name),
                    IsUsbNet = !(PhonePattern.IsMatch(desc) || PhonePattern.IsMatch(ni.Name)) && (UsbNetPattern.IsMatch(desc) || UsbNetPattern.IsMatch(ni.Name)),
                    IsVpn = ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel || VpnPattern.IsMatch(desc) || VpnPattern.IsMatch(ni.Name),
                    IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                });
            }
            catch { }
        }
        if (list.Any(a => a.IsWifi && a.Up)) FillSsids(list);
        return list.OrderBy(a => a.Index).ToList();
    }

    /// <summary>The adapter's gateway as Windows keeps it in the registry, only when it is on the adapter's own network.</summary>
    public static string SavedGateway(string id, bool dhcp, IPAddress local, IPAddress mask)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + id);
            var value = key?.GetValue(dhcp ? "DhcpDefaultGateway" : "DefaultGateway");
            var gw = (value is string[] list ? list.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)) : value as string)?.Trim();
            return SameNetwork(gw, local, mask) ? gw : null;
        }
        catch { return null; }
    }

    /// <summary>The gateway is another address on the local IP's network (a stale value from another network doesn't count).</summary>
    public static bool SameNetwork(string gateway, IPAddress local, IPAddress mask)
    {
        if (!IPAddress.TryParse(gateway, out var g) || g.AddressFamily != AddressFamily.InterNetwork || local == null || mask == null || g.Equals(local)) return false;
        var gb = g.GetAddressBytes();
        var lb = local.GetAddressBytes();
        var mb = mask.GetAddressBytes();
        if (lb.Length != 4 || mb.Length != 4 || mb.All(b => b == 0)) return false;
        for (var i = 0; i < 4; i++) if ((gb[i] & mb[i]) != (lb[i] & mb[i])) return false;
        return true;
    }

    static readonly Regex SsidLine = new(@"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex GuidLine = new(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    /// <summary>Reads the current Wi-Fi network of each adapter ("netsh wlan show interfaces"; the SSID label is not translated).</summary>
    static void FillSsids(List<Adapter> list)
    {
        try
        {
            var r = Proc.Run("netsh", "wlan show interfaces", 6000).GetAwaiter().GetResult();
            // One block per adapter; the block's GUID links it to the right adapter.
            var blocks = Regex.Split(r.Output, @"\r?\n\s*\r?\n");
            foreach (var b in blocks)
            {
                var g = GuidLine.Match(b);
                var ssid = SsidLine.Match(b);
                if (!g.Success) continue;
                var a = list.FirstOrDefault(x => x.Id.Trim('{', '}').Equals(g.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (a != null) a.Ssid = ssid.Success ? ssid.Groups[1].Value : null;
            }
        }
        catch { }
    }

    /// <summary>
    /// Picks the protected connection: the adapter pinned in Settings, or the first active USB phone.
    /// On a Wi-Fi adapter it only counts while connected to the saved hotspot: if the PC
    /// jumps to the home router, the same adapter stops counting.
    /// </summary>
    public static Adapter PickEgress(List<Adapter> all, string fixedId, string fixedSsid, out string problem)
    {
        problem = null;
        if (!string.IsNullOrEmpty(fixedId))
        {
            var a = all.FirstOrDefault(x => x.Id == fixedId && x.Up && x.LocalIp != null);
            if (a != null && a.IsWifi && !string.IsNullOrEmpty(fixedSsid) && a.Ssid != fixedSsid)
            {
                problem = a.Ssid == null
                    ? $"The PC's Wi-Fi is not connected to the hotspot \"{fixedSsid}\"."
                    : $"The PC's Wi-Fi is connected to \"{a.Ssid}\", not to the hotspot \"{fixedSsid}\".";
                return null;
            }
            return a;
        }
        return all.FirstOrDefault(a => a.IsPhone && a.Up && a.LocalIp != null && a.Gateway != null);
    }

    public static Adapter PickEgress(List<Adapter> all, string fixedId, string fixedSsid) => PickEgress(all, fixedId, fixedSsid, out _);

    // ------------------------------------------------------------ Route picked by Windows

    [DllImport("iphlpapi.dll")]
    static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    public static int BestInterface(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return -1;
        var dest = BitConverter.ToUInt32(a.GetAddressBytes(), 0);
        return GetBestInterface(dest, out var idx) == 0 ? (int)idx : -1;
    }
}
