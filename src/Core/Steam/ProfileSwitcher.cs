using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SRT.Core;

// ------------------------------------------------------------ Profiles page: templates, the plan per account, the saved
// sign-ins (DPAPI), the copies for undo and the apply run. The network part is in SteamWeb.cs.

[Flags]
public enum ProfileFields
{
    None = 0, Name = 1, Avatar = 2, RealName = 4, Summary = 8, Country = 16, Privacy = 32, CustomUrl = 64,
    Look = Name | Avatar | RealName | Summary | Country,
}

/// <summary>Steam's privacy levels. Null = leave as it is.</summary>
public sealed class PrivacyPreset
{
    public const int Private = 1, Friends = 2, Public = 3;
    public int? Profile { get; set; }
    public int? GameDetails { get; set; }
    public int? Inventory { get; set; }
    public int? FriendsList { get; set; }
    public int? Comments { get; set; }
    [JsonIgnore] public bool Any => Profile != null || GameDetails != null || Inventory != null || FriendsList != null || Comments != null;

    /// <summary>Steam's comment setting uses other numbers: 0 friends, 1 anyone, 2 nobody.</summary>
    public static int CommentValue(int level) => level switch { Private => 2, Friends => 0, _ => 1 };
    public static int CommentLevel(int value) => value switch { 2 => Private, 0 => Friends, _ => Public };

    /// <summary>Only the levels picked change; every other key Steam sent stays as it was.</summary>
    public PrivacyState ApplyTo(PrivacyState cur)
    {
        var s = new PrivacyState { Settings = (JsonObject)cur.Settings.DeepClone(), CommentPermission = cur.CommentPermission };
        if (Profile is int p) s.Settings["PrivacyProfile"] = p;
        if (GameDetails is int g) s.Settings["PrivacyOwnedGames"] = g;
        if (Inventory is int i) s.Settings["PrivacyInventory"] = i;
        if (FriendsList is int f) s.Settings["PrivacyFriendsList"] = f;
        if (Comments is int c) s.CommentPermission = CommentValue(c);
        return s;
    }

    public static PrivacyPreset From(PrivacyState s)
    {
        int? I(string k) => s.Settings[k] is JsonNode n && n.GetValueKind() == JsonValueKind.Number ? (int)n : null;
        return new PrivacyPreset { Profile = I("PrivacyProfile"), GameDetails = I("PrivacyOwnedGames"), Inventory = I("PrivacyInventory"), FriendsList = I("PrivacyFriendsList"), Comments = CommentLevel(s.CommentPermission) };
    }

    public PrivacyPreset Copy() => (PrivacyPreset)MemberwiseClone();
}

/// <summary>A saved look. Name may use {name} (the account's current Steam name) and {n} (1, 2, 3… in the list).</summary>
public sealed class ProfileTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New template";
    public string Name { get; set; } = "{name}";
    public string RealName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Country { get; set; } = "";       // ISO code ("PT"), "" = none shown
    public string CustomUrl { get; set; } = "";     // may use {n}
    public string Avatar { get; set; }              // file name in profiles/avatars (already re-encoded)
    public PrivacyPreset Privacy { get; set; } = new();
    public ProfileFields Fields { get; set; } = ProfileFields.Name | ProfileFields.Avatar;
    public DateTime Created { get; set; } = DateTime.Now;

    public ProfileTemplate Copy() { var t = (ProfileTemplate)MemberwiseClone(); t.Privacy = Privacy?.Copy() ?? new(); return t; }
}

// ------------------------------------------------------------ Name rules

/// <summary>
/// What a template may put on a public profile. There is deliberately no {login}: the login is half of what someone needs
/// to take an account over (and what phishing goes after), and a profile is public. A login typed in by hand is caught too.
/// </summary>
public static class NamePattern
{
    public const int MaxName = 32;
    // "{{" and "}}" are a plain brace (a Steam name copied into a template can have them).
    static readonly Regex Tag = new(@"\{\{|\}\}|\{([^{}]*)\}", RegexOptions.Compiled);
    static readonly string[] LoginWords = { "login", "account", "accountname", "account_name", "user", "username", "steamlogin" };

    /// <summary>Text as it is, braces doubled so no part of it reads as a tag.</summary>
    public static string Escape(string text) => (text ?? "").Replace("{", "{{").Replace("}", "}}");

