using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Servers of the same community usually share one ban list: a ban on one counts on all of them.
/// Same network = same IP (any port), or the same tag at the start of the name ("[PT] WINDOH.GG • Competitivo #5" -> "WINDOH.GG").
/// </summary>
public static class ServerNetwork
{
    static readonly Regex Prefixes = new(@"^\s*((\[[^\]]{1,12}\]|\([^)]{1,12}\))\s*)+", RegexOptions.Compiled);
    static readonly Regex Head = new(@"^\s*([^•|#\-–—:\[\]()]+)", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex TrailingNumber = new(@"\s*\d+$", RegexOptions.Compiled);

    /// <summary>The community tag of a server name, upper case. Null when there's no clear one.</summary>
    public static string Tag(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = Head.Match(Prefixes.Replace(name, ""));
        if (!m.Success) return null;
        var tag = TrailingNumber.Replace(Spaces.Replace(m.Groups[1].Value.Trim(), " "), "");
        return tag.Length >= 3 ? tag.ToUpperInvariant() : null;
    }

    public static string Ip(string key) => key?.Split(':')[0];

    /// <summary>Same IP, or both names carry the same tag.</summary>
    public static bool Same(string keyA, string nameA, string keyB, string nameB)
    {
        if (keyA == null || keyB == null) return false;
        if (Ip(keyA) == Ip(keyB)) return true;
        var a = Tag(nameA);
        return a != null && a == Tag(nameB);
    }
}
