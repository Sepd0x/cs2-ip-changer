using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SRT.Core;

/// <summary>One Valve relay: where CS2 sends its pings and, through it, its game traffic.</summary>
public sealed record Relay(string Ip, int PortLo, int PortHi);

/// <summary>A point of presence in Valve's SDR list ("fra" = Frankfurt). Pops without relays are reached through others.</summary>
public sealed record Pop(string Code, string Desc, double Lon, double Lat, IReadOnlyList<Relay> Relays);

/// <summary>Valve's relay list for CS2, as read from the API or from the copy saved on disk.</summary>
public sealed class SdrConfig
{
    public long Revision { get; init; }
    public IReadOnlyList<Pop> Pops { get; init; } = Array.Empty<Pop>();
    public DateTime FetchedAt { get; init; }
    /// <summary>The API didn't answer: this is the last list saved on this PC.</summary>
    public bool FromCache { get; init; }
}

/// <summary>
/// One region on the Regions page: a city, with every pop Valve has there (Frankfurt = "fra" + "dfra").
/// Turning a region off blocks all of them, so CS2 can't reach the city through a partner's relay either.
/// </summary>
public sealed record RegionInfo(string Id, string City, string Area, string Country, string Continent, IReadOnlyList<string> Codes, IReadOnlyList<string> RelayIps);

/// <summary>
/// CS2 matchmaking (Premier, Competitive, Casual, Deathmatch, Wingman) picks a server by ping to Valve's relays (SDR).
/// A region whose relays cs2.exe can't reach counts as unreachable, so the game plays somewhere else.
/// Only cs2.exe and only those relay IPs are blocked: Steam, friends and community servers are never touched.
/// </summary>
public static class Regions
{
    public const string Url = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730";
    public static string CachePath => Paths.File("sdrconfig.json");

    public static readonly string[] ContinentOrder = { "Europe", "North America", "South America", "Asia", "Middle East", "Africa", "Oceania" };

    // ------------------------------------------------------------ The list

