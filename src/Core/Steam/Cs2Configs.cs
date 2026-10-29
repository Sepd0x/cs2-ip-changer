using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SRT.Core;

// ------------------------------------------------------------ CS2 config of each account (Steam/userdata/<id32>/730), see publish/configs-pesquisa.md

/// <summary>A file of CS2's per-account config.</summary>
public enum CfgPart { Keys, Convars, Video, Machine }

/// <summary>The text of each part (null = the account doesn't have it).</summary>
public sealed class CfgSet
{
    public Dictionary<CfgPart, string> Files { get; } = new();
    public string Get(CfgPart p) => Files.TryGetValue(p, out var t) ? t : null;
    public bool Empty => Files.Count == 0;
}

/// <summary>A copy to do: from an account or a snapshot, to one account. Waits in the queue while CS2 is open.</summary>
public sealed class CfgJob
{
    public string SourceId { get; set; }
    public string Snapshot { get; set; }
    public string Target { get; set; }
    public List<string> Categories { get; set; } = new();
    public string Reason { get; set; }
    public DateTime At { get; set; }
}

public sealed class CfgState
{
    /// <summary>Accounts already seen: one that isn't here is new and gets the config once.</summary>
    public List<string> Known { get; set; }
    /// <summary>SteamID64 -> part -> SHA-1 of what the app wrote last. A file that still has it was not changed in game.</summary>
    public Dictionary<string, Dictionary<string, string>> Written { get; set; } = new();
    public List<CfgJob> Pending { get; set; } = new();
    public string LastSyncId { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string LastResult { get; set; }
}

public sealed class CfgBackup
{
    public string SteamId { get; set; }
    public DateTime At { get; set; }
    public string Reason { get; set; }
    public string From { get; set; }
    public string SyncId { get; set; }
    /// <summary>Paths under 730 that were saved here.</summary>
    public List<string> Files { get; set; } = new();
    /// <summary>Paths under 730 that didn't exist before: an undo removes them.</summary>
    public List<string> Created { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Dir { get; set; }
}

public sealed class CfgSnapshot
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string From { get; set; }            // local only: never in an exported .zip
    public List<string> Parts { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Dir { get; set; }
}

public enum CfgOutcome { Copied, Same, Queued, Skipped, Failed }

public sealed record CfgResult(CfgOutcome Outcome, string Target, List<string> Categories, string Detail = null);

/// <summary>One setting that differs between two configs.</summary>
public sealed record CfgDiff(string Category, string Key, string Label, string A, string B);

/// <summary>
/// The crosshair, read from the user convars (null fields = not in the file, CS2's default applies).
/// <paramref name="Pixels"/>: CS2's newer crosshair (style 7), whose length, gap and thickness are pixels at <paramref name="ScreenHeight"/>.
/// </summary>
public sealed record Crosshair(double? Style, double? Size, double? Gap, double? Thickness, bool? Dot, bool? Outline, double? OutlineThickness, int Color, byte R, byte G, byte B, byte Alpha, bool? TStyle,
    bool Pixels = false, double? ScreenHeight = null);

/// <summary>
/// CS2 writes its config files without escapes: a bind on the \ key is saved as "\" (seen on a real keys file). The Steam VDF
/// parser would read that as an escaped quote, so these files are read and written token by token, exactly as CS2 does.
/// </summary>
public static class Vcfg
{
    static List<(string Text, bool Quoted)> Tokens(string t)
    {
        var list = new List<(string, bool)>();
        var p = 0;
        while (p < t.Length)
        {
            var ch = t[p];
            if (char.IsWhiteSpace(ch)) { p++; continue; }
            if (ch == '/' && p + 1 < t.Length && t[p + 1] == '/') { while (p < t.Length && t[p] != '\n') p++; continue; }
            if (ch is '{' or '}') { list.Add((ch.ToString(), false)); p++; continue; }
            if (ch == '"')
            {
                var end = t.IndexOf('"', p + 1);
                if (end < 0) end = t.Length;
                list.Add((t[(p + 1)..end], true));
                p = end + 1;
                continue;
            }
            var s = p;
            while (p < t.Length && !char.IsWhiteSpace(t[p]) && t[p] is not ('{' or '}' or '"')) p++;
            list.Add((t[s..p], false));
        }
        return list;
    }

    public static VdfNode Parse(string text)
    {
        var tokens = Tokens(text);
        var i = 0;
        return Body(tokens, ref i, 0);
    }

    static VdfNode Body(List<(string Text, bool Quoted)> k, ref int i, int depth)
    {
        // A crafted file with thousands of "{" would overflow the stack (an imported snapshot can come from anyone).
        if (depth > 32) throw new InvalidDataException("config nested too deep");
        var node = new VdfNode();
        while (i < k.Count)
        {
            var key = k[i++];
            if (!key.Quoted && key.Text == "}") return node;
            if (i >= k.Count) return node;
            var v = k[i++];
            if (!v.Quoted && v.Text == "{") node.Items.Add(new(key.Text, Body(k, ref i, depth + 1)));
            else node.Items.Add(new(key.Text, v.Text));
        }
        return node;
    }

    /// <summary>The same layout CS2 writes (tabs, LF; the caller picks the line endings). Throws on a quote, which this format can't hold.</summary>
    public static string Write(VdfNode n, int depth = 0)
    {
        var sb = new StringBuilder();
        var tab = new string('\t', depth);
        foreach (var (k, v) in n.Items)
        {
            if (k.Contains('"') || (v is string sv && sv.Contains('"'))) throw new InvalidDataException("a quote inside a config value");
            if (v is VdfNode c) sb.Append(tab).Append('"').Append(k).Append("\"\n").Append(tab).Append("{\n").Append(Write(c, depth + 1)).Append(tab).Append("}\n");
            else sb.Append(tab).Append('"').Append(k).Append("\"\t\t\"").Append((string)v).Append("\"\n");
        }
        return sb.ToString();
    }

    /// <summary>True if writing the parsed file gives back the same tokens (nothing bare, nothing lost).</summary>
    public static bool RoundTrips(string text)
    {
        try
        {
            var before = Tokens(text);
            if (before.Any(t => !t.Quoted && t.Text is not ("{" or "}"))) return false;
            return before.SequenceEqual(Tokens(Write(Parse(text))));
        }
        catch { return false; }
    }
}

public static class Cs2Config
{
    const long SteamId64Base = 76561197960265728;

    /// <summary>What can be copied, in the order shown.</summary>
    public static readonly string[] Categories = { "binds", "crosshair", "game", "video", "hud" };

    public static string CategoryName(string c) => c switch
    {
        "binds" => "binds", "crosshair" => "crosshair", "game" => "game settings", "video" => "video", "hud" => "HUD, radar and audio", _ => c,
    };

    public static string CategoryList(IEnumerable<string> cats)
    {
        // In a sentence with others, "HUD, radar and audio" would read as three more items.
        var c = cats.ToList();
        var l = c.Select(x => x == "hud" && c.Count > 1 ? "HUD/radar/audio" : CategoryName(x)).ToList();
        return l.Count switch { 0 => "nothing", 1 => l[0], _ => string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1] };
    }