    /// <summary>Null when the pattern is fine, else the reason in plain words.</summary>
    public static string Problem(string pattern, bool allowName = true)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "The name is empty.";
        foreach (Match m in Tag.Matches(pattern))
        {
            if (!m.Groups[1].Success) continue;
            var t = m.Groups[1].Value.Trim().ToLowerInvariant();
            if (t == "n" || (t == "name" && allowName)) continue;
            if (LoginWords.Contains(t)) return $"{{{m.Groups[1].Value}}} can't be used: it would put the account's username on its public profile, and that makes the account easier to steal.";
            return allowName ? $"Unknown tag {{{m.Groups[1].Value}}}. Use {{name}} (the account's Steam name now) or {{n}} (1, 2, 3…)." : $"Unknown tag {{{m.Groups[1].Value}}}. Only {{n}} (1, 2, 3…) works here.";
        }
        return null;
    }

    public static string Render(string pattern, string currentName, int n) =>
        Tag.Replace(pattern ?? "", m => !m.Groups[1].Success ? m.Value[..1] : m.Groups[1].Value.Trim().ToLowerInvariant() switch { "n" => n.ToString(CultureInfo.InvariantCulture), "name" => currentName ?? "", _ => m.Value }).Trim();

    /// <summary>True when the text shows the login (3+ characters, any case). Shorter logins match too many words to be useful.</summary>
    public static bool ShowsLogin(string text, string login) =>
        !string.IsNullOrEmpty(text) && login is { Length: >= 3 } && text.Contains(login, StringComparison.OrdinalIgnoreCase);

    /// <summary>The text between the tags, as typed and with "{{" and "}}" made single (the recording filter matches both).</summary>
    public static IEnumerable<string> Literals(string pattern)
    {
        var at = 0;
        var parts = new List<string>();
        foreach (Match m in Tag.Matches(pattern ?? ""))
        {
            if (!m.Groups[1].Success) continue;
            parts.Add(pattern[at..m.Index]); at = m.Index + m.Length;
        }
        parts.Add((pattern ?? "")[at..]);
        foreach (var p in parts)
        {
            yield return p.Trim();
            var plain = p.Replace("{{", "{").Replace("}}", "}").Trim();
            if (plain != p.Trim()) yield return plain;
        }
    }

    public static bool HasN(string pattern) => Has(pattern, "n");
    public static bool HasName(string pattern) => Has(pattern, "name");
    static bool Has(string pattern, string tag) => Tag.Matches(pattern ?? "").Any(m => m.Groups[1].Success && m.Groups[1].Value.Trim().Equals(tag, StringComparison.OrdinalIgnoreCase));
}

public static class Countries
{
    static List<(string Code, string Name)> _all;

    /// <summary>ISO country codes with English names, from Windows itself (no list kept in the app).</summary>
    public static IReadOnlyList<(string Code, string Name)> All => _all ??= CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new System.Globalization.RegionInfo(c.Name); } catch { return null; } })
        .Where(r => r != null && r.TwoLetterISORegionName.Length == 2 && r.TwoLetterISORegionName.All(char.IsAsciiLetterUpper))
        .GroupBy(r => r.TwoLetterISORegionName).Select(g => (g.Key, g.First().EnglishName))
        .OrderBy(x => x.EnglishName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string NameOf(string code) => string.IsNullOrEmpty(code) ? null : All.FirstOrDefault(c => c.Code == code).Name ?? code;

    /// <summary>"Lisboa, Lisboa, Portugal" (the public XML) to "PT". Null when it doesn't end in a known country.</summary>
    public static string FromLocation(string location)
    {
        var last = (location ?? "").Split(',').LastOrDefault()?.Trim();
        return string.IsNullOrEmpty(last) ? null : All.FirstOrDefault(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)).Code;
    }
}

// ------------------------------------------------------------ The plan: what each account gets

/// <summary>One account to change. Login is only for the leak check: it never leaves this object.</summary>
/// <summary>A look read for a template: what the profile shows, and what it keeps private (those fields stay as they are on each account).</summary>
public sealed record LookCopy(string Name, string RealName, string Summary, string Country, string AvatarUrl, ProfileFields Unknown)
{
    /// <summary>From the public XML. A private profile has no real name, bio or location; a location Windows doesn't name is unknown, not "none".</summary>
    /// <param name="onlyFilled">From a link to someone else's profile: a field that came back empty is left unticked too, so Apply
    /// never wipes the bio or real name of your accounts with nothing.</param>
    public static LookCopy FromPublic(PublicLook look, bool onlyFilled = false)
    {
        var cc = Countries.FromLocation(look.Location);
        ProfileFields unknown = 0;
        if (string.IsNullOrEmpty(look.PersonaName)) unknown |= ProfileFields.Name;
        if (look.RealName == null || (onlyFilled && string.IsNullOrWhiteSpace(look.RealName))) unknown |= ProfileFields.RealName;
        if (look.Summary == null || (onlyFilled && string.IsNullOrWhiteSpace(look.Summary))) unknown |= ProfileFields.Summary;
        if (look.Location == null || (cc == null && (onlyFilled || !string.IsNullOrWhiteSpace(look.Location)))) unknown |= ProfileFields.Country;
        return new LookCopy(look.PersonaName, look.RealName, look.Summary, cc, look.AvatarUrl, unknown);
    }

    /// <summary>
    /// Into the template: the name as it is (braces escaped, so "{x}" stays text), no custom URL (it belongs to one account).
    /// <paramref name="avatar"/>: the picture was read and saved; without it the template's picture is left out.
    /// </summary>
    public void Into(ProfileTemplate t, bool avatar, PrivacyPreset privacy = null)
    {
        t.Name = Name != null ? NamePattern.Escape(Name) : t.Name;
        t.RealName = RealName ?? "";
        t.Summary = Summary ?? "";
        t.Country = Country ?? "";
        t.CustomUrl = "";
        if (privacy != null) t.Privacy = privacy;
        t.Fields = (ProfileFields.Look | (privacy != null ? ProfileFields.Privacy : 0)) & ~Unknown;
        if (!avatar) t.Fields &= ~ProfileFields.Avatar;
    }
}

public sealed class ProfileJob
{
    public string SteamId { get; init; }
    public string Shown { get; init; }               // what the app calls the account (log, results)
    [JsonIgnore] public string Login { get; init; }
    public ProfileFields Fields { get; init; }
    public string PersonaName { get; init; }
    public string RealName { get; init; }
    public string Summary { get; init; }
    public string Country { get; init; }
    public string State { get; init; }               // only a restore sets these; null = keep them if the country stays
    public string City { get; init; }
    public string CustomUrl { get; init; }
    [JsonIgnore] public byte[] Avatar { get; init; }
    /// <summary>The picture is Steam's default one (from a copy taken while the profile had it): it can't be uploaded back as itself.</summary>
    public bool AvatarIsDefault { get; init; }
    public PrivacyPreset Privacy { get; init; }
    public bool ClearAliases { get; init; }
    public bool Restore { get; init; }               // putting a copy back: no new copy is taken
    public string Problem { get; init; }             // not applied: why
}

