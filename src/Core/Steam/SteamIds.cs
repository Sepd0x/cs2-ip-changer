using System.Globalization;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Steam account ids. A SteamID64 (7656119…, 17 digits) is the id in loginusers.vdf, in profile links and in the app's file names;
/// the account id (id32) is the SteamID64 minus <see cref="Base"/>: Steam's userdata folder and the ActiveUser in the registry.
/// </summary>
public static class SteamIds
{
    public const long Base = 76561197960265728;

    static readonly Regex Id64 = new(@"^7656119\d{10}\z", RegexOptions.Compiled);

    /// <summary>A real SteamID64. Ids become file names (avatars, sign-ins) and parts of URLs, so nothing else gets through.</summary>
    public static bool IsId64(string s) => s != null && Id64.IsMatch(s);

    /// <summary>Steam's userdata folder id (SteamID64 minus the base), or null.</summary>
    public static string Id32(string steamId64) =>
        long.TryParse(steamId64, out var id) && id > Base && id - Base <= uint.MaxValue ? (id - Base).ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>The SteamID64 of an account id (ActiveUser).</summary>
    public static string FromId32(uint accountId) => (Base + accountId).ToString(CultureInfo.InvariantCulture);
}
