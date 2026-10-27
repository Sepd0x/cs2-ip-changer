using System.Net;
using System.Net.Sockets;

namespace SRT.Core;

/// <summary>/16 ranges: many admins ban a whole range, not one IP.</summary>
public static class IpRange
{
    /// <summary>"198.51.100.23" -> "198.51" (null if it isn't IPv4).</summary>
    public static string Slash16(string ip)
    {
        if (!IPAddress.TryParse(ip?.Trim() ?? "", out var a) || a.AddressFamily != AddressFamily.InterNetwork) return null;
        var b = a.GetAddressBytes();
        return $"{b[0]}.{b[1]}";
    }

    public static bool Same16(string a, string b) => Slash16(a) is string x && x == Slash16(b);

    /// <summary>The first earlier IP in the same /16 as this one, or null if the range is new.</summary>
    public static string FirstSame16(string ip, IEnumerable<string> previous) =>
        Slash16(ip) == null ? null : previous?.FirstOrDefault(p => Same16(ip, p));
}