/// <summary>CurrentName is the Steam name only when it is known (null otherwise, never the login); Shown is what the app calls the account.</summary>
public sealed record ProfileTarget(string SteamId, string Login, string CurrentName, string Shown = null);

public static class ProfilePlan
{
    /// <summary>
    /// The template for each account, with the checks done before anything is sent: tags, length, the login nowhere on the
    /// profile, one custom URL per account. An account with a problem keeps it in Problem and is not changed.
    /// firstN is the {n} of the first target (an account given the template on its own keeps its place in the list).
    /// </summary>
    public static List<ProfileJob> Make(ProfileTemplate t, ProfileFields fields, IReadOnlyList<ProfileTarget> targets, byte[] avatar, bool clearAliases, IEnumerable<string> allLogins = null, int firstN = 1)
    {
        // Every login on this PC counts, not only the target's: a look copied from an account whose name is its login
        // would otherwise publish that login on all the others.
        var logins = (allLogins ?? Enumerable.Empty<string>()).Concat(targets.Select(x => x.Login)).Where(l => l is { Length: >= 3 }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string Leaked(string text, string except) => logins.FirstOrDefault(l => NamePattern.ShowsLogin(text, l) && !NamePattern.ShowsLogin(except, l));
        var jobs = new List<ProfileJob>();
        var nameProblem = fields.HasFlag(ProfileFields.Name) ? NamePattern.Problem(t.Name) : null;
        var urlProblem = fields.HasFlag(ProfileFields.CustomUrl) && !string.IsNullOrWhiteSpace(t.CustomUrl) ? NamePattern.Problem(t.CustomUrl, allowName: false) : null;
        if (urlProblem == null && fields.HasFlag(ProfileFields.CustomUrl) && (targets.Count > 1 || firstN > 1) && !string.IsNullOrWhiteSpace(t.CustomUrl) && !NamePattern.HasN(t.CustomUrl))
            urlProblem = "Each account needs its own custom URL: add {n} to it.";
        if (fields.HasFlag(ProfileFields.Avatar) && avatar == null) fields &= ~ProfileFields.Avatar;
        if (fields.HasFlag(ProfileFields.Privacy) && t.Privacy?.Any != true) fields &= ~ProfileFields.Privacy;

        for (var i = 0; i < targets.Count; i++)
        {
            var a = targets[i];
            string problem = nameProblem ?? urlProblem;
            string name = null, url = null;
            if (fields.HasFlag(ProfileFields.Name) && problem == null)
            {
                name = NamePattern.Render(t.Name, a.CurrentName, firstN + i);
                if (string.IsNullOrEmpty(a.CurrentName) && NamePattern.HasName(t.Name)) problem = "This account's Steam name isn't known yet, so {name} can't be used.";
                else if (name.Length == 0) problem = "The name would be empty.";
                else if (name.Length > NamePattern.MaxName) problem = $"The name would be {name.Length} characters; Steam allows {NamePattern.MaxName}.";
                // Already public in the current name (Steam's default is the login): keeping it is not a new leak.
                else if (Leaked(name, a.CurrentName) is string l) problem = l.Equals(a.Login, StringComparison.OrdinalIgnoreCase) ? "The new name would show this account's username." : "The new name would show the username of another account on this PC.";
            }
            if (fields.HasFlag(ProfileFields.CustomUrl) && problem == null)
            {
                url = NamePattern.Render(t.CustomUrl, null, firstN + i);
                if (url.Length > 0 && !Regex.IsMatch(url, "^[A-Za-z0-9_-]{3,32}$")) problem = "A custom URL is 3 to 32 letters, numbers, - or _.";
                else if (Leaked(url, null) != null) problem = "The custom URL would show a username from this PC.";
            }
            if (problem == null && fields.HasFlag(ProfileFields.RealName) && Leaked(t.RealName, null) != null) problem = "The real name would show a username from this PC.";
            if (problem == null && fields.HasFlag(ProfileFields.Summary) && Leaked(t.Summary, null) != null) problem = "The bio would show a username from this PC.";
            if (problem == null && fields == ProfileFields.None && !clearAliases) problem = "Nothing picked to change.";

            jobs.Add(new ProfileJob
            {
                SteamId = a.SteamId, Shown = a.Shown ?? a.CurrentName, Login = a.Login, Fields = fields, Problem = problem,
                PersonaName = name, CustomUrl = url,
                RealName = fields.HasFlag(ProfileFields.RealName) ? (t.RealName ?? "").Trim() : null,
                Summary = fields.HasFlag(ProfileFields.Summary) ? (t.Summary ?? "").Trim() : null,
                Country = fields.HasFlag(ProfileFields.Country) ? (t.Country ?? "") : null,
                Avatar = fields.HasFlag(ProfileFields.Avatar) ? avatar : null,
                Privacy = fields.HasFlag(ProfileFields.Privacy) ? t.Privacy.Copy() : null,
                ClearAliases = clearAliases,
            });
        }
        return jobs;
    }

    /// <summary>
    /// The {n} for an account given the template on its own: from start up, the first whose name no other account has now
    /// (the list is sorted by name, so a place in it can already be taken).
    /// </summary>
    public static int FreeN(ProfileTemplate t, int start, string currentName, IEnumerable<string> takenNames)
    {
        var taken = new HashSet<string>((takenNames ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)), StringComparer.OrdinalIgnoreCase);
        var n = Math.Max(1, start);
        if (!NamePattern.HasN(t.Name)) return n;
        for (var i = 0; i < 1000 && taken.Contains(NamePattern.Render(t.Name, currentName, n)); i++) n++;
        return n;
    }
}

