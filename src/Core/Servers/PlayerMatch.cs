using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>Matching flagged players to the names a server shares, and reading a Steam profile link.</summary>
public static class PlayerMatch
{
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex Id64 = new(@"^7656119\d{10}$", RegexOptions.Compiled);
    static readonly Regex ProfileUrl = new(@"steamcommunity\.com/profiles/(7656119\d{10})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex VanityUrl = new(@"steamcommunity\.com/id/([A-Za-z0-9_-]{2,32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex VanityName = new(@"^[A-Za-z0-9_-]{2,32}$", RegexOptions.Compiled);

    /// <summary>Same name for A2S: trimmed, spaces collapsed, case ignored. Anyone can pick the same name.</summary>
    public static bool SameName(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    static string Norm(string s) => Spaces.Replace(s.Trim(), " ");

    /// <summary>A SteamID64, a /profiles/ link, a /id/ link or a bare custom URL name (with a letter, so a typo'd number isn't one). Returns (steamId, vanity); both null if it isn't one.</summary>
    public static (string SteamId, string Vanity) ParseProfile(string input)
    {
        var t = (input ?? "").Trim();
        if (t.Length == 0) return (null, null);
        if (Id64.IsMatch(t)) return (t, null);
        if (ProfileUrl.Match(t) is { Success: true } p) return (p.Groups[1].Value, null);
        if (VanityUrl.Match(t) is { Success: true } v) return (null, v.Groups[1].Value);
        if (!t.Contains('/') && !t.Contains('.') && VanityName.IsMatch(t) && t.Any(char.IsLetter)) return (null, t);
        return (null, null);
    }

    /// <summary>"203.0.113.10:27015" from the profile's game server, if it's a plain IPv4:port.</summary>
    public static string ServerKey(string gameServerIp)
    {
        var m = Regex.Match(gameServerIp ?? "", @"^(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})$");
        return m.Success ? $"{m.Groups[1].Value}:{int.Parse(m.Groups[2].Value)}" : null;
    }
}
