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
    public bool IsVpn { get; init; }
    public bool IsWifi { get; init; }
    /// <summary>Nome da rede Wi-Fi a que a placa está ligada (só placas Wi-Fi).</summary>
    public string Ssid { get; set; }
    public string Kind => IsPhone ? "Telemóvel (USB)" : IsWifi ? "Wi-Fi" : IsVpn ? "VPN" : "Rede";
    public string Display => $"{Name}  ·  {Description}";
    public override string ToString() => Display;
}

/// <summary>Placas de rede e a rota que o Windows escolhe para cada IP.</summary>
public static partial class Net
{
    static readonly Regex PhonePattern = new(
        "Remote NDIS|RNDIS|Internet Sharing Device|USB.*Ethernet|USB.*Network|Mobile USB|Android|Apple Mobile Device Ethernet|iPhone",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex VpnPattern = new(
        "WireGuard|WARP|Cloudflare|TAP-|Wintun|OpenVPN|Proton|Mullvad|NordLynx|Nord|Surfshark|ExpressVPN|Windscribe|Tailscale|ZeroTier|VPN",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ------------------------------------------------------------ Adaptadores (rápido, sem PowerShell)

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
                var local = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).FirstOrDefault();
                var gw = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    .Select(g => g.Address.ToString()).FirstOrDefault();
                var desc = ni.Description ?? "";
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
                    IsVpn = ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel || VpnPattern.IsMatch(desc) || VpnPattern.IsMatch(ni.Name),
                    IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                });
            }
            catch { }
        }
        if (list.Any(a => a.IsWifi && a.Up)) FillSsids(list);
        return list.OrderBy(a => a.Index).ToList();
    }

    static readonly Regex SsidLine = new(@"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex GuidLine = new(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    /// <summary>Lê a rede Wi-Fi atual de cada placa ("netsh wlan show interfaces"; o rótulo SSID não é traduzido).</summary>
    static void FillSsids(List<Adapter> list)
    {
        try
        {
            var r = Proc.Run("netsh", "wlan show interfaces", 6000).GetAwaiter().GetResult();
            // Um bloco por placa; o GUID do bloco liga-o à placa certa.
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
    /// Escolhe a saída: a placa fixada nas definições, ou o primeiro telemóvel USB ativo.
    /// Numa placa Wi-Fi a saída só vale enquanto estiver ligada ao ponto de acesso guardado: se o PC
    /// saltar para o router de casa, a mesma placa deixa de contar como saída.
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
                    ? $"O Wi-Fi do PC não está ligado ao ponto de acesso \"{fixedSsid}\"."
                    : $"O Wi-Fi do PC está ligado a \"{a.Ssid}\" e não ao ponto de acesso \"{fixedSsid}\".";
                return null;
            }
            return a;
        }
        return all.FirstOrDefault(a => a.IsPhone && a.Up && a.LocalIp != null && a.Gateway != null);
    }

    public static Adapter PickEgress(List<Adapter> all, string fixedId, string fixedSsid) => PickEgress(all, fixedId, fixedSsid, out _);

    // ------------------------------------------------------------ Rota escolhida pelo Windows

    [DllImport("iphlpapi.dll")]
    static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    public static int BestInterface(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return -1;
        var dest = BitConverter.ToUInt32(a.GetAddressBytes(), 0);
        return GetBestInterface(dest, out var idx) == 0 ? (int)idx : -1;
    }
}