/// <summary>Steam's edit form overwrites every field: it starts from what the profile has now and changes only what was picked.</summary>
public static class ProfileForm
{
    public static List<KeyValuePair<string, string>> Build(EditConfig cur, ProfileJob job, string sessionId)
    {
        var f = job.Fields;
        var country = f.HasFlag(ProfileFields.Country) ? job.Country ?? "" : cur.Country ?? "";
        var sameCountry = string.Equals(country, cur.Country ?? "", StringComparison.OrdinalIgnoreCase);
        return new()
        {
            KeyValuePair.Create("sessionID", sessionId),
            KeyValuePair.Create("type", "profileSave"),
            KeyValuePair.Create("personaName", f.HasFlag(ProfileFields.Name) && job.PersonaName != null ? job.PersonaName : cur.PersonaName ?? ""),
            KeyValuePair.Create("real_name", f.HasFlag(ProfileFields.RealName) ? job.RealName ?? "" : cur.RealName ?? ""),
            KeyValuePair.Create("summary", f.HasFlag(ProfileFields.Summary) ? job.Summary ?? "" : cur.Summary ?? ""),
            KeyValuePair.Create("country", country),
            // A state and city only mean something inside their country. A restore puts back the copy's (same copy as the country).
            KeyValuePair.Create("state", job.Restore ? job.State ?? "" : sameCountry ? cur.State ?? "" : ""),
            KeyValuePair.Create("city", job.Restore ? job.City ?? "" : sameCountry ? cur.City ?? "" : ""),
            KeyValuePair.Create("customURL", f.HasFlag(ProfileFields.CustomUrl) && job.CustomUrl != null ? job.CustomUrl : cur.CustomUrl ?? ""),
            KeyValuePair.Create("json", "1"),
        };
    }

    /// <summary>
    /// The form fields of the job that differ from the profile now. A name of just {name} is the current name, so nothing
    /// is sent for it. A restore always sends its fields (it also puts back the state and city).
    /// </summary>
    public static ProfileFields Changing(EditConfig cur, ProfileJob job)
    {
        var f = job.Fields;
        if (job.Restore || cur == null) return f;
        static bool Same(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
        if (f.HasFlag(ProfileFields.Name) && (job.PersonaName == null || Same(job.PersonaName, cur.PersonaName))) f &= ~ProfileFields.Name;
        if (f.HasFlag(ProfileFields.RealName) && Same(job.RealName, cur.RealName)) f &= ~ProfileFields.RealName;
        if (f.HasFlag(ProfileFields.Summary) && Same(job.Summary, cur.Summary)) f &= ~ProfileFields.Summary;
        if (f.HasFlag(ProfileFields.Country) && string.Equals(job.Country ?? "", cur.Country ?? "", StringComparison.OrdinalIgnoreCase)) f &= ~ProfileFields.Country;
        if (f.HasFlag(ProfileFields.CustomUrl) && (job.CustomUrl == null || Same(job.CustomUrl, cur.CustomUrl))) f &= ~ProfileFields.CustomUrl;
        return f;
    }

    /// <summary>True when the edit form has to be sent (anything besides the picture, privacy and old names).</summary>
    public static bool NeedsForm(ProfileFields f) => (f & (ProfileFields.Name | ProfileFields.RealName | ProfileFields.Summary | ProfileFields.Country | ProfileFields.CustomUrl)) != 0;
}

// ------------------------------------------------------------ Pictures

/// <summary>
/// Every picture is decoded and drawn again as a new JPEG before it is saved or sent: nothing from the original file
/// (EXIF, GPS, comments, extra data after the image) reaches Steam. Steam takes JPG/PNG/GIF under 1024 KB, at least 184×184.
/// </summary>
public static class AvatarImage
{
    public const int MaxInput = 20 * 1024 * 1024, MinSide = 184, MaxSide = 10000, OutSide = 512, MaxOutput = 1000 * 1024;

    static bool Magic(byte[] b)
    {
        bool S(params byte[] m) => b.Length >= m.Length && m.Select((x, i) => b[i] == x).All(x => x);
        return S(0xFF, 0xD8, 0xFF) || S(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) || S(0x47, 0x49, 0x46, 0x38) || S(0x42, 0x4D);
    }

