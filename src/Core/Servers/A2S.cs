using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SRT.Core;

public sealed record ServerInfo(string Name, string Map, int Players, int MaxPlayers, int Bots, int PingMs, List<A2SPlayer> PlayerList);

/// <summary>One player from A2S_PLAYER. Servers only give the name, never the SteamID.</summary>
public sealed record A2SPlayer(string Name, int Score, TimeSpan Time);

/// <summary>Source A2S query (name, map, players), always from the local IP of the protected connection.</summary>
public static class A2S
{
    static readonly byte[] Header = { 0xFF, 0xFF, 0xFF, 0xFF };

    public static async Task<ServerInfo> Query(string ip, int port, string bindLocalIp, int timeoutMs = 1500)
    {
        using var udp = new UdpClient(new IPEndPoint(bindLocalIp != null ? IPAddress.Parse(bindLocalIp) : IPAddress.Any, 0));
        var ep = new IPEndPoint(IPAddress.Parse(ip), port);

        var req = Header.Concat(new byte[] { 0x54 }).Concat(Encoding.ASCII.GetBytes("Source Engine Query\0")).ToArray();
        var sw = Stopwatch.StartNew();
        var data = await Exchange(udp, ep, req, timeoutMs);
        if (data.Length >= 9 && data[4] == 0x41)
        {
            // Challenge: repeat the request with the 4 bytes received.
            req = req.Concat(data.Skip(5).Take(4)).ToArray();
            data = await Exchange(udp, ep, req, timeoutMs);
        }
        sw.Stop();
        if (data.Length < 6 || data[4] != 0x49) throw new InvalidDataException("Invalid A2S response");

        var o = 6;
        var name = CStr(data, ref o);
        var map = CStr(data, ref o);
        CStr(data, ref o); // folder
        CStr(data, ref o); // game
        if (o + 5 > data.Length) throw new InvalidDataException("Incomplete A2S response");
        o += 2; // appid
        int players = data[o], max = data[o + 1], bots = data[o + 2];

        // Null with players > 0: the server doesn't share the list (it ignores the request or answers without names).
        List<A2SPlayer> names = null;
        if (players > 0)
        {
            try { names = await QueryPlayers(udp, ep, timeoutMs); }
            catch { try { names = await QueryPlayers(udp, ep, timeoutMs * 2); } catch { } }
            if (names is { Count: 0 }) names = null;
        }
        return new ServerInfo(name, map, players, max, bots, (int)sw.ElapsedMilliseconds, names);
    }

    static async Task<List<A2SPlayer>> QueryPlayers(UdpClient udp, IPEndPoint ep, int timeoutMs)
    {
        var req = Header.Concat(new byte[] { 0x55, 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
        var data = await Exchange(udp, ep, req, timeoutMs);
        if (data.Length >= 9 && data[4] == 0x41)
        {
            req = Header.Concat(new byte[] { 0x55 }).Concat(data.Skip(5).Take(4)).ToArray();
            data = await Exchange(udp, ep, req, timeoutMs);
        }
        if (data.Length < 6 || data[4] != 0x44) return null;
        var count = data[5];
        var o = 6;
        var list = new List<(string n, int score, float t)>();
        for (var i = 0; i < count && o < data.Length; i++)
        {
            o++; // index
            var n = CStr(data, ref o);
            if (o + 8 > data.Length) break;
            var score = BitConverter.ToInt32(data, o);
            var t = BitConverter.ToSingle(data, o + 4);
            o += 8;
            if (!string.IsNullOrWhiteSpace(n)) list.Add((n, score, t));
        }
        return list.OrderByDescending(p => p.score)
            .Select(p => new A2SPlayer(p.n, p.score, TimeSpan.FromSeconds(float.IsFinite(p.t) && p.t > 0 ? p.t : 0))).ToList();
    }

    static async Task<byte[]> Exchange(UdpClient udp, IPEndPoint ep, byte[] req, int timeoutMs)
    {
        await udp.SendAsync(req, req.Length, ep);
        using var cts = new CancellationTokenSource(timeoutMs);
        var res = await udp.ReceiveAsync(cts.Token);
        return res.Buffer;
    }

    static string CStr(byte[] b, ref int o)
    {
        var s = o;
        while (o < b.Length && b[o] != 0) o++;
        var v = Encoding.UTF8.GetString(b, s, o - s);
        if (o < b.Length) o++;
        return v;
    }
}
