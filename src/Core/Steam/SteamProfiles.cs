using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SRT.Core;

public static partial class Steam
{
    // ------------------------------------------------------------ Public account status

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CS2IPChanger/0.1");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return h;
    }

    static readonly Regex VacRe = new(@"(\d+|Multiple)\s+VAC\s+bans?\s+on\s+record", RegexOptions.IgnoreCase);
    static readonly Regex GameBanRe = new(@"(\d+|Multiple)\s+game\s+bans?\s+on\s+record", RegexOptions.IgnoreCase);
    static readonly Regex DaysRe = new(@"(\d+)\s+day\(s\)\s+since\s+last\s+ban", RegexOptions.IgnoreCase);
    static readonly Regex LevelRe = new(@"friendPlayerLevelNum""?>\s*(\d+)\s*<", RegexOptions.IgnoreCase);
    static readonly Regex CommunityBanRe = new(@"profile_ban_status[^>]*>[^<]*community ban|This account has been banned from the Steam Community", RegexOptions.IgnoreCase);

    static int BanCount(Match m) => !m.Success ? 0 : m.Groups[1].Value.Equals("Multiple", StringComparison.OrdinalIgnoreCase) ? 2 : int.Parse(m.Groups[1].Value);

    /// <summary>Public profile: XML (no key) + profile page (bans, level). With a Web API key, bans come from the official API.</summary>
    public static async Task<SteamProfile> FetchProfile(string steamId, string apiKey)
    {
        var p = new SteamProfile { Checked = DateTime.Now };
        var xml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/?xml=1");
        var x = XDocument.Parse(xml).Root;
        if (x == null || x.Element("error") != null) throw new InvalidDataException("Profile not found");
        string E(string n) => x.Element(n)?.Value?.Trim();
        p.Persona = E("steamID");
        p.AvatarUrl = E("avatarFull");
        p.OnlineState = E("onlineState");
        p.Private = E("privacyState") != "public";
        p.Limited = E("isLimitedAccount") == "1";
        p.VacBans = E("vacBanned") == "1" ? 1 : 0;
        p.TradeBan = E("tradeBanState") is string tb && tb != "None" ? tb : null;
        p.MemberSince = E("memberSince");
        p.Country = E("location");
        var cs = x.Element("mostPlayedGames")?.Elements("mostPlayedGame")
            .FirstOrDefault(g => (g.Element("gameName")?.Value ?? "").Contains("Counter-Strike"));
        if (cs != null && double.TryParse(cs.Element("hoursOnRecord")?.Value?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
            p.Cs2Hours = h;

        try
        {
            var html = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/");
            var vac = BanCount(VacRe.Match(html));
            if (vac > 0) p.VacBans = vac;
            p.GameBans = BanCount(GameBanRe.Match(html));
            var d = DaysRe.Match(html);
            if (d.Success) p.DaysSinceLastBan = int.Parse(d.Groups[1].Value);
            var lv = LevelRe.Match(html);
            if (lv.Success) p.Level = int.Parse(lv.Groups[1].Value);
            p.CommunityBan = CommunityBanRe.IsMatch(html);
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var json = await Http.GetStringAsync($"https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/?key={apiKey.Trim()}&steamids={steamId}");
                using var doc = JsonDocument.Parse(json);
                var pl = doc.RootElement.GetProperty("players");
                if (pl.GetArrayLength() > 0)
                {
                    var b = pl[0];
                    p.VacBans = b.GetProperty("NumberOfVACBans").GetInt32();
                    p.GameBans = b.GetProperty("NumberOfGameBans").GetInt32();
                    p.CommunityBan = b.GetProperty("CommunityBanned").GetBoolean();
                    var days = b.GetProperty("DaysSinceLastBan").GetInt32();
                    p.DaysSinceLastBan = p.VacBans + p.GameBans > 0 ? days : null;
                    var eco = b.GetProperty("EconomyBan").GetString();
                    p.TradeBan = eco == "none" ? null : eco;
                }
            }
            catch (Exception ex) { Log.Warn("Steam Web API: " + ex.Message); }
        }
        return p;
    }

    // ------------------------------------------------------------ Other players (flags)

    /// <summary>Current name and game server of a profile. GameServer is only there when the profile's game details are public.</summary>
    public sealed record Summary(string Persona, string GameServer);

    /// <summary>SteamID64 from a link, an id or a custom URL name. The custom URL needs the API key or the public XML.</summary>
    public static async Task<string> ResolveProfile(string input, string apiKey)
    {
        var (id, vanity) = PlayerMatch.ParseProfile(input);
        if (id != null || vanity == null) return id;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var json = await Http.GetStringAsync($"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={apiKey.Trim()}&vanityurl={Uri.EscapeDataString(vanity)}");
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement.GetProperty("response");
                if (r.TryGetProperty("steamid", out var sid)) return sid.GetString();
            }
            catch (Exception ex) { Log.Warn("Steam Web API: " + ex.Message); }
        }
        var xml = await Http.GetStringAsync($"https://steamcommunity.com/id/{Uri.EscapeDataString(vanity)}/?xml=1");
        var x = XDocument.Parse(xml).Root;
        return x?.Element("error") == null ? x?.Element("steamID64")?.Value?.Trim() : null;
    }

    /// <summary>With a key: one GetPlayerSummaries call for up to 100 profiles. Without: the public XML, one profile at a time.</summary>
    public static async Task<Dictionary<string, Summary>> FetchSummaries(IReadOnlyCollection<string> ids, string apiKey)
    {
        var res = new Dictionary<string, Summary>();
        if (ids.Count == 0) return res;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            foreach (var chunk in ids.Chunk(100))
            {
                var json = await Http.GetStringAsync($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={apiKey.Trim()}&steamids={string.Join(",", chunk)}");
                using var doc = JsonDocument.Parse(json);
                foreach (var p in doc.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
                {
                    string S(string n) => p.TryGetProperty(n, out var v) ? v.GetString() : null;
                    res[S("steamid")] = new Summary(S("personaname"), S("gameserverip"));
                }
            }
            return res;
        }
        foreach (var id in ids)
        {
            try
            {
                var x = XDocument.Parse(await Http.GetStringAsync($"https://steamcommunity.com/profiles/{id}/?xml=1")).Root;
                if (x == null || x.Element("error") != null) continue;
                res[id] = new Summary(x.Element("steamID")?.Value?.Trim(), x.Element("inGameServerIP")?.Value?.Trim());
            }
            catch { }
        }
        return res;
    }

    public static async Task<string> DownloadAvatar(string steamId, string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var path = Path.Combine(Paths.Avatars, steamId + ".jpg");
        var bytes = await Http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}