    /// <summary>A square JPEG ready for Steam, or null with the reason.</summary>
    public static byte[] Prepare(byte[] input, out string problem)
    {
        problem = null;
        if (input == null || input.Length == 0) { problem = "The file is empty."; return null; }
        if (input.Length > MaxInput) { problem = "The picture is over 20 MB."; return null; }
        if (!Magic(input)) { problem = "Use a JPG, PNG, GIF or BMP picture."; return null; }
        try
        {
            using var ms = new MemoryStream(input, false);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            var frame = dec.Frames.FirstOrDefault();
            if (frame == null) { problem = "Couldn't read the picture."; return null; }
            int w = frame.PixelWidth, h = frame.PixelHeight;
            if (w > MaxSide || h > MaxSide) { problem = $"The picture is too big ({w}×{h}); use one under {MaxSide} px."; return null; }
            if (w < MinSide || h < MinSide) { problem = $"The picture is {w}×{h}; Steam needs at least {MinSide}×{MinSide}."; return null; }
            var side = Math.Min(w, h);
            BitmapSource src = new CroppedBitmap(frame, new System.Windows.Int32Rect((w - side) / 2, (h - side) / 2, side, side));
            var outSide = Math.Min(side, OutSide);
            if (outSide != side) src = new TransformedBitmap(src, new ScaleTransform((double)outSide / side, (double)outSide / side));
            // Transparency goes onto the app's dark grey (Steam shows avatars on dark backgrounds), then plain 24-bit color.
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)), null, new System.Windows.Rect(0, 0, outSide, outSide));
                dc.DrawImage(src, new System.Windows.Rect(0, 0, outSide, outSide));
            }
            var rtb = new RenderTargetBitmap(outSide, outSide, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            BitmapSource flat = new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
            for (var q = 92; q >= 50; q -= 14)
            {
                var enc = new JpegBitmapEncoder { QualityLevel = q };
                enc.Frames.Add(BitmapFrame.Create(flat));
                using var outMs = new MemoryStream();
                enc.Save(outMs);
                if (outMs.Length <= MaxOutput) return outMs.ToArray();
            }
            problem = "Couldn't make the picture small enough for Steam.";
            return null;
        }
        catch (Exception) { problem = "Couldn't read the picture (damaged or not an image)."; return null; }
    }
}

// ------------------------------------------------------------ Where the files go

public static class ProfilePaths
{
    public static string Dir => Path.Combine(Paths.Data, "profiles");
    public static string Sessions => Path.Combine(Dir, "sessions");
    public static string Snapshots => Path.Combine(Dir, "snapshots");
    public static string Avatars => Path.Combine(Dir, "avatars");
    public static string Templates => Path.Combine(Dir, "templates.json");
    public static string Known => Path.Combine(Dir, "known.json");

    /// <summary>
    /// True if the folder is a link or junction. The app runs as admin and writes here; SRT_Dados sits wherever the exe is
    /// (often Downloads), so a link planted by any program would send those writes somewhere else.
    /// </summary>
    public static bool Redirected(string dir)
    {
        try
        {
            // From the folder up to SRT_Dados (included); a folder outside it (tests) is checked with its parent only.
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Paths.Data));
            var stop = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? root : Path.GetDirectoryName(full);
            for (var d = new DirectoryInfo(full); d != null; d = d.Parent)
            {
                if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
                if (string.Equals(Path.TrimEndingDirectorySeparator(d.FullName), stop, StringComparison.OrdinalIgnoreCase)) break;
            }
            return false;
        }
        catch { return true; }
    }
}

// ------------------------------------------------------------ Saved sign-ins

public sealed record SessionInfo(string SteamId, DateTime Saved, DateTime Expires)
{
    public bool Valid => Expires > DateTime.Now;
}

/// <summary>
/// One file per account with its refresh token, encrypted with DPAPI (this Windows user only, plus the app's own entropy and
/// the SteamID, so a file copied onto another account's name doesn't open). The file and its folder only let this Windows
/// user in (no inherited access): the app runs as admin, and nothing else on the PC needs to read them.
/// No password, no access token and no cookie is ever written.
/// </summary>
public sealed class SessionStore
{
    readonly string _dir;
    public SessionStore(string dir = null) { _dir = dir ?? ProfilePaths.Sessions; }

    static byte[] Entropy(string steamId) => SHA256.HashData(Encoding.UTF8.GetBytes("CS2IPChanger/ProfileSession/v1/" + steamId));
    string FileOf(string steamId) => Path.Combine(_dir, Valid(steamId) + ".bin");
    static string Valid(string steamId) => Regex.IsMatch(steamId ?? "", @"^7656119\d{10}$") ? steamId : throw new ArgumentException("not a SteamID64");

    static SecurityIdentifier Me => WindowsIdentity.GetCurrent().User;