    /// <summary>Reads the API's JSON. Pops without relays stay out (nothing to ping or block there).</summary>
    public static SdrConfig Parse(string json, DateTime fetchedAt, bool fromCache = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("response", out var inner)) root = inner;   // the same JSON, wrapped, in case Valve moves it
        if (!root.TryGetProperty("pops", out var pops) || pops.ValueKind != JsonValueKind.Object) throw new InvalidDataException("No pops in the relay list");
        var list = new List<Pop>();
        foreach (var p in pops.EnumerateObject())
        {
            var relays = new List<Relay>();
            if (p.Value.TryGetProperty("relays", out var rs) && rs.ValueKind == JsonValueKind.Array)
                foreach (var r in rs.EnumerateArray())
                {
                    if (!r.TryGetProperty("ipv4", out var ip) || !IPAddress.TryParse(ip.GetString(), out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    int lo = 0, hi = 0;
                    if (r.TryGetProperty("port_range", out var pr) && pr.ValueKind == JsonValueKind.Array && pr.GetArrayLength() == 2) { lo = pr[0].GetInt32(); hi = pr[1].GetInt32(); }
                    relays.Add(new Relay(a.ToString(), lo, hi));
                }
            if (relays.Count == 0) continue;
            double lon = 0, lat = 0;
            if (p.Value.TryGetProperty("geo", out var geo) && geo.ValueKind == JsonValueKind.Array && geo.GetArrayLength() == 2) { lon = geo[0].GetDouble(); lat = geo[1].GetDouble(); }
            var desc = p.Value.TryGetProperty("desc", out var d) ? d.GetString() : p.Name;
            list.Add(new Pop(p.Name, desc, lon, lat, relays));
        }
        if (list.Count == 0) throw new InvalidDataException("The relay list is empty");
        var rev = root.TryGetProperty("revision", out var rv) && rv.TryGetInt64(out var n) ? n : 0;
        return new SdrConfig { Revision = rev, Pops = list, FetchedAt = fetchedAt, FromCache = fromCache };
    }

    /// <summary>
    /// The list from Valve; if that fails (offline, API down), the last good copy on this PC. Null only when there's neither.
    /// <paramref name="fetch"/> replaces the download (tests).
    /// </summary>
    public static async Task<SdrConfig> Load(string cachePath = null, Func<Task<string>> fetch = null)
    {
        cachePath ??= CachePath;
        try
        {
            var json = await (fetch ?? Download)();
            var cfg = Parse(json, DateTime.Now);
            try { File.WriteAllText(cachePath + ".tmp", json); File.Move(cachePath + ".tmp", cachePath, true); } catch { }
            return cfg;
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(cachePath)) return Parse(File.ReadAllText(cachePath), File.GetLastWriteTime(cachePath), fromCache: true);
            }
            catch (Exception cex) { Log.Warn("The saved relay list can't be read: " + cex.Message); }
            Log.Warn("Couldn't get Valve's relay list: " + (ex.InnerException ?? ex).Message);
            return null;
        }
    }

    static async Task<string> Download()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await http.GetStringAsync(Url);
    }

    // ------------------------------------------------------------ Cities, countries, continents

    // Valve writes US states (and England) where the country goes.
    static readonly HashSet<string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho",
        "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota",
        "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina",
        "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas",
        "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming",
    };

    static readonly Dictionary<string, string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["England"] = "United Kingdom", ["Scotland"] = "United Kingdom", ["Wales"] = "United Kingdom", ["UAE"] = "United Arab Emirates",
    };

    static readonly Dictionary<string, string> ContinentOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Netherlands"] = "Europe", ["Germany"] = "Europe", ["Finland"] = "Europe", ["United Kingdom"] = "Europe", ["Spain"] = "Europe",
        ["France"] = "Europe", ["Sweden"] = "Europe", ["Austria"] = "Europe", ["Poland"] = "Europe", ["Italy"] = "Europe", ["Portugal"] = "Europe",
        ["Turkey"] = "Europe", ["Russia"] = "Europe", ["Norway"] = "Europe", ["Denmark"] = "Europe", ["Romania"] = "Europe",
        ["United States"] = "North America", ["Canada"] = "North America", ["Mexico"] = "North America",
        ["Argentina"] = "South America", ["Brazil"] = "South America", ["Peru"] = "South America", ["Chile"] = "South America", ["Colombia"] = "South America",
        ["China"] = "Asia", ["Hong Kong"] = "Asia", ["South Korea"] = "Asia", ["Singapore"] = "Asia", ["Japan"] = "Asia", ["India"] = "Asia",
        ["United Arab Emirates"] = "Middle East", ["Saudi Arabia"] = "Middle East", ["Bahrain"] = "Middle East", ["Israel"] = "Middle East",
        ["South Africa"] = "Africa",
        ["Australia"] = "Oceania", ["Guam"] = "Oceania", ["New Zealand"] = "Oceania",
    };

    static readonly Dictionary<string, string> CityNames = new(StringComparer.OrdinalIgnoreCase) { ["Tokyo Koto City"] = "Tokyo" };

    // Partner networks in the name ("Datapacket Frankfurt"): the same city as Valve's own pop.
    static readonly string[] Providers = { "Alibaba Cloud ", "Tencent ", "Datapacket ", "Hetzner " };

    /// <summary>"Datapacket Frankfurt (Germany)" -> Frankfurt, Germany; "Atlanta (Georgia)" -> Atlanta, Georgia, United States.</summary>
    public static (string City, string Area, string Country) Describe(Pop p)
    {
        var desc = (p.Desc ?? p.Code).Trim();
        foreach (var pre in Providers) if (desc.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) desc = desc[pre.Length..];
        string inside = null;
        var open = desc.IndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && desc.EndsWith(')')) { inside = desc[(open + 2)..^1].Trim(); desc = desc[..open]; }
        var dash = desc.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) desc = desc[..dash];   // "Stockholm - Kista", "Chengdu - Mobile"
        var city = CityNames.GetValueOrDefault(desc.Trim(), desc.Trim());
        if (inside == null) return (city, null, city);   // Hong Kong, Singapore, Guam
        if (UsStates.Contains(inside) && p.Lon < -30) return (city, inside, "United States");
        return (city, null, CountryNames.GetValueOrDefault(inside, inside));
    }

    /// <summary>From the country, or from where the pop is on the map when Valve adds a country this table doesn't know.</summary>
    public static string Continent(string country, double lon, double lat)
    {
        if (country != null && ContinentOf.TryGetValue(country, out var c)) return c;
        if (lon < -30) return lat > 12 ? "North America" : "South America";
        if (lat < -10 && lon > 110) return "Oceania";
        if (lon < 60 && lat > 36) return "Europe";
        if (lon < 60 && lat < 12) return "Africa";
        if (lon < 60) return "Middle East";
        return "Asia";
    }

    /// <summary>The pops grouped by city, in continent order, then by name.</summary>
    public static List<RegionInfo> Group(SdrConfig cfg)
    {
        var byId = new Dictionary<string, (string City, string Area, string Country, string Continent, List<string> Codes, List<string> Ips)>();
        foreach (var p in cfg.Pops)
        {
            var (city, area, country) = Describe(p);
            var id = Slug(city + "-" + country);
            if (!byId.TryGetValue(id, out var r))
                byId[id] = r = (city, area, country, Continent(country, p.Lon, p.Lat), new List<string>(), new List<string>());
            r.Codes.Add(p.Code);
            foreach (var relay in p.Relays) if (!r.Ips.Contains(relay.Ip)) r.Ips.Add(relay.Ip);
        }
        return byId.Select(kv => new RegionInfo(kv.Key, kv.Value.City, kv.Value.Area, kv.Value.Country, kv.Value.Continent, kv.Value.Codes, kv.Value.Ips))
            .OrderBy(r => ContinentRank(r.Continent)).ThenBy(r => r.City, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Same rows to show: the same regions, names, pops and relay IPs, in the same order (the hourly read rebuilds the list only when this is false).</summary>
    public static bool SameRows(IReadOnlyList<RegionInfo> a, IReadOnlyList<RegionInfo> b) =>
        a != null && b != null && a.Count == b.Count && a.Zip(b).All(p =>
            p.First.Id == p.Second.Id && p.First.City == p.Second.City && p.First.Area == p.Second.Area
            && p.First.Country == p.Second.Country && p.First.Continent == p.Second.Continent
            && p.First.Codes.SequenceEqual(p.Second.Codes) && p.First.RelayIps.SequenceEqual(p.Second.RelayIps));

    public static int ContinentRank(string continent) { var i = Array.IndexOf(ContinentOrder, continent); return i < 0 ? 99 : i; }

    static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    // ------------------------------------------------------------ Picking

    /// <summary>The <paramref name="n"/> measured regions with the lowest ping (no answer = left out).</summary>
    public static List<string> Closest(IReadOnlyDictionary<string, int> pings, int n) =>
        pings.Where(kv => kv.Value >= 0).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Take(n).Select(kv => kv.Key).ToList();

    /// <summary>Everything except <paramref name="keep"/>: what a shortcut turns off.</summary>
    public static HashSet<string> AllExcept(IEnumerable<RegionInfo> regions, IEnumerable<string> keep)
    {
        var k = keep.ToHashSet();
        return regions.Select(r => r.Id).Where(id => !k.Contains(id)).ToHashSet();
    }

    /// <summary>
    /// The continent for the "<continent> only" shortcut: the one of the region with the lowest ping, else the one of your home
    /// country (a region in that country, or the country table), else null (the button hides). Only continents on the page count.
    /// </summary>
    public static string HomeContinent(IReadOnlyCollection<RegionInfo> regions, IReadOnlyDictionary<string, int> pings, string homeCountry)
    {
        if (regions == null || regions.Count == 0) return null;
        var byId = regions.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var best = (pings ?? new Dictionary<string, int>()).Where(p => p.Value >= 0 && byId.ContainsKey(p.Key))
            .OrderBy(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault();
        if (best != null) return byId[best].Continent;
        if (string.IsNullOrWhiteSpace(homeCountry)) return null;
        var name = CountryNames.GetValueOrDefault(homeCountry.Trim(), homeCountry.Trim());
        var same = regions.FirstOrDefault(r => string.Equals(r.Country, name, StringComparison.OrdinalIgnoreCase));
        if (same != null) return same.Continent;
        return ContinentOf.TryGetValue(name, out var c) && regions.Any(r => r.Continent == c) ? c : null;
    }

    /// <summary>"continent:Europe" in a shortcut's keep list: every city of that continent, new ones included.</summary>
    public static string ContinentKey(string continent) => "continent:" + continent;

    /// <summary>
    /// What a shortcut's keep list turns off in today's list: everything else, cities Valve added since then included.
    /// Null when none of the kept regions is in the list any more (a rename): the saved set stays, so nothing blocks every region.
    /// </summary>
    public static HashSet<string> KeepOnly(IEnumerable<RegionInfo> regions, IEnumerable<string> keep)
    {
        var k = keep.ToHashSet();
        var list = regions.ToList();
        var kept = list.Where(r => k.Contains(r.Id) || k.Contains(ContinentKey(r.Continent))).Select(r => r.Id).ToList();
        return kept.Count == 0 ? null : AllExcept(list, kept);
    }

    /// <summary>
    /// The regions rule (or the leftover of an interrupted update) is among these Firewall rule names (<see cref="Firewall.Remaining"/>).
    /// <paramref name="unreadableCounts"/>: a Firewall that couldn't be read counts as the rule still being there.
    /// </summary>
    public static bool RuleLeft(IEnumerable<string> remaining, bool unreadableCounts = false) =>
        remaining.Any(n => n.StartsWith(Firewall.RegionsRuleName) || (unreadableCounts && n.StartsWith("(could not read")));

    /// <summary>
    /// Whether the 30 s check writes the rule again: regions are blocked and the rule isn't on (not written yet, CS2 not found,
    /// or a failed write once <paramref name="retryAt"/> passed), or Allow all failed to remove it and the wait passed.
    /// </summary>
    public static bool ApplyDue(int blocked, bool ruleOn, string error, bool noCs2, DateTime now, DateTime retryAt) =>
        blocked > 0 ? !ruleOn && (error == null || noCs2 || now >= retryAt)
                    : error != null && !noCs2 && now >= retryAt;

    /// <summary>The relay IPs of the blocked regions that exist in this list (a blocked city Valve dropped adds nothing).</summary>
    public static List<string> BlockedIps(IEnumerable<RegionInfo> regions, IEnumerable<string> blocked)
    {
        var b = blocked.ToHashSet();
        return regions.Where(r => b.Contains(r.Id)).SelectMany(r => r.RelayIps).Distinct().OrderBy(ip => ip, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The one Firewall rule: outbound, cs2.exe only, to the blocked relays only, every protocol, from every local IP.
    /// Null when nothing is blocked (the rule is removed).
    /// </summary>
    public static Firewall.FwRule Rule(IEnumerable<RegionInfo> regions, IEnumerable<string> blocked, string cs2Path)
    {
        var ips = BlockedIps(regions, blocked);
        if (ips.Count == 0 || string.IsNullOrEmpty(cs2Path)) return null;
        return new Firewall.FwRule(Firewall.RegionsRuleName, ips, cs2Path, null,
            "Managed by CS2 IP Changer (Regions page). Stops CS2 from reaching Valve's relays in the regions you turned off, so matchmaking plays elsewhere. Steam and other programs are not affected.");
    }

    /// <summary>Puts the rule in place (or removes it when nothing is blocked). Needs administrator.</summary>
    public static void Apply(IEnumerable<RegionInfo> regions, IEnumerable<string> blocked, string cs2Path)
    {
        var rule = Rule(regions, blocked, cs2Path);
        if (rule == null) Firewall.Remove(Firewall.RegionsRuleName);
        else Firewall.Apply(rule);
    }

    // ------------------------------------------------------------ Ping

    // ICMP echo, not a real SDR ping: the relays' own ping protocol (UDP, signed) isn't public. The relays answer ICMP,
    // and the request leaves from the local IP of the protected connection, so Windows sends it out the phone, the
    // same path CS2's packets take. The number is close to what the game shows, usually within a few ms.
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern IntPtr IcmpCreateFile();

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern bool IcmpCloseHandle(IntPtr handle);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint IcmpSendEcho2Ex(IntPtr handle, IntPtr ev, IntPtr apc, IntPtr apcContext, uint source, uint destination,
        byte[] data, ushort dataSize, IntPtr options, byte[] reply, uint replySize, uint timeoutMs);

    static uint Addr(string ip) => BitConverter.ToUInt32(IPAddress.Parse(ip).GetAddressBytes(), 0);

    /// <summary>One ICMP echo from <paramref name="sourceIp"/> to <paramref name="ip"/>. Round trip in ms, or -1 without an answer.</summary>
    public static int PingFrom(string ip, string sourceIp, int timeoutMs = 1000)
    {
        var h = IcmpCreateFile();
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return -1;
        try
        {
            var data = new byte[32];
            var reply = new byte[256];
            var n = IcmpSendEcho2Ex(h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Addr(sourceIp), Addr(ip), data, (ushort)data.Length, IntPtr.Zero, reply, (uint)reply.Length, (uint)timeoutMs);
            // ICMP_ECHO_REPLY: Address (4), Status (4), RoundTripTime (4)…
            if (n == 0 || BitConverter.ToUInt32(reply, 4) != 0) return -1;
            return (int)BitConverter.ToUInt32(reply, 8);
        }
        catch { return -1; }
        finally { IcmpCloseHandle(h); }
    }

    /// <summary>
    /// A region's ping: the best of 2 tries to each of its first 2 relays (one relay down doesn't make the region look far).
    /// -1 when none answered.
    /// </summary>
    public static async Task<int> PingRegion(RegionInfo r, string sourceIp, int timeoutMs = 1000)
    {
        var tries = r.RelayIps.Take(2).SelectMany(ip => new[] { ip, ip }).Select(ip => Task.Run(() => PingFrom(ip, sourceIp, timeoutMs)));
        var results = await Task.WhenAll(tries);
        var ok = results.Where(x => x >= 0).ToList();
        return ok.Count == 0 ? -1 : ok.Min();
    }

    /// <summary>Every region at once (16 at a time), through <paramref name="sourceIp"/>. Region id -> ms (-1 = no answer).</summary>
    public static async Task<Dictionary<string, int>> PingAll(IReadOnlyList<RegionInfo> regions, string sourceIp, int timeoutMs = 1000)
    {
        var gate = new SemaphoreSlim(16);
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        await Task.WhenAll(regions.Select(async r =>
        {
            await gate.WaitAsync();
            try { result[r.Id] = await PingRegion(r, sourceIp, timeoutMs); }
            finally { gate.Release(); }
        }));
        return new Dictionary<string, int>(result);
    }
}
