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
    /// <summary>Windows didn't let the app read the Wi-Fi name (Windows 11 24H2 asks for Location): null Ssid means "not known", not "not connected".</summary>
    public bool SsidUnknown { get; set; }
    public string Kind => IsPhone ? Loc.T("Phone (USB)") : IsUsbNet ? Loc.T("USB network") : IsWifi ? "Wi-Fi" : IsVpn ? "VPN" : Loc.T("Network");
    public string Display => $"{Name}  ·  {Description}";
    public override string ToString() => Display;
}

/// <summary>Network adapters and the route Windows picks for each IP.</summary>
public static partial class Net
{
    // Only what phones (and 4G dongles that act like one) show up as. A laptop dock's USB-Ethernet is the home network.
    // "UsbNcm Host Device": Windows 11's own driver for phones that share over NCM instead of RNDIS (9 nov., tethering on and the app said Off).
    static readonly Regex PhonePattern = new(
        "Remote NDIS|RNDIS|Internet Sharing Device|Mobile USB|Android|Apple Mobile Device Ethernet|iPhone|UsbNcm",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static volatile string[] _phoneUsbIps = Array.Empty<string>();

    /// <summary>
    /// The phone's own USB tethering addresses, read over ADB (Adb.SharingAddresses). An adapter whose gateway is one of them is the
    /// phone whatever Windows calls it (a brand's own driver name the pattern doesn't know).
    /// </summary>
    public static void SetPhoneUsbIps(IEnumerable<string> ips) => _phoneUsbIps = (ips ?? Enumerable.Empty<string>()).Where(i => i != null).Distinct().ToArray();
    public static IReadOnlyCollection<string> PhoneUsbIps => _phoneUsbIps;

    /// <summary>
    /// An adapter is the phone's: by its name, or (not Wi-Fi) because its gateway is the phone's USB tethering address. A Wi-Fi adapter
    /// on the phone's hotspot is never taken by itself: the hotspot is picked by its name (Settings), so the app knows when the PC
    /// jumps to another network.
    /// </summary>
    public static bool IsPhoneAdapter(string description, string name, bool wifi, string gateway, IReadOnlyCollection<string> phoneUsbIps) =>
        PhonePattern.IsMatch(description ?? "") || PhonePattern.IsMatch(name ?? "")
        || !wifi && gateway != null && phoneUsbIps != null && phoneUsbIps.Contains(gateway);

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
                var wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var phone = IsPhoneAdapter(desc, ni.Name, wifi, gw, _phoneUsbIps);
                list.Add(new Adapter
                {
                    Index = v4.Index,
                    Name = ni.Name,
                    Description = desc,
                    Id = ni.Id,
                    Up = ni.OperationalStatus == OperationalStatus.Up,
                    LocalIp = local,
                    Gateway = gw,
                    IsPhone = phone,
                    IsUsbNet = !phone && (UsbNetPattern.IsMatch(desc) || UsbNetPattern.IsMatch(ni.Name)),
                    IsVpn = ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel || VpnPattern.IsMatch(desc) || VpnPattern.IsMatch(ni.Name),
                    IsWifi = wifi,
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
            var (byGuid, unreadable) = SsidsFrom(r.Output);
            foreach (var (guid, ssid) in byGuid)
            {
                var a = list.FirstOrDefault(x => x.Id.Trim('{', '}').Equals(guid, StringComparison.OrdinalIgnoreCase));
                if (a != null) a.Ssid = ssid;
            }
            // Wi-Fi is up but netsh gave no adapter at all: it refused (no Location permission), the name isn't known
            if (unreadable) foreach (var a in list.Where(x => x.IsWifi && x.Up)) a.SsidUnknown = true;
        }
        catch { }
    }

    /// <summary>
    /// "netsh wlan show interfaces": one block per adapter, linked by its GUID, with its SSID (null = not connected). Unreadable: no
    /// block with a GUID at all, as when Windows asks for the Location permission first; then no name is known.
    /// </summary>
    public static (Dictionary<string, string> ByGuid, bool Unreadable) SsidsFrom(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Regex.Split(output ?? "", @"\r?\n\s*\r?\n"))
        {
            var g = GuidLine.Match(b);
            if (!g.Success) continue;
            var ssid = SsidLine.Match(b);
            map[g.Groups[1].Value] = ssid.Success ? ssid.Groups[1].Value : null;
        }
        return (map, map.Count == 0);
    }

    /// <summary>What to do when Windows doesn't let the app read the Wi-Fi name.</summary>
    public const string SsidLocationText = "Windows doesn't let the app read the Wi-Fi name: turn on Location in Windows Settings > Privacy & security > Location.";

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
                problem = a.Ssid == null && a.SsidUnknown ? Loc.T(SsidLocationText)
                    : a.Ssid == null
                    ? Loc.F("The PC's Wi-Fi is not connected to the hotspot \"{0}\".", fixedSsid)
                    : Loc.F("The PC's Wi-Fi is connected to \"{0}\", not to the hotspot \"{1}\".", a.Ssid, fixedSsid);
                return null;
            }
            return a;
        }
        return all.FirstOrDefault(a => a.IsPhone && a.Up && a.LocalIp != null && a.Gateway != null);
    }

    public static Adapter PickEgress(List<Adapter> all, string fixedId, string fixedSsid) => PickEgress(all, fixedId, fixedSsid, out _);

    // ------------------------------------------------------------ Route picked by Windows

    /// <summary>
    /// An address on the internet the app never routes by itself: the adapter Windows picks for it is the one the rest of the PC
    /// uses. <see cref="PcProbeIpHigh"/> is one in the other half (128.0.0.0/1), so Whole PC checks both halves.
    /// </summary>
    public const string PcProbeIp = "1.1.1.1", PcProbeIpHigh = "200.1.1.1";

    [DllImport("iphlpapi.dll")]
    static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    public static int BestInterface(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return -1;
        var dest = BitConverter.ToUInt32(a.GetAddressBytes(), 0);
        return GetBestInterface(dest, out var idx) == 0 ? (int)idx : -1;
    }
}