    /// <summary>Only this user, full control, nothing inherited from the folder above.</summary>
    static FileSecurity FileAcl()
    {
        var s = new FileSecurity();
        s.SetAccessRuleProtection(true, false);
        s.SetOwner(Me);
        s.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl, AccessControlType.Allow));
        return s;
    }

    void EnsureDir()
    {
        if (ProfilePaths.Redirected(_dir)) throw new IOException("the app's data folder goes through a link or junction; sign-ins are only saved in a real folder");
        var d = new DirectoryInfo(_dir);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        sec.SetOwner(Me);
        sec.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (!d.Exists) d.Create(sec); else d.SetAccessControl(sec);
    }

    public void Save(string steamId, string refreshToken)
    {
        Valid(steamId);
        var tok = SteamToken.Read(refreshToken);
        if (tok == null || tok.SteamId != steamId) throw new InvalidOperationException("That sign-in is not for this account.");
        EnsureDir();
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Saved { SteamId = steamId, Token = refreshToken, At = DateTime.Now });
        byte[] blob;
        try { blob = ProtectedData.Protect(plain, Entropy(steamId), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var path = FileOf(steamId);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        // Created with its ACL already on: there is no moment where the file exists with the folder's default access.
        using (var fs = new FileInfo(tmp).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, FileAcl()))
            fs.Write(blob);
        File.Move(tmp, path, true);
        new FileInfo(path).SetAccessControl(FileAcl());
    }

    sealed class Saved { public string SteamId { get; set; } public string Token { get; set; } public DateTime At { get; set; } }

    Saved Read(string steamId)
    {
        var path = FileOf(steamId);
        if (!File.Exists(path) || ProfilePaths.Redirected(_dir)) return null;
        byte[] plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(steamId), DataProtectionScope.CurrentUser);
            var s = JsonSerializer.Deserialize<Saved>(plain);
            return s?.SteamId == steamId ? s : null;
        }
        catch { return null; }   // another Windows user's file, damaged, or not ours: as if there were none
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>The refresh token, only for the moment it is needed. Null when there is none or it has expired.</summary>
    public string Token(string steamId)
    {
        var s = Read(steamId);
        var t = SteamToken.Read(s?.Token);
        return t != null && t.Expires > DateTime.Now ? s.Token : null;
    }

    public SessionInfo Info(string steamId)
    {
        var s = Read(steamId);
        var t = SteamToken.Read(s?.Token);
        return t == null ? null : new SessionInfo(steamId, s.At, t.Expires);
    }

    // A folder that goes through a link: nothing is deleted there (the files behind it are not the app's).
    public bool Forget(string steamId)
    {
        var path = FileOf(steamId);
        if (!File.Exists(path) || ProfilePaths.Redirected(_dir)) return false;
        File.Delete(path);
        return true;
    }

    public int ForgetAll()
    {
        if (!Directory.Exists(_dir) || ProfilePaths.Redirected(_dir)) return 0;
        var n = 0;
        foreach (var f in Directory.GetFiles(_dir, "*.bin").Concat(Directory.GetFiles(_dir, "*.tmp"))) { try { File.Delete(f); n++; } catch { } }
        return n;
    }

    public IEnumerable<string> Accounts() => Directory.Exists(_dir) && !ProfilePaths.Redirected(_dir)
        ? Directory.GetFiles(_dir, "*.bin").Select(Path.GetFileNameWithoutExtension).Where(x => Regex.IsMatch(x, @"^7656119\d{10}$"))
        : Enumerable.Empty<string>();
}

// ------------------------------------------------------------ Copies for undo

/// <summary>How a profile was before the app changed it. The picture sits next to it as a .jpg.</summary>
public sealed class ProfileSnapshot
{
    public string SteamId { get; set; }
    public DateTime At { get; set; }
    public bool Original { get; set; }     // the first copy ever: "Restore original" goes back to it
    public EditConfig Info { get; set; }
    public PrivacyPreset Privacy { get; set; }
    public string Avatar { get; set; }     // file name next to the .json
    public bool AvatarDefault { get; set; } // the profile had Steam's default picture: Restore can't upload it back as itself
}

public sealed class SnapshotStore
{
    readonly string _dir;
    public SnapshotStore(string dir = null) { _dir = dir ?? ProfilePaths.Snapshots; }
    string DirOf(string steamId) => Path.Combine(_dir, Regex.IsMatch(steamId ?? "", @"^\d{17}$") ? steamId : throw new ArgumentException("not a SteamID64"));

    public ProfileSnapshot Save(string steamId, EditConfig info, PrivacyPreset privacy, byte[] avatarJpeg, bool avatarDefault = false)
    {
        // The account's own folder too: a link planted there would send these writes (and Prune's deletes) somewhere else.
        var dir = DirOf(steamId);
        if (ProfilePaths.Redirected(_dir) || ProfilePaths.Redirected(dir)) throw new IOException("the app's data folder goes through a link or junction");
        Directory.CreateDirectory(dir);
        if (ProfilePaths.Redirected(dir)) throw new IOException("the app's data folder goes through a link or junction");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var s = new ProfileSnapshot { SteamId = steamId, At = DateTime.Now, Original = Original(steamId) == null, Info = info, Privacy = privacy, AvatarDefault = avatarDefault };
        if (avatarJpeg != null) { s.Avatar = stamp + ".jpg"; File.WriteAllBytes(Path.Combine(dir, s.Avatar), avatarJpeg); }
        Store.Save(Path.Combine(dir, stamp + ".json"), s);
        Prune(steamId);
        return s;
    }

    public List<ProfileSnapshot> All(string steamId)
    {
        var dir = DirOf(steamId);
        if (!Directory.Exists(dir) || ProfilePaths.Redirected(dir)) return new();
        return Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Store.Load<ProfileSnapshot>(f, () => null)).Where(s => s?.SteamId == steamId).ToList();
    }

    public ProfileSnapshot Original(string steamId) => All(steamId).FirstOrDefault(s => s.Original);
    public ProfileSnapshot Latest(string steamId) => All(steamId).LastOrDefault();

    public byte[] AvatarOf(ProfileSnapshot s)
    {
        if (s?.Avatar == null || s.Avatar.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var dir = DirOf(s.SteamId);
        var p = Path.Combine(dir, s.Avatar);
        return File.Exists(p) && !ProfilePaths.Redirected(dir) ? File.ReadAllBytes(p) : null;
    }

    /// <summary>The original and the 5 newest stay; older in-between copies go.</summary>
    void Prune(string steamId)
    {
        var dir = DirOf(steamId);
        if (ProfilePaths.Redirected(dir)) return;
        var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var original = files.FirstOrDefault(f => Store.Load<ProfileSnapshot>(f, () => null)?.Original == true);
        foreach (var f in files.Where(f => f != original).SkipLast(5))
        {
            try { File.Delete(f); var jpg = Path.ChangeExtension(f, ".jpg"); if (File.Exists(jpg)) File.Delete(jpg); } catch { }
        }
    }

    /// <summary>The job that puts a copy back (every field, the picture, privacy).</summary>
    public ProfileJob RestoreJob(ProfileSnapshot s, string shown)
    {
        var avatar = AvatarOf(s);
        var f = ProfileFields.Name | ProfileFields.RealName | ProfileFields.Summary | ProfileFields.Country | ProfileFields.CustomUrl
                | (avatar != null ? ProfileFields.Avatar : 0) | (s.Privacy?.Any == true ? ProfileFields.Privacy : 0);
        return new ProfileJob
        {
            SteamId = s.SteamId, Shown = shown, Fields = f, Restore = true,
            PersonaName = s.Info?.PersonaName, RealName = s.Info?.RealName ?? "", Summary = s.Info?.Summary ?? "", Country = s.Info?.Country ?? "", CustomUrl = s.Info?.CustomUrl ?? "",
            State = s.Info?.State ?? "", City = s.Info?.City ?? "",
            Avatar = avatar, AvatarIsDefault = s.AvatarDefault, Privacy = s.Privacy,
        };
    }
}

