using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>As ligações do steam.exe aos servidores da Valve, vistas pela tabela TCP do Windows.</summary>
public sealed class SteamLink
{
    /// <summary>IPs locais por onde o steam.exe fala com a Valve (vazio = Steam fechado ou sem ligação).</summary>
    public List<string> LocalIps { get; init; } = new();
    /// <summary>"remoto:porta|porta local" de cada ligação: se nenhuma sobrevive entre duas leituras, o Steam religou-se.</summary>
    public HashSet<string> Keys { get; init; } = new();
    public bool Connected => LocalIps.Count > 0;
    /// <summary>As linhas da tabela tal como o Windows as dá (para as poder cortar).</summary>
    public List<Net.TcpRow> Rows { get; init; } = new();
}

/// <summary>
/// O servidor de jogo pergunta ao Steam se a conta continua ligada. Se o steam.exe perder ou refizer a ligação
/// aos servidores da Valve a meio do jogo (outra placa, outro IP), o Steam responde que não e o servidor dá
/// kick com "No Steam logon". Por isso o Steam fica na mesma saída que o CS2, e a app vigia a ligação.
/// </summary>
public static partial class Net
{
    // Blocos da Valve (AS32590, anunciados a 27 out. 2026, RIPEstat): login do Steam (CM), amigos e relays.
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

    /// <summary>Um IP dentro do bloco, para perguntar ao Windows por que placa sai.</summary>
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
    /// Corta as ligações (precisa de administrador). Depois de trocar o IP, a ligação antiga do Steam fica
    /// morta mas o Steam só o percebe uns 30 s depois; cortada, religa-se logo pelo IP novo.
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

    /// <summary>Ligações TCP estabelecidas de um programa para os destinos que <paramref name="remoteFilter"/> aceita.</summary>
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
                // MIB_TCPROW_OWNER_PID: estado, IP local, porta local, IP remoto, porta remota, PID (6 × 4 bytes).
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