    /// <summary>Steam's userdata folder id (SteamID64 minus the base), or null.</summary>
    public static string Id32(string steamId64) =>
        long.TryParse(steamId64, out var id) && id > SteamId64Base && id - SteamId64Base <= uint.MaxValue ? (id - SteamId64Base).ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>Under 730: the file CS2 reads, its "last sent to the cloud" twin and the Steam Cloud copy (null when not clouded).</summary>
    public static (string Local, string LastClouded, string Remote) PathsOf(CfgPart p) => p switch
    {
        CfgPart.Keys => (@"local\cfg\cs2_user_keys_0_slot0.vcfg", @"local\cfg\cs2_user_keys_0_slot0.vcfg_lastclouded", @"remote\cs2_user_keys.vcfg"),
        CfgPart.Convars => (@"local\cfg\cs2_user_convars_0_slot0.vcfg", @"local\cfg\cs2_user_convars_0_slot0.vcfg_lastclouded", @"remote\cs2_user_convars.vcfg"),
        CfgPart.Video => (@"local\cfg\cs2_video.txt", null, null),
        _ => (@"local\cfg\cs2_machine_convars.vcfg", null, null),
    };

    /// <summary>File name inside a snapshot.</summary>
    public static string SnapshotFile(CfgPart p) => p switch
    {
        CfgPart.Keys => "keys.vcfg", CfgPart.Convars => "convars.vcfg", CfgPart.Video => "video.txt", _ => "machine.vcfg",
    };

    public static CfgPart[] PartsOf(string category) => category switch
    {
        "binds" => new[] { CfgPart.Keys },
        "crosshair" or "game" => new[] { CfgPart.Convars },
        "video" => new[] { CfgPart.Video },
        "hud" => new[] { CfgPart.Machine },
        _ => Array.Empty<CfgPart>(),
    };

    public static bool IsCrosshairKey(string k) => k.StartsWith("cl_crosshair", StringComparison.OrdinalIgnoreCase)
        || k.Equals("crosshair", StringComparison.OrdinalIgnoreCase) || k.Equals("cl_fixedcrosshairgap", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The user convars the target gets: everything from the source (crosshair and game), or only one side merged into the target's file
    /// (alone when the target has none). Null if a side can't be merged without losing something (the parser doesn't keep that file exactly).
    /// </summary>
    public static string MergeConvars(string src, string dst, bool crosshair, bool game)
    {
        if (src == null) return null;
        if (crosshair && game) return src;
        if (!crosshair && !game) return dst;
        if (!Vcfg.RoundTrips(src) || (dst != null && !Vcfg.RoundTrips(dst))) return null;
        bool Mine(string k) => crosshair ? IsCrosshairKey(k) : !IsCrosshairKey(k);
        if (dst == null)
        {
            // No file on the target: CS2 uses its defaults there, so it gets only the chosen side.
            var only = Vcfg.Parse(src);
            var c = only.Child("config")?.Child("convars");
            if (c == null) return null;
            c.Items.RemoveAll(i => i.Value is string && !Mine(i.Key));
            return Crlf(Vcfg.Write(only));
        }
        var s = Vcfg.Parse(src).Child("config")?.Child("convars");
        var root = Vcfg.Parse(dst);
        var d = root.Child("config")?.Child("convars");
        if (s == null || d == null) return null;
        // A key missing in the source means CS2's default there: it goes from the target too.
        d.Items.RemoveAll(i => i.Value is string && Mine(i.Key));
        foreach (var (k, v) in s.Items) if (v is string str && Mine(k)) d.Items.Add(new(k, str));
        return Crlf(Vcfg.Write(root));
    }

    /// <summary>
    /// Video keys that belong to this PC's display, not to the player: the refresh rate and which monitor. Copied from another account
    /// they made CS2 run a 144 Hz screen at 60 Hz. The resolution stays a player setting (stretched 4:3 is a choice), so it is copied.
    /// Snapshots and exports keep the whole file, as a record; the rule applies when a copy is written to an account (Push).
    /// </summary>
    public static readonly string[] DisplayKeys = { "setting.refreshrate_numerator", "setting.refreshrate_denominator", "setting.monitor_index" };

    /// <summary>
    /// The source's cs2_video.txt with the target's own display keys (or none, so CS2 detects them, when the target has no file).
    /// A file this parser doesn't keep exactly is copied as it is.
    /// </summary>
    public static string KeepDisplay(string src, string dst)
    {
        if (src == null) return null;
        if (!Vcfg.RoundTrips(src) || (dst != null && !Vcfg.RoundTrips(dst))) return src;
        var root = Vcfg.Parse(src);
        var s = root.Items.Select(i => i.Value).OfType<VdfNode>().FirstOrDefault();
        if (s == null) return src;
        var d = dst == null ? null : Vcfg.Parse(dst).Items.Select(i => i.Value).OfType<VdfNode>().FirstOrDefault();
        foreach (var k in DisplayKeys)
        {
            if (d?.Get(k) is string v) s.Set(k, v);
            else s.Items.RemoveAll(i => i.Value is string && i.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
        }
        return Crlf(Vcfg.Write(root));
    }

    /// <summary>A bind that isn't in the file and an explicit "&lt;unbound&gt;" are the same: the key does nothing.</summary>
    public static bool Unbound(string v) => string.IsNullOrEmpty(v) || v == "<unbound>";

    public static string Crlf(string t) => t.Replace("\r\n", "\n").Replace("\n", "\r\n");
    public static string Lf(string t) => t.Replace("\r\n", "\n");
    /// <summary>Same settings, whatever the line endings.</summary>
    public static bool SameText(string a, string b) => a != null && b != null && Lf(a).TrimEnd() == Lf(b).TrimEnd();

    /// <summary>Machine convars CS2 rewrites by itself every session (cache times, last news read, menu loadout): not settings.</summary>
    static readonly string[] Volatile = { "cachedvalue_", "ui_news_last_read_link", "cl_redemption_reset_timestamp", "ui_vanitysetting_", "ui_setting_advertiseforhire_auto_last", "panorama_console_position_and_size" };
    public static bool IsVolatile(string k) => Volatile.Any(v => k.StartsWith(v, StringComparison.OrdinalIgnoreCase));

    /// <summary>Same settings: same text, or for the machine file the same values once what CS2 rewrites by itself is left out.</summary>
    public static bool SameSettings(CfgPart p, string a, string b)
    {
        if (SameText(a, b)) return true;
        if (p != CfgPart.Machine || a == null || b == null) return false;
        var fa = Flatten(a, p);
        var fb = Flatten(b, p);
        if (fa.Count == 0 || fb.Count == 0) return false;   // can't be read: compared as text only
        var ka = fa.Where(x => !IsVolatile(x.Key)).ToList();
        return ka.Count == fb.Count(x => !IsVolatile(x.Key)) && ka.All(x => fb.TryGetValue(x.Key, out var v) && v == x.Value);
    }

    /// <summary>"1.000000" (older CS2 builds) and "1" (newer) are the same value.</summary>
    static bool SameNumber(string a, string b) =>
        a != null && b != null
        && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
        && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
        && x == y;

    public static string Sha(string text) => text == null ? null : Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Lf(text).TrimEnd())));

