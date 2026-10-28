using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Servers of the same community usually share one ban list: a ban on one counts on all of them.
/// Same network = the community name you set on both, or the same tag at the start of the name
/// ("[EU] EXAMPLE.GG • Retakes #5" -> "EXAMPLE.GG"). Generic tags ("Retakes", "Mirage 24/7") don't count, and the same IP
/// only counts while one of the names is still unknown: shared hosting puts different communities on one IP.
/// </summary>
public static class ServerNetwork
{
    static readonly Regex Prefixes = new(@"^\s*((\[[^\]]{1,12}\]|\([^)]{1,12}\))\s*)+", RegexOptions.Compiled);
    static readonly Regex Head = new(@"^\s*([^•|#\-–—:\[\]()]+)", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex TrailingNumber = new(@"\s*\d+$", RegexOptions.Compiled);
    static readonly Regex TrailingAllDay = new(@"\s*24\s*/\s*7$", RegexOptions.Compiled);
    static readonly Regex Words = new(@"[\s._]+", RegexOptions.Compiled);

    // Game modes, maps and words every community uses: a tag made only of these says nothing about who runs the server.
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "RETAKE", "RETAKES", "SURF", "DM", "DEATHMATCH", "FFA", "1V1", "ARENA", "ARENAS", "AWP", "KZ", "BHOP", "HVH", "CASUAL", "COMPETITIVE",
        "COMPETITIVO", "PUBLIC", "PUB", "PRIME", "VIP", "FASTDL", "MIRAGE", "DUST2", "DUST", "INFERNO", "NUKE", "ANCIENT", "ANUBIS",
        "VERTIGO", "OVERPASS", "TRAIN", "OFFICE", "CACHE", "ONLY", "TICK", "SERVER",
    };

    /// <summary>The community tag of a server name, upper case. Null when there's no clear one (or only generic words).</summary>
    public static string Tag(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = Head.Match(Prefixes.Replace(name, ""));
        if (!m.Success) return null;
        var tag = Spaces.Replace(m.Groups[1].Value.Trim(), " ");
        tag = TrailingNumber.Replace(TrailingAllDay.Replace(tag, ""), "").Trim();
        if (tag.Length < 3 || IsGeneric(tag)) return null;
        return tag.ToUpperInvariant();
    }

    static bool IsGeneric(string tag) =>
        Words.Split(tag).Where(w => w.Length > 0).All(w => Generic.Contains(w) || w.All(char.IsDigit) || Regex.IsMatch(w, @"^\d+(TICK|HZ)$", RegexOptions.IgnoreCase));

    public static string Ip(string key) => key?.Split(':')[0];

    /// <summary>The community name you set, cleaned up (null = none).</summary>
    public static string Clean(string network) => string.IsNullOrWhiteSpace(network) ? null : Spaces.Replace(network.Trim(), " ");

    /// <summary>
    /// Same network: the names you set (or else the tags) are equal. With no label on one side, only the same IP counts,
    /// and only while one of the two names is still unknown (the server hasn't answered yet).
    /// </summary>
    public static bool Same(string keyA, string nameA, string netA, string keyB, string nameB, string netB)
    {
        if (keyA == null || keyB == null) return false;
        var a = Clean(netA) ?? Tag(nameA);
        var b = Clean(netB) ?? Tag(nameB);
        if (a != null && b != null) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB)) && Ip(keyA) == Ip(keyB);
    }
}