// ------------------------------------------------------------ Applying

public sealed class ProfileResult
{
    public string SteamId { get; init; }
    public string Shown { get; init; }
    public bool Ok { get; init; }
    public bool Skipped { get; init; }
    public bool Partial { get; init; }                // failed after some steps went through: the message says which
    public bool Expired { get; init; }                // the saved sign-in no longer works
    public string Message { get; init; }
}

/// <summary>
/// Applies the jobs one account after another, with a pause between accounts and between steps. Before the first change
/// on an account, its profile is copied (for undo). A 429 waits once and tries again; a second one stops the run, since
/// Steam limits by address and the next accounts would only make it worse.
/// </summary>
public sealed class ProfileApplier
{
    readonly SteamWeb _web;
    readonly Func<string, string> _token;
    readonly SnapshotStore _snaps;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    public TimeSpan AccountGap { get; set; } = TimeSpan.FromSeconds(6);
    public TimeSpan StepGap { get; set; } = TimeSpan.FromSeconds(1.5);

    public ProfileApplier(SteamWeb web, Func<string, string> tokenFor, SnapshotStore snaps, Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _web = web; _token = tokenFor; _snaps = snaps; _delay = delay ?? Task.Delay;
    }

    sealed class StopRun(string why) : Exception(why);

    async Task<T> Retry<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try { return await call(); }
        catch (SteamWebException ex) when (ex.Limited)
        {
            var wait = ex.RetryAfter is TimeSpan t && t > TimeSpan.Zero ? (t > TimeSpan.FromSeconds(90) ? TimeSpan.FromSeconds(90) : t) : TimeSpan.FromSeconds(30);
            await _delay(wait, ct);
            try { return await call(); }
            catch (SteamWebException again) when (again.Limited) { throw new StopRun("Steam is limiting requests. Wait a few minutes and try again."); }
        }
    }

    Task Retry(Func<Task> call, CancellationToken ct) => Retry(async () => { await call(); return true; }, ct);

    public async Task<List<ProfileResult>> Apply(IReadOnlyList<ProfileJob> jobs, Action<ProfileJob, string> step, Action<ProfileResult> done, CancellationToken ct)
    {
        var results = new List<ProfileResult>();
        string stopped = null;
        var first = true;
        foreach (var job in jobs)
        {
            ProfileResult r;
            if (stopped != null) r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Skipped = true, Message = "Not tried: " + stopped };
            else if (job.Problem != null) r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Skipped = true, Message = job.Problem };
            else if (ct.IsCancellationRequested) r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Skipped = true, Message = "Not tried: stopped." };
            else
            {
                var wait = !first;
                first = false;
                // What already went through on this account when a later step fails: the message says so, Undo puts it back.
                var changed = new List<string>();
                string Done(string msg) => changed.Count == 0 ? msg : (job.Restore ? "Restored: " : "Changed: ") + string.Join(", ", changed) + ". Then: " + msg
                    + (job.Restore ? "" : " Right-click > Undo the last change puts it back.");
                try
                {
                    if (wait) await _delay(AccountGap, ct);
                    r = await One(job, changed, s => step?.Invoke(job, s), ct);
                }
                catch (StopRun ex) { stopped = ex.Message; r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Partial = changed.Count > 0, Message = Done(ex.Message) }; }
                catch (SteamWebException ex) { r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Partial = changed.Count > 0, Expired = ex.Expired, Message = Done(Secrets.Clean(ex.Message)) }; }
                catch (OperationCanceledException) { r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Partial = changed.Count > 0, Message = Done("Stopped.") }; stopped = "stopped."; }
                catch (Exception ex) { r = new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Partial = changed.Count > 0, Message = Done("Something went wrong (" + ex.GetType().Name + ").") }; }
            }
            results.Add(r);
            done?.Invoke(r);
        }
        return results;
    }

    /// <summary>The picture on the profile is already this one (compared by SHA-256; unknown counts as different).</summary>
    public static bool SamePicture(byte[] now, byte[] wanted) =>
        now != null && wanted != null && System.Security.Cryptography.SHA256.HashData(now).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(wanted));

    static bool SamePrivacy(PrivacyState a, PrivacyState b) => a.CommentPermission == b.CommentPermission && a.Settings.ToJsonString() == b.Settings.ToJsonString();

    async Task<ProfileResult> One(ProfileJob job, List<string> changed, Action<string> step, CancellationToken ct)
    {
        var token = _token(job.SteamId);
        if (token == null) return new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Skipped = true, Message = "Not signed in: sign in to this account first." };
        step("signing in");
        var s = await Retry(() => CommunitySession.Open(_web, token, job.SteamId, ct), ct);
        token = null;

        step("reading the profile");
        var info = await Retry(() => s.ReadInfo(ct), ct);
        var needPrivacy = job.Fields.HasFlag(ProfileFields.Privacy) || !job.Restore;
        PrivacyState privacy = null;
        if (needPrivacy) { await _delay(StepGap, ct); privacy = await Retry(() => s.ReadPrivacy(ct), ct); }

        // The picture as it is now, after the same re-encoding the app's copies get: the copy for undo, and the way to tell
        // whether the new picture is really another one (Steam re-encodes every upload, so sending the same one again changes it).
        byte[] avatarNow = null;
        var nowDefault = false;
        if (!job.Restore || (job.Fields.HasFlag(ProfileFields.Avatar) && job.Avatar != null))
        {
            try
            {
                var look = await Retry(() => s.ReadLook(ct), ct);
                nowDefault = CommunitySession.IsDefaultAvatar(look.AvatarUrl);
                var raw = await CommunitySession.DownloadAvatar(_web, look.AvatarUrl, ct);
                avatarNow = raw == null ? null : AvatarImage.Prepare(raw, out _);
            }
            catch (SteamWebException ex) when (!ex.Limited) { }
        }
        // The copy for undo comes before any change: name, fields, privacy and the picture as it is now.
        if (!job.Restore) _snaps.Save(job.SteamId, info, privacy == null ? null : PrivacyPreset.From(privacy), avatarNow, nowDefault);
        string note = null;

        // Only what really changes: a template that keeps the name ({name}) and has no picture sends nothing.
        var form = ProfileForm.Changing(info, job);
        if (ProfileForm.NeedsForm(form))
        {
            step("saving the profile");
            await _delay(StepGap, ct);
            await Retry(() => s.SaveInfo(ProfileForm.Build(info, job, s.SessionId), ct), ct);
            if (form.HasFlag(ProfileFields.Name)) changed.Add("name");
            if ((form & (ProfileFields.RealName | ProfileFields.Summary | ProfileFields.Country | ProfileFields.CustomUrl)) != 0) changed.Add("details");
        }
        var sendPicture = job.Fields.HasFlag(ProfileFields.Avatar) && job.Avatar != null && !SamePicture(avatarNow, job.Avatar);
        // Steam's default picture ("?") uploaded back would be a custom picture that only looks like it.
        if (sendPicture && (job.AvatarIsDefault || await IsDefaultPicture(job.Avatar, ct)))
        {
            sendPicture = false;
            if (!nowDefault) note = DefaultPictureNote;
        }
        if (sendPicture)
        {
            step("uploading the picture");
            await _delay(StepGap, ct);
            await Retry(() => s.UploadAvatar(job.Avatar, ct), ct);
            changed.Add("picture");
        }
        if (job.Fields.HasFlag(ProfileFields.Privacy) && job.Privacy?.Any == true && privacy != null && !SamePrivacy(job.Privacy.ApplyTo(privacy), privacy))
        {
            step("setting privacy");
            await _delay(StepGap, ct);
            await Retry(() => s.SetPrivacy(job.Privacy.ApplyTo(privacy), ct), ct);
            changed.Add("privacy");
        }
        if (job.ClearAliases)
        {
            step("clearing old names");
            await _delay(StepGap, ct);
            await Retry(() => s.ClearAliases(ct), ct);
            changed.Add("old names cleared");
        }
        var msg = changed.Count == 0 ? "Nothing to change." : (job.Restore ? "Restored: " : "Changed: ") + string.Join(", ", changed) + ".";
        return new ProfileResult { SteamId = job.SteamId, Shown = job.Shown, Ok = true, Message = note == null ? msg : msg + " " + note };
    }

    public const string DefaultPictureNote = "Steam's default picture can't be set back by the app: pick it in Steam > Edit Profile > Avatar.";

    byte[] _defaultPicture;
    bool _defaultRead;

    /// <summary>The picture is Steam's default one: compared with the default from Steam's own avatar server, read once per run.</summary>
    async Task<bool> IsDefaultPicture(byte[] jpg, CancellationToken ct)
    {
        if (!_defaultRead)
        {
            _defaultRead = true;
            try
            {
                var raw = await CommunitySession.DownloadAvatar(_web, CommunitySession.DefaultAvatarUrl, ct);
                _defaultPicture = raw == null ? null : AvatarImage.Prepare(raw, out _);
            }
            catch (SteamWebException ex) when (!ex.Limited) { }
        }
        return SamePicture(_defaultPicture, jpg);
    }
}

// ------------------------------------------------------------ Keeping secrets out of text

/// <summary>For anything the Profiles code writes to the Log or shows: tokens (JWT) and Steam's session cookies never get through.</summary>
public static class Secrets
{
    static readonly Regex Jwt = new(@"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}", RegexOptions.Compiled);
    static readonly Regex Cookies = new(@"\b(steamLoginSecure|sessionid|steamRefresh_steam|steamMachineAuth\d*)=[^;\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static string Clean(string text) => string.IsNullOrEmpty(text) ? text : Jwt.Replace(Cookies.Replace(text, "$1=(hidden)"), "(token hidden)");
}
