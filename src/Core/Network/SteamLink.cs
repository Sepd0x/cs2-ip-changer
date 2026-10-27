using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>steam.exe's connections to Valve's servers, as seen in the Windows TCP table.</summary>
public sealed class SteamLink
{
    /// <summary>Local IPs steam.exe uses to talk to Valve (empty = Steam closed or not connected).</summary>
    public List<string> LocalIps { get; init; } = new();
    /// <summary>"remote:port|local port" of each connection: if none survives between two reads, Steam reconnected.</summary>
    public HashSet<string> Keys { get; init; } = new();
    public bool Connected => LocalIps.Count > 0;
    /// <summary>The table rows as Windows gives them (so they can be cut).</summary>
    public List<Net.TcpRow> Rows { get; init; } = new();
}

/// <summary>
/// The game server asks Steam whether the account is still connected. If steam.exe loses or redoes its connection
/// to Valve's servers mid-game (another adapter, another IP), Steam says no and the server kicks
/// with "No Steam logon". So Steam stays on the same connection as CS2, and the app watches the link.
/// </summary>
public static partial class Net
{
    // Valve blocks (AS32590, announced on 2026-10-27, RIPEstat): Steam login (CM), friends and relays.
    public static readonly string[] ValvePrefixes =
    {
        "45.121.184.0/22", "103.10.124.0/23", "103.28.54.0/23", "146.66.152.0/21", "155.133.224.0/19", "162.254.192.0/21",
        "185.25.180.0/22", "192.69.96.0/22", "205.196.6.0/24", "208.64.200.0/22", "208.78.164.0/22",
    };

    static readonly (uint Net, uint Mask)[] ValveNets = ValvePrefixes.Select(ParsePrefix).ToArray();

    static (uint Net, uint Mask) ParsePrefix(string prefix)
    {
        var p = prefix.Split('/');
        var bits = int.Parse(p[1]);
        var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
        return (BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(p[0]).GetAddressBytes()) & mask, mask);
    }

    public static bool IsValve(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var v = BinaryPrimitives.ReadUInt32BigEndian(a.GetAddressBytes());
        return ValveNets.Any(n => (v & n.Mask) == n.Net);
    }

    /// <summary>An IP inside the block, to ask Windows which adapter it leaves through.</summary>
    public static string ProbeIp(string prefix)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, ParsePrefix(prefix).Net + 1);
        return new IPAddress(b).ToString();
    }

    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    const int AF_INET = 2;
    const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;
    const uint MIB_TCP_STATE_ESTAB = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct TcpRow { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort; }

    [DllImport("iphlpapi.dll")]
    static extern uint SetTcpEntry(ref TcpRow row);

    const uint MIB_TCP_STATE_DELETE_TCB = 12;

    /// <summary>
    /// Cuts the connections (needs administrator). After changing the IP, Steam's old connection is dead
    /// but Steam only notices about 30 s later; once cut, it reconnects right away through the new IP.
    /// </summary>
    public static int ResetLink(SteamLink link)
    {
        var n = 0;
        foreach (var r in link.Rows)
        {
            var row = r;
            row.State = MIB_TCP_STATE_DELETE_TCB;
            if (SetTcpEntry(ref row) == 0) n++;
        }
        return n;
    }

    public static SteamLink ReadSteamLink() => ReadLink("steam", IsValve);

    /// <summary>Established TCP connections of a program to the destinations <paramref name="remoteFilter"/> accepts.</summary>
    public static SteamLink ReadLink(string process, Func<string, bool> remoteFilter)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(process)) { pids.Add((uint)p.Id); p.Dispose(); }
        var link = new SteamLink();
        if (pids.Count == 0) return link;

        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            size += 4096;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0) != 0) continue;
                var n = Marshal.ReadInt32(buf);
                // MIB_TCPROW_OWNER_PID: state, local IP, local port, remote IP, remote port, PID (6 × 4 bytes).
                for (var i = 0; i < n; i++)
                {
                    var row = buf + 4 + i * 24;
                    if ((uint)Marshal.ReadInt32(row) != MIB_TCP_STATE_ESTAB || !pids.Contains((uint)Marshal.ReadInt32(row, 20))) continue;
                    var remote = new IPAddress((uint)Marshal.ReadInt32(row, 12)).ToString();
                    if (!remoteFilter(remote)) continue;
                    var local = new IPAddress((uint)Marshal.ReadInt32(row, 4)).ToString();
                    var lport = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt16(row, 8));
                    var rport = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt16(row, 16));
                    if (!link.LocalIps.Contains(local)) link.LocalIps.Add(local);
                    link.Keys.Add($"{remote}:{rport}|{lport}");
                    link.Rows.Add(new TcpRow
                    {
                        State = MIB_TCP_STATE_ESTAB,
                        LocalAddr = (uint)Marshal.ReadInt32(row, 4), LocalPort = (uint)Marshal.ReadInt32(row, 8),
                        RemoteAddr = (uint)Marshal.ReadInt32(row, 12), RemotePort = (uint)Marshal.ReadInt32(row, 16),
                    });
                }
                return link;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return link;
    }
}