    // ------------------------------------------------------------ Which copies a trigger makes

    /// <summary>
    /// The copies for a sync mode. <paramref name="trigger"/>: "new" (an account appeared) and "login" (before logging in) only touch
    /// <paramref name="target"/>; "manual" (Sync now) and "closed" (CS2 closed, Latest change wins only) touch every account.
    /// Opted-out accounts and the source itself are never targets, and an opted-out account is never the source either
    /// (except the main account picked by hand).
    /// </summary>
    public static List<CfgJob> Plan(string mode, string mainId, string previousId, string latestId, string trigger, string target,
        IEnumerable<string> all, ICollection<string> optOut, IEnumerable<string> categories)
    {
        var list = new List<CfgJob>();
        var cats = categories?.Where(Categories.Contains).Distinct().ToList() ?? new();
        if (cats.Count == 0) return list;
        if (trigger == "closed" && mode != "latest") return list;
        var source = mode switch { "main" => mainId, "previous" => previousId, "latest" => latestId, _ => null };
        if (source == null) return list;
        if (mode != "main" && optOut?.Contains(source) == true) return list;
        var targets = trigger is "new" or "login" ? new[] { target } : all ?? Enumerable.Empty<string>();
        foreach (var t in targets.Where(t => t != null).Distinct())
        {
            if (t == source || optOut?.Contains(t) == true) continue;
            list.Add(new CfgJob { SourceId = source, Target = t, Categories = cats, Reason = trigger, At = DateTime.Now });
        }
        return list;
    }

    // ------------------------------------------------------------ Compare

    static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sensitivity"] = "Sensitivity", ["zoom_sensitivity_ratio"] = "Zoom sensitivity", ["m_yaw"] = "Mouse yaw", ["m_pitch"] = "Mouse pitch",
        ["sensitivity_y_scale"] = "Vertical sensitivity scale", ["m_rawinput"] = "Raw input",
        ["viewmodel_fov"] = "Viewmodel FOV", ["viewmodel_offset_x"] = "Viewmodel X", ["viewmodel_offset_y"] = "Viewmodel Y", ["viewmodel_offset_z"] = "Viewmodel Z",
        ["viewmodel_presetpos"] = "Viewmodel preset", ["cl_righthand"] = "Right hand", ["cl_prefer_lefthanded"] = "Left handed",
        ["cl_crosshairstyle"] = "Crosshair: style", ["cl_crosshairsize"] = "Crosshair: size", ["cl_crosshairgap"] = "Crosshair: gap",
        ["cl_crosshairthickness"] = "Crosshair: thickness", ["cl_crosshairdot"] = "Crosshair: dot", ["cl_crosshair_drawoutline"] = "Crosshair: outline",
        ["cl_crosshair_outlinethickness"] = "Crosshair: outline thickness", ["cl_crosshaircolor"] = "Crosshair: color", ["cl_crosshaircolor_r"] = "Crosshair: red",
        ["cl_crosshaircolor_g"] = "Crosshair: green", ["cl_crosshaircolor_b"] = "Crosshair: blue", ["cl_crosshairalpha"] = "Crosshair: alpha",
        ["cl_crosshairusealpha"] = "Crosshair: use alpha", ["cl_crosshair_t"] = "Crosshair: T style", ["cl_crosshair_recoil"] = "Crosshair: follows recoil",
        ["cl_crosshairgap_useweaponvalue"] = "Crosshair: gap per weapon", ["cl_crosshair_sniper_width"] = "Crosshair: sniper width",
        ["cl_crosshair_length"] = "Crosshair: length", ["cl_crosshair_gap"] = "Crosshair: gap", ["cl_crosshair_thickness"] = "Crosshair: thickness",
        ["cl_crosshaircolor_a"] = "Crosshair: alpha", ["cl_crosshairoutline_r"] = "Crosshair: outline red", ["cl_crosshairoutline_g"] = "Crosshair: outline green",
        ["cl_crosshairoutline_b"] = "Crosshair: outline blue", ["cl_crosshairoutline_a"] = "Crosshair: outline alpha", ["cl_crosshair_screen_height"] = "Crosshair: screen height",
        ["resolution"] = "Resolution", ["setting.fullscreen"] = "Fullscreen", ["setting.nowindowborder"] = "Borderless window", ["setting.aspectratiomode"] = "Aspect ratio",
        ["setting.mat_vsync"] = "V-Sync", ["setting.msaa_samples"] = "Anti-aliasing (MSAA)", ["setting.r_low_latency"] = "NVIDIA Reflex",
        ["setting.videocfg_shadow_quality"] = "Shadow quality", ["setting.videocfg_texture_detail"] = "Texture detail", ["setting.shaderquality"] = "Shader detail",
        ["setting.videocfg_particle_detail"] = "Particle detail", ["setting.videocfg_ao_detail"] = "Ambient occlusion", ["setting.videocfg_hdr_detail"] = "HDR",
        ["setting.videocfg_fsr_detail"] = "FidelityFX Super Resolution", ["setting.r_texturefilteringquality"] = "Texture filtering", ["setting.monitor_index"] = "Monitor",
        ["setting.refreshrate_numerator"] = "Refresh rate",
        ["fps_max"] = "Max FPS", ["fps_max_ui"] = "Max FPS in menus", ["r_fullscreen_gamma"] = "Brightness (gamma)", ["volume"] = "Master volume",
        ["snd_gamevolume"] = "Game volume", ["snd_gamevoicevolume"] = "Voice volume", ["snd_menumusic_volume"] = "Menu music volume",
        ["snd_roundstart_volume"] = "Round start music", ["snd_roundend_volume"] = "Round end music", ["snd_mvp_volume"] = "MVP music",
        ["snd_tensecondwarning_volume"] = "Ten second warning", ["snd_deathcamera_volume"] = "Death camera music", ["snd_mapobjective_volume"] = "Bomb and hostage music",
        ["cl_hud_color"] = "HUD color", ["hud_scaling"] = "HUD scale", ["cl_radar_scale"] = "Radar scale", ["cl_radar_rotate"] = "Radar rotates",
        ["cl_radar_always_centered"] = "Radar centered", ["cl_radar_icon_scale_min"] = "Radar icon size", ["cl_hud_radar_scale"] = "Radar HUD size",
        ["cl_showloadout"] = "Always show inventory", ["cl_teamid_overhead_mode"] = "Teammate labels", ["cl_color"] = "Teammate color",
        ["gameinstructor_enable"] = "Game instructor", ["cl_autowepswitch"] = "Auto weapon switch", ["cl_use_opens_buy_menu"] = "Use key opens buy menu",
    };

    static readonly string[] ColorNames = { "red", "green", "yellow", "blue", "cyan", "custom" };

    /// <summary>The settings that differ, in words where known (category order, then label).</summary>
    public static List<CfgDiff> Diff(CfgSet a, CfgSet b)
    {
        var list = new List<CfgDiff>();
        foreach (var part in new[] { CfgPart.Convars, CfgPart.Keys, CfgPart.Video, CfgPart.Machine })
        {
            var fa = Flatten(a?.Get(part), part);
            var fb = Flatten(b?.Get(part), part);
            if (fa == null && fb == null) continue;
            foreach (var k in (fa?.Keys ?? Enumerable.Empty<string>()).Union(fb?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase))
            {
                string va = null, vb = null;
                fa?.TryGetValue(k, out va);
                fb?.TryGetValue(k, out vb);
                if (string.Equals(va, vb, StringComparison.Ordinal)) continue;
                if (part == CfgPart.Keys && fa != null && fb != null && Unbound(va) && Unbound(vb)) continue;   // missing on one side, "unbound" on the other
                if (part != CfgPart.Keys && SameNumber(va, vb)) continue;   // binds are commands, not numbers
                if (part == CfgPart.Machine && IsVolatile(k)) continue;
                var cat = part switch { CfgPart.Keys => "binds", CfgPart.Video => "video", CfgPart.Machine => "hud", _ => IsCrosshairKey(k) ? "crosshair" : "game" };
                var label = part == CfgPart.Keys ? $"Key {k}" : Labels.TryGetValue(BaseKey(k), out var l) ? l : k;
                list.Add(new CfgDiff(cat, k, label, Show(k, va, part, fa == null), Show(k, vb, part, fb == null)));
            }
        }
        var order = Categories.ToList();
        return list.OrderBy(d => order.IndexOf(d.Category)).ThenBy(d => Labels.ContainsKey(BaseKey(d.Key)) || d.Category == "binds" ? 0 : 1)
            .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"fps_max_ui$2" is fps_max_ui for a split-screen slot.</summary>
    static string BaseKey(string k) => k.Contains('$') ? k[..k.IndexOf('$')] : k;

    static string Show(string key, string v, CfgPart part, bool noFile)
    {
        if (noFile) return "no file";
        if (v == null) return part == CfgPart.Keys ? "unbound" : "default";
        if (part == CfgPart.Keys) return v == "<unbound>" ? "unbound" : v;
        if (v == "true") return "on";
        if (v == "false") return "off";
        if (key.Equals("cl_crosshaircolor", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var c) && c >= 0 && c < ColorNames.Length) return ColorNames[c];
        if (key.Equals("setting.fullscreen", StringComparison.OrdinalIgnoreCase) || key.Equals("setting.nowindowborder", StringComparison.OrdinalIgnoreCase)
            || key.Equals("setting.mat_vsync", StringComparison.OrdinalIgnoreCase)) return v == "1" ? "on" : v == "0" ? "off" : v;
        return v;
    }

    /// <summary>Leaf settings of a part (the "config/convars" style wrappers left out), or null without the file.</summary>
    public static Dictionary<string, string> Flatten(string text, CfgPart part)
    {
        if (text == null) return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        VdfNode root;
        try { root = Vcfg.Parse(text); } catch { return map; }
        var top = root.Items.Select(i => i.Value).OfType<VdfNode>().FirstOrDefault();
        if (top == null) return map;
        // config { convars { ... } } and config { bindings { ... } }: one level more
        if (top.Items.Count == 1 && top.Items[0].Value is VdfNode only) top = only;
        void Walk(VdfNode n, string prefix)
        {
            foreach (var (k, v) in n.Items)
            {
                var key = prefix + k;
                if (v is VdfNode c) Walk(c, key + "/");
                else map[key] = (string)v;
            }
        }
        Walk(top, "");
        if (part == CfgPart.Video && map.TryGetValue("setting.defaultres", out var w) && map.TryGetValue("setting.defaultresheight", out var h))
        {
            map.Remove("setting.defaultres");
            map.Remove("setting.defaultresheight");
            map["resolution"] = $"{w}x{h}";
        }
        // The GPU ids and the version of the file change with the PC, not with the player.
        if (part == CfgPart.Video) foreach (var k in new[] { "VendorID", "DeviceID", "Version", "Autoconfig", "setting.knowndevice" }) map.Remove(k);
        return map;
    }

    static double? Num(Dictionary<string, string> m, string k) =>
        m.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    static bool? Bool(Dictionary<string, string> m, string k) => m.TryGetValue(k, out var v) ? v is "true" or "1" : null;

    public static Crosshair ReadCrosshair(CfgSet s)
    {
        var m = Flatten(s?.Get(CfgPart.Convars), CfgPart.Convars);
        if (m == null || !m.Keys.Any(IsCrosshairKey)) return null;
        // CS2's newer crosshair (style 7) has its own keys, in pixels, and its own r/g/b/a colour; the old keys stay in the file, unused.
        var px = m.ContainsKey("cl_crosshair_length") || (Num(m, "cl_crosshairstyle") ?? 0) >= 7;
        var color = px ? 5 : (int)(Num(m, "cl_crosshaircolor") ?? 1);   // 5 = custom, so r/g/b are read
        byte B(string k, double def) => (byte)Math.Clamp(Num(m, k) ?? def, 0, 255);
        (byte r, byte g, byte b) = color switch
        {
            0 => ((byte)250, (byte)50, (byte)50), 1 => ((byte)50, (byte)250, (byte)50), 2 => ((byte)250, (byte)250, (byte)50),
            3 => ((byte)50, (byte)50, (byte)250), 4 => ((byte)50, (byte)250, (byte)250),
            _ => (B("cl_crosshaircolor_r", 50), B("cl_crosshaircolor_g", 250), B("cl_crosshaircolor_b", 50)),
        };
        var alpha = px ? B("cl_crosshaircolor_a", 255) : Bool(m, "cl_crosshairusealpha") == false ? (byte)255 : B("cl_crosshairalpha", 200);
        return new Crosshair(Num(m, "cl_crosshairstyle"), Num(m, px ? "cl_crosshair_length" : "cl_crosshairsize"), Num(m, px ? "cl_crosshair_gap" : "cl_crosshairgap"),
            Num(m, px ? "cl_crosshair_thickness" : "cl_crosshairthickness"),
            Bool(m, "cl_crosshairdot"), Bool(m, "cl_crosshair_drawoutline"), Num(m, "cl_crosshair_outlinethickness"), color, r, g, b, alpha, Bool(m, "cl_crosshair_t"),
            px, px ? Num(m, "cl_crosshair_screen_height") : null);
    }

    /// <summary>"size 1 · gap -4 · thick 1 · cyan · dot off", or null without crosshair settings.</summary>
    public static string CrosshairSummary(CfgSet s)
    {
        var c = ReadCrosshair(s);
        if (c == null) return null;
        string N(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";
        var color = c.Color >= 0 && c.Color < ColorNames.Length - 1 ? ColorNames[c.Color] : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return $"{(c.Pixels ? "length" : "size")} {N(c.Size)} · gap {N(c.Gap)} · thick {N(c.Thickness)} · {color}" + (c.Dot == true ? " · dot" : "") + (c.Outline == true ? " · outline" : "");
    }
}

/// <summary>
/// Copies, backups, snapshots and the queue. Every path comes in from outside, so the tests run on made-up userdata folders.
/// Never writes while cs2.exe is open (the game saves on exit and would undo the copy): the copy waits in the queue.
/// </summary>
public sealed class Cs2ConfigStore
{
    public string UserData { get; }
    public string Root { get; }
    public Func<bool> Cs2Running { get; set; } = () => Steam.Cs2Running;
    public int KeepBackups { get; set; } = 20;
    public CfgState State { get; }

    string StatePath => Path.Combine(Root, "state.json");
    string BackupRoot => Path.Combine(Root, "backups");
    string SnapshotRoot => Path.Combine(Root, "snapshots");
    const long MaxFile = 1024 * 1024;
    /// <summary>When this run of the app opened the store: a Copy or Sync now queued before it is from an earlier run.</summary>
    readonly DateTime _opened = DateTime.Now;

    public Cs2ConfigStore(string userData, string root)
    {
        UserData = userData;
        Root = root;
        Directory.CreateDirectory(Root);
        State = Store.Load(StatePath, () => new CfgState());
        State.Written ??= new();
        State.Pending ??= new();
    }

    public void SaveState() => Store.Save(StatePath, State);

    public string Dir(string steamId) => UserData != null && Cs2Config.Id32(steamId) is string id ? Path.Combine(UserData, id, "730") : null;

    public bool HasConfig(string steamId) => Dir(steamId) is string d && !Redirected(d) && Enum.GetValues<CfgPart>().Any(p => File.Exists(Path.Combine(d, Cs2Config.PathsOf(p).Local)));

    /// <summary>The text of a small file. With <paramref name="real"/>: null unless the open file really is there (not reached through a link).</summary>
    static string ReadText(string path, string real = null)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFile) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxFile || (real != null && !SamePath(FinalPath(fs.SafeFileHandle), real))) return null;
            return new StreamReader(fs, Encoding.UTF8).ReadToEnd();
        }
        catch { return null; }
    }

    /// <summary>
    /// A file in the app's own folder (backups, snapshots), null unless the open file really is there: users can write in that
    /// folder too, and a link there would make the admin app read any file and copy it into an account.
    /// </summary>
    string ReadOwn(string path)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root));
            var rel = Path.GetRelativePath(root, Path.GetFullPath(path));
            if (rel == "." || rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
            using var h = OpenDir(root);
            return FinalPath(h) is string real ? ReadText(path, Path.Combine(real, rel)) : null;
        }
        catch { return null; }
    }

    static readonly HashSet<string> WarnedLinked = new(), WarnedRead = new();

    public CfgSet Read(string steamId)
    {
        var set = new CfgSet();
        if (Dir(steamId) is not string d) return set;
        // The app runs as admin: a source folder that is a link could make it copy any file on the PC into another account.
        if (Redirected(d))
        {
            if (WarnedLinked.Add(steamId)) Log.Warn("Config: an account's CS2 folder goes through a link or junction, so it was not read.");
            return set;
        }
        var held = new List<SafeFileHandle>();
        try
        {
            var real = Path.Combine(HoldUserData(held), Cs2Config.Id32(steamId), "730");
            foreach (var p in Enum.GetValues<CfgPart>())
            {
                var rel = Cs2Config.PathsOf(p).Local;
                if (ReadText(Path.Combine(d, rel), Path.Combine(real, rel)) is string t) set.Files[p] = t;
            }
            lock (WarnedRead) WarnedRead.Remove(steamId);   // read fine again: the next failure is logged too
        }
        catch (IOException ex)
        {
            // Otherwise the page only says "No CS2 config" with no reason. Once per account until a read works again (pages read often).
            set.Files.Clear();
            bool first;
            lock (WarnedRead) first = WarnedRead.Add(steamId);
            if (first) Log.Warn($"Config: an account's CS2 config couldn't be read ({Why(ex)}), so it shows as having none.");
        }
        finally { foreach (var h in held) h.Dispose(); }
        return set;
    }

    /// <summary>When the account's config was last changed in game: files that still hold what the app wrote don't count.</summary>
    public DateTime? Changed(string steamId)
    {
        if (Dir(steamId) is not string d || Redirected(d)) return null;
        DateTime? best = null;
        State.Written.TryGetValue(steamId, out var mine);
        foreach (var p in Enum.GetValues<CfgPart>())
        {
            var path = Path.Combine(d, Cs2Config.PathsOf(p).Local);
            if (!File.Exists(path)) continue;
            if (mine != null && mine.TryGetValue(p.ToString(), out var sha) && sha == Cs2Config.Sha(ReadText(path))) continue;
            var t = File.GetLastWriteTime(path);
            if (best == null || t > best) best = t;
        }
        return best;
    }

    /// <summary>Latest change wins: the account changed most recently in game (null if none was).</summary>
    public string PickLatest(IEnumerable<string> ids) =>
        ids.Select(i => (Id: i, At: Changed(i))).Where(x => x.At != null).OrderByDescending(x => x.At).Select(x => x.Id).FirstOrDefault();

    /// <summary>The account folder (or a folder on the way) is a link or junction: the app runs as admin and only writes to real folders.</summary>
    static bool Redirected(string dir730)
    {
        try
        {
            foreach (var p in new[] { Path.GetDirectoryName(dir730), dir730, Path.Combine(dir730, "local"), Path.Combine(dir730, "local", "cfg"), Path.Combine(dir730, "remote") })
                if (Directory.Exists(p) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) return true;
            return false;
        }
        catch { return true; }
    }

    // ------------------------------------------------------------ Links held off
    // Every user can write in Steam's userdata, and a folder there swapped for a junction between a check and the write would send
    // the admin app somewhere else. So reads check where the open file really is, and writes hold the folders open while they run.

    const string Linked = "the Steam folder goes through a link or junction";

    /// <summary>
    /// A short reason for the Log and the toasts. .NET's own messages carry the path, and the path has userdata\&lt;id32&gt;
    /// (the account's SteamID), so those are swapped for a plain reason. The app's own messages have no path and stay.
    /// </summary>
    public static string Why(Exception ex) => !ex.Message.Contains('\\') ? ex.Message : ex switch
    {
        UnauthorizedAccessException => "no permission to write the config files",
        FileNotFoundException or DirectoryNotFoundException => "a config file or folder is missing",
        IOException => "a file is in use, try again with Steam closed",
        _ => ex.GetType().Name,
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    /// <summary>Where an open file or folder really is, every link on the way resolved, or null.</summary>
    static string FinalPath(SafeFileHandle h)
    {
        var sb = new StringBuilder(1024);
        var n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity) return null;
        var p = sb.ToString();
        return p.StartsWith(@"\\?\UNC\") ? @"\\" + p[8..] : p.StartsWith(@"\\?\") ? p[4..] : p;
    }

    static bool SamePath(string a, string b) => a != null && b != null &&
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A folder held open without delete sharing: while the handle lives, nobody can rename it or put a link in its place.</summary>
    static SafeFileHandle OpenDir(string path)
    {
        const uint ListDirectory = 0x1, BackupSemantics = 0x02000000;
        var h = CreateFileW(path, ListDirectory, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, BackupSemantics, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        throw new IOException("can't open the Steam folder");
    }

    /// <summary>Userdata's real path, with it and the Steam folder held. The Steam folder may be a link (moved to another drive); userdata may not.</summary>
    string HoldUserData(List<SafeFileHandle> held)
    {
        var ud = Path.TrimEndingDirectorySeparator(Path.GetFullPath(UserData));
        var steam = Path.GetDirectoryName(ud) ?? throw new IOException(Linked);
        var hs = OpenDir(steam);
        held.Add(hs);
        var hu = OpenDir(ud);
        held.Add(hu);
        var real = FinalPath(hu);
        if (FinalPath(hs) is not string rs || !SamePath(real, Path.Combine(rs, Path.GetFileName(ud)))) throw new IOException(Linked);
        return real;
    }

    /// <summary>
    /// Holds the account's 730 folder and the folders of <paramref name="rels"/> open (in <paramref name="held"/>), making the missing
    /// ones one at a time, each inside a folder already held. Throws when one is a link or junction. Returns 730's real path.
    /// </summary>
    string Pin(string steamId, IEnumerable<string> rels, List<SafeFileHandle> held)
    {
        var real = HoldUserData(held);
        var id = Cs2Config.Id32(steamId);
        var dirs = new List<string> { id, Path.Combine(id, "730") };
        foreach (var rel in rels)
        {
            var parts = (Path.GetDirectoryName(rel) ?? "").Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i <= parts.Length; i++)
            {
                var d = Path.Combine(new[] { id, "730" }.Concat(parts[..i]).ToArray());
                if (!dirs.Contains(d, StringComparer.OrdinalIgnoreCase)) dirs.Add(d);
            }
        }
        foreach (var d in dirs)
        {
            var path = Path.Combine(UserData, d);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);   // its parent is held, so it is made in the real folder
            var h = OpenDir(path);
            held.Add(h);
            if (!SamePath(FinalPath(h), Path.Combine(real, d))) throw new IOException(Linked);
        }
        return Path.Combine(real, id, "730");
    }

    static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".srt-tmp";
        if (File.Exists(tmp)) File.Delete(tmp);   // a leftover (or something put there) is never opened
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false))) w.Write(text);
            File.Move(tmp, path, true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    static bool ReadOnly(string path) => File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);

    public CfgSet Source(CfgJob job) => job.Snapshot != null ? ReadSnapshot(job.Snapshot) : Read(job.SourceId);

    /// <summary>Runs one copy: backup of what changes, then the new files. With CS2 open it goes to the queue instead.</summary>
    public CfgResult Push(CfgJob job, string syncId, string fromLabel = null)
    {
        var cats = job.Categories.Where(Cs2Config.Categories.Contains).Distinct().ToList();
        if (Cs2Running())
        {
            State.Pending.RemoveAll(j => j.Target == job.Target);   // the newest copy for an account replaces the older one
            if (job.At == default) job.At = DateTime.Now;            // when it was decided: a change in game after that wins (FlushPending)
            State.Pending.Add(job);
            SaveState();
            return new CfgResult(CfgOutcome.Queued, job.Target, cats, "CS2 is open");
        }
        var dir = Dir(job.Target);
        if (dir == null) return new CfgResult(CfgOutcome.Failed, job.Target, cats, "no Steam userdata folder");
        var src = Source(job);
        if (src.Empty) return new CfgResult(CfgOutcome.Skipped, job.Target, cats, "the source has no CS2 config yet");

        // What each file becomes (path under 730 -> text)
        var plan = new Dictionary<string, string>();
        var done = new List<string>();
        var clouded = File.Exists(Path.Combine(dir, "remotecache.vdf"));   // Steam already syncs this account's cloud files on this PC
        var current = Read(job.Target);
        CfgResult skipped = null;   // why nothing was written, when it isn't "already the same"
        bool Put(CfgPart p, string text)
        {
            if (text == null) return false;
            var (local, last, remote) = Cs2Config.PathsOf(p);
            // Only when the file CS2 reads changes: then its cloud twins follow, as CS2 does when it saves.
            if (Cs2Config.SameSettings(p, text, current.Get(p))) return false;
            // A file marked read-only is the player's choice (so CS2 keeps it): it stays, the other files still sync.
            if (new[] { local, last, remote }.FirstOrDefault(r => r != null && ReadOnly(Path.Combine(dir, r))) is string ro)
            {
                Log.Warn($"Config: {Path.GetFileName(ro)} is read-only, so it was left as it is.");
                skipped = new CfgResult(CfgOutcome.Skipped, job.Target, cats, $"{Path.GetFileName(ro)} is read-only");
                return false;
            }
            plan[local] = Cs2Config.Crlf(text);
            if (last != null && (clouded || File.Exists(Path.Combine(dir, last)))) plan[last] = Cs2Config.Crlf(text);
            if (remote != null && clouded) plan[remote] = Cs2Config.Lf(text);
            return true;
        }
        foreach (var c in cats.Where(c => c is "binds" or "video" or "hud"))
            foreach (var p in Cs2Config.PartsOf(c))
                if (Put(p, p == CfgPart.Video ? Cs2Config.KeepDisplay(src.Get(p), current.Get(p)) : src.Get(p))) done.Add(c);   // the display stays this PC's
        bool cross = cats.Contains("crosshair"), game = cats.Contains("game");
        if ((cross || game) && src.Get(CfgPart.Convars) is string conv)
        {
            var merged = Cs2Config.MergeConvars(conv, current.Get(CfgPart.Convars), cross, game);
            if (merged == null)
            {
                Log.Warn("Config: the game settings file has content this version can't merge, so only whole files were copied.");
                skipped = new CfgResult(CfgOutcome.Failed, job.Target, cats, "its game settings file can't be merged, so the crosshair and game settings were not copied");
            }
            else if (Put(CfgPart.Convars, merged))
            {
                if (cross) done.Add("crosshair");
                if (game) done.Add("game");
            }
        }
        if (plan.Count == 0) return skipped ?? new CfgResult(CfgOutcome.Same, job.Target, cats);
        if (Redirected(dir)) return new CfgResult(CfgOutcome.Failed, job.Target, cats, Linked);

        var held = new List<SafeFileHandle>();
        try
        {
            string real;
            try { real = Pin(job.Target, plan.Keys, held); }
            catch (IOException ex) { return new CfgResult(CfgOutcome.Failed, job.Target, cats, Why(ex)); }
            Backup(job.Target, dir, plan.Keys, job.Reason, fromLabel, syncId, real);
            var wrote = new List<string>();
            try { foreach (var (rel, text) in plan) { WriteAtomic(Path.Combine(dir, rel), text); wrote.Add(rel); } }
            finally
            {
                // Also after a write that failed half-way: what did go in is the app's, and Undo can take it back.
                if (!State.Written.TryGetValue(job.Target, out var mine)) State.Written[job.Target] = mine = new();
                foreach (var p in Enum.GetValues<CfgPart>())
                    if (wrote.Contains(Cs2Config.PathsOf(p).Local)) mine[p.ToString()] = Cs2Config.Sha(plan[Cs2Config.PathsOf(p).Local]);
                State.LastSyncId = syncId;
                State.LastSyncAt = DateTime.Now;
                SaveState();
            }
        }
        finally { foreach (var h in held) h.Dispose(); }
        return new CfgResult(CfgOutcome.Copied, job.Target, Cs2Config.Categories.Where(done.Contains).ToList());
    }

    /// <summary>The queue, once CS2 is closed. Returns what ran (empty while CS2 is still open).</summary>
    public List<CfgResult> FlushPending(string syncId, Func<CfgJob, string> label = null)
    {
        var results = new List<CfgResult>();
        if (State.Pending.Count == 0 || Cs2Running()) return results;
        var jobs = State.Pending.ToList();
        State.Pending.Clear();
        SaveState();
        foreach (var j in jobs)
        {
            // An automatic copy decided before the account was played: what it saved in game since then wins.
            // A Copy or Sync now the player asked for in this run still goes in; one left from an earlier run is treated the same way
            // (the account may have been played since without the app).
            var asked = j.Reason is "copy" or "manual" && j.At >= _opened;
            if (!asked && Changed(j.Target) is DateTime c && c > j.At)
            {
                results.Add(new CfgResult(CfgOutcome.Skipped, j.Target, j.Categories, "it changed in game after the copy was queued"));
                continue;
            }
            try { results.Add(Push(j, syncId, label?.Invoke(j))); }
            catch (Exception ex) { results.Add(new CfgResult(CfgOutcome.Failed, j.Target, j.Categories, Why(ex))); }
        }
        return results;
    }

    /// <summary>Takes sync copies out of the queue (Sync turned off, an account opted out). A Copy the player asked for stays.</summary>
    public int DropQueued(Func<CfgJob, bool> which)
    {
        var n = State.Pending.RemoveAll(j => j.Reason != "copy" && which(j));
        if (n > 0) SaveState();
        return n;
    }

    // ------------------------------------------------------------ Backups

    /// <summary>Copies what is there now. <paramref name="real"/> is 730's real path: a file that turns out to be a link stops it.</summary>
    void Backup(string steamId, string dir, IEnumerable<string> rels, string reason, string from, string syncId, string real)
    {
        var b = new CfgBackup { SteamId = steamId, At = DateTime.Now, Reason = reason, From = from, SyncId = syncId };
        var bdir = Path.Combine(BackupRoot, Cs2Config.Id32(steamId), $"{b.At:yyyyMMdd-HHmmss-fff}");
        for (var i = 2; Directory.Exists(bdir); i++) bdir = Path.Combine(BackupRoot, Cs2Config.Id32(steamId), $"{b.At:yyyyMMdd-HHmmss-fff}-{i}");
        Directory.CreateDirectory(bdir);
        foreach (var rel in rels.Distinct())
        {
            var path = Path.Combine(dir, rel);
            if (File.Exists(path))
            {
                var to = Path.Combine(bdir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (!SamePath(FinalPath(s.SafeFileHandle), Path.Combine(real, rel))) throw new IOException(Linked);
                    using var t = new FileStream(to, FileMode.Create, FileAccess.Write);
                    s.CopyTo(t);
                }
                b.Files.Add(rel);
            }
            else b.Created.Add(rel);
        }
        Store.Save(Path.Combine(bdir, "backup.json"), b);
        try
        {
            foreach (var old in Directory.GetDirectories(Path.GetDirectoryName(bdir)!).OrderByDescending(x => x, StringComparer.Ordinal).Skip(Math.Max(1, KeepBackups)))
                Directory.Delete(old, true);
        }
        catch (Exception ex) { Log.Warn("Could not tidy the old config backups: " + Why(ex)); }
    }

    public List<CfgBackup> Backups()
    {
        var list = new List<CfgBackup>();
        if (!Directory.Exists(BackupRoot)) return list;
        foreach (var f in Directory.GetFiles(BackupRoot, "backup.json", SearchOption.AllDirectories))
        {
            var b = Store.Load<CfgBackup>(f, () => null);
            if (b?.SteamId == null) continue;
            b.Dir = Path.GetDirectoryName(f);
            list.Add(b);
        }
        return list.OrderByDescending(b => b.At).ToList();
    }

    /// <summary>Puts a backup back (after a backup of what is there now, so it can be undone too). False while CS2 is open.</summary>
    public bool Restore(CfgBackup b, string syncId)
    {
        if (Cs2Running()) return false;
        var dir = Dir(b.SteamId) ?? throw new IOException("no Steam userdata folder");
        if (Redirected(dir)) throw new IOException(Linked);
        // Read first: the backup made just below can push this one out of the kept ones.
        var saved = b.Files.Where(Safe).Select(rel => (rel, text: ReadOwn(Path.Combine(b.Dir, rel)))).Where(x => x.text != null).ToList();
        var rels = b.Files.Concat(b.Created).Where(Safe).ToList();
        // Before anything is written: a read-only file would stop the restore half-way.
        if (saved.FirstOrDefault(x => ReadOnly(Path.Combine(dir, x.rel))).rel is string ro)
            throw new IOException($"{Path.GetFileName(ro)} is read-only: clear that in its Properties first");
        var held = new List<SafeFileHandle>();
        try
        {
            var real = Pin(b.SteamId, rels, held);
            Backup(b.SteamId, dir, rels.Where(r => File.Exists(Path.Combine(dir, r))), "restore", null, syncId, real);
            foreach (var (rel, text) in saved) WriteAtomic(Path.Combine(dir, rel), text);
            // Files the copy created didn't exist before it: the account goes back to CS2's defaults there.
            foreach (var rel in b.Created.Where(Safe)) { try { File.Delete(Path.Combine(dir, rel)); } catch { } }
        }
        finally { foreach (var h in held) h.Dispose(); }
        // What the restore wrote is the app's, not a change made in game (Latest change wins).
        if (!State.Written.TryGetValue(b.SteamId, out var mine)) State.Written[b.SteamId] = mine = new();
        foreach (var p in Enum.GetValues<CfgPart>())
        {
            var local = Cs2Config.PathsOf(p).Local;
            if (saved.FirstOrDefault(x => x.rel == local).text is string t) mine[p.ToString()] = Cs2Config.Sha(t);
            else if (b.Created.Contains(local)) mine.Remove(p.ToString());
        }
        State.LastSyncId = syncId;
        State.LastSyncAt = DateTime.Now;
        SaveState();
        return true;
    }

    /// <summary>The files the app copies (all under local\cfg or remote, which are checked for links): a backup.json can name nothing else.</summary>
    static readonly HashSet<string> Known = new(Enum.GetValues<CfgPart>().Select(Cs2Config.PathsOf)
        .SelectMany(p => new[] { p.Local, p.LastClouded, p.Remote }).Where(x => x != null), StringComparer.OrdinalIgnoreCase);

    /// <summary>A path from a backup.json is one of the files the app copies.</summary>
    static bool Safe(string rel) => !string.IsNullOrEmpty(rel) && Known.Contains(rel.Replace('/', '\\'));

    /// <summary>
    /// Undo last sync: every backup of the last sync goes back. Returns how many accounts; -1 while CS2 is open.
    /// An account that can't be restored stays as it is (its id goes in <paramref name="failed"/>) and the others still go back.
    /// </summary>
    /// <summary>The last sync left backups to put back (Undo last sync has something to do).</summary>
    public bool CanUndo => State.LastSyncId is string last && Backups().Any(b => b.SyncId == last);

    public int UndoLast(string syncId, List<string> failed = null)
    {
        if (State.LastSyncId == null) return 0;
        if (Cs2Running()) return -1;
        var last = State.LastSyncId;
        var list = Backups().Where(b => b.SyncId == last).GroupBy(b => b.SteamId).Select(g => g.OrderBy(b => b.At).First()).ToList();
        var done = 0;
        foreach (var b in list)
        {
            try { if (Restore(b, syncId)) done++; else failed?.Add(b.SteamId); }
            catch (Exception ex) { Log.Warn($"Config undo: an account stayed as it was: {Why(ex)}"); failed?.Add(b.SteamId); }
        }
        return done;
    }

    // ------------------------------------------------------------ Snapshots

    static string SafeName(string name)
    {
        var n = new string((name ?? "").Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_').ToArray()).Trim(' ', '.');
        return n.Length == 0 ? null : n.Length > 40 ? n[..40].TrimEnd(' ', '.') : n;
    }

    public CfgSnapshot FindSnapshot(string name) => Snapshots().FirstOrDefault(x => string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The snapshot's own folder, else a free one: two names can make the same folder name ("Retakes/A" and "Retakes_A").</summary>
    string SnapshotDir(string name)
    {
        if (FindSnapshot(name) is CfgSnapshot old) return old.Dir;
        var safe = SafeName(name);
        var dir = Path.Combine(SnapshotRoot, safe);
        for (var i = 2; Directory.Exists(dir); i++)
        {
            var suffix = "-" + i;
            dir = Path.Combine(SnapshotRoot, (safe.Length + suffix.Length > 40 ? safe[..(40 - suffix.Length)].TrimEnd(' ', '.') : safe) + suffix);
        }
        return dir;
    }

    public List<CfgSnapshot> Snapshots()
    {
        var list = new List<CfgSnapshot>();
        if (!Directory.Exists(SnapshotRoot)) return list;
        foreach (var d in Directory.GetDirectories(SnapshotRoot))
        {
            var s = Store.Load<CfgSnapshot>(Path.Combine(d, "snapshot.json"), () => null);
            if (s?.Name == null) continue;
            s.Dir = d;
            list.Add(s);
        }
        return list.OrderByDescending(s => s.Created).ToList();
    }

    public CfgSnapshot SaveSnapshot(string name, CfgSet set, string from)
    {
        if (SafeName(name) == null) throw new ArgumentException("Give the snapshot a name.");
        if (set == null || set.Empty) throw new InvalidOperationException("That account has no CS2 config yet: open CS2 on it once.");
        var dir = SnapshotDir(name);
        Directory.CreateDirectory(dir);
        var s = new CfgSnapshot { Name = name.Trim(), Created = DateTime.Now, From = from, Dir = dir };
        foreach (var (p, text) in set.Files)
        {
            WriteAtomic(Path.Combine(dir, Cs2Config.SnapshotFile(p)), Cs2Config.Crlf(text));
            s.Parts.Add(p.ToString());
        }
        foreach (var p in Enum.GetValues<CfgPart>().Where(p => !set.Files.ContainsKey(p)))
            try { File.Delete(Path.Combine(dir, Cs2Config.SnapshotFile(p))); } catch { }
        Store.Save(Path.Combine(dir, "snapshot.json"), s);
        return s;
    }

    public CfgSet ReadSnapshot(string name)
    {
        var set = new CfgSet();
        var s = FindSnapshot(name);
        if (s == null) return set;
        foreach (var p in Enum.GetValues<CfgPart>())
            if (ReadOwn(Path.Combine(s.Dir, Cs2Config.SnapshotFile(p))) is string t) set.Files[p] = t;
        return set;
    }

    public void DeleteSnapshot(string name)
    {
        var s = FindSnapshot(name);
        if (s != null) Directory.Delete(s.Dir, true);
    }

    /// <summary>A .zip to share: the config files and the name. The account it came from stays on this PC.</summary>
    public void ExportSnapshot(string name, string zipPath)
    {
        var s = FindSnapshot(name) ?? throw new FileNotFoundException("No snapshot called " + name);
        var tmp = zipPath + ".srt-tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            void Add(string entry, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Add("snapshot.json", JsonSerializer.Serialize(new { s.Name, s.Created, s.Parts, App = "CS2 IP Changer " + AppInfo.Version }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var p in Enum.GetValues<CfgPart>())
                if (ReadOwn(Path.Combine(s.Dir, Cs2Config.SnapshotFile(p))) is string t) Add(Cs2Config.SnapshotFile(p), t);
        }
        File.Move(tmp, zipPath, true);
    }

    /// <summary>Reads a snapshot .zip (only the known files, each small and readable as KeyValues). Returns the name it got here.</summary>
    public string ImportSnapshot(string zipPath)
    {
        var set = new CfgSet();
        string name = null;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.Entries.Count > 64) throw new InvalidDataException("The .zip has too many files for a snapshot.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in zip.Entries)
            {
                var isMeta = e.FullName == "snapshot.json";
                var part = isMeta ? null : Enum.GetValues<CfgPart>().Cast<CfgPart?>().FirstOrDefault(p => Cs2Config.SnapshotFile(p.Value) == e.FullName);
                if (!isMeta && part == null) continue;   // anything else in the zip is ignored, unopened
                if (!seen.Add(e.FullName)) throw new InvalidDataException($"{e.FullName} is in the .zip twice.");
                if (e.Length > MaxFile) throw new InvalidDataException($"{e.FullName} is too big for a config file.");
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                // Read no more than the limit, whatever the zip says the size is.
                var buf = new char[MaxFile + 1];
                var n = r.ReadBlock(buf, 0, buf.Length);
                if (n > MaxFile) throw new InvalidDataException($"{e.FullName} is too big for a config file.");
                var text = new string(buf, 0, n);
                if (isMeta)
                {
                    try { name = JsonDocument.Parse(text).RootElement.GetProperty("Name").GetString(); } catch { }
                    continue;
                }
                var node = Vcfg.Parse(text);   // throws on a file nested too deep
                if (node.Items.Count == 0) throw new InvalidDataException($"{e.FullName} is not a CS2 config file.");
                set.Files[part.Value] = text;
            }
        }
        if (set.Empty) throw new InvalidDataException("The .zip has no CS2 config files.");
        name = SafeName(name) ?? SafeName(Path.GetFileNameWithoutExtension(zipPath)) ?? "Imported";
        var taken = Snapshots().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var final = name;
        // SaveSnapshot finds a free folder itself; the suffix goes on a cut name so it always changes the name (names stop at 40).
        for (var i = 2; taken.Contains(final); i++)
        {
            if (i > 999) throw new InvalidDataException("Too many snapshots with that name: delete or rename some first.");
            var suffix = $" ({i})";
            final = (name.Length + suffix.Length > 40 ? name[..(40 - suffix.Length)].TrimEnd(' ', '.') : name) + suffix;
        }
        SaveSnapshot(final, set, "imported");
        return final;
    }
}
