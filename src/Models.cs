using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SRT.Core;

namespace SRT;

public sealed partial class Settings
{
    public string Mode { get; set; } = "servers";         // "servers" = list only · "all" = whole PC
    public string EgressAdapterId { get; set; }           // null = automatic USB phone
    public string EgressSsid { get; set; }                // Wi-Fi adapter: only valid when connected to this hotspot
    public bool LockCs2 { get; set; } = true;             // cs2.exe can only go out through the protected connection
    public bool GameMode { get; set; } = true;            // CS2 open = whole PC through the phone connection
    public bool SteamOnEgress { get; set; } = true;       // Steam talks to Valve through the same connection as CS2
    public string Cs2Path { get; set; }
    public bool KeepPcOffPhone { get; set; } = true;
    public string ChangeMethod { get; set; } = "data";    // "data" | "airplane"
    public int OffSeconds { get; set; } = 4;
    public int MaxAttempts { get; set; } = 3;
    public bool RequireNew16 { get; set; }               // Change IP keeps trying until the /16 differs from the IP before
    public bool CheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string UpdateLatest { get; set; }              // highest version seen on GitHub at the last check
    public string SteamApiKey { get; set; }
    public bool LaunchCs2 { get; set; }
    public bool SteamSilent { get; set; }
    public bool SteamOffline { get; set; }
    public bool StreamerMode { get; set; }
    public bool AutoQuery { get; set; } = true;
    public bool CleanupOnExit { get; set; }
    public int DefaultPort { get; set; } = 27015;
    public bool LegacyCleaned { get; set; }
    public string LastHomeIp { get; set; }
    public string LastServer { get; set; }                // IP:port of the last server you joined through the app
    public DateTime? LastServerAt { get; set; }
    public string AccountSort { get; set; } = "recent";
    public List<IpHistoryEntry> IpHistory { get; set; } = new();
}

public sealed class IpHistoryEntry
{
    public DateTime Time { get; set; }
    public string Ip { get; set; }
    public string Isp { get; set; }
    public string Reason { get; set; }
    [JsonIgnore] public string TimeText => Time.ToString(Time.Date == DateTime.Today ? "HH:mm" : "d MMM", System.Globalization.CultureInfo.InvariantCulture);
    [JsonIgnore] public string ReasonText => Reason switch { "trocado" => "after changing", "detetado" => "at startup", _ => Reason };
}

public sealed class CheckItem : Observable
{
    bool? _ok; bool _waiting; string _detail;
    public string Title { get; init; }
    public string Help { get; init; }
    public bool? Ok { get => _ok; set { if (Set(ref _ok, value)) { Raise(nameof(Glyph)); Raise(nameof(Brush)); } } }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    /// <summary>Not OK yet, but safe: blocked while waiting for the phone (amber, not red).</summary>
    public bool Waiting { get => _waiting; set { if (Set(ref _waiting, value)) Raise(nameof(Brush)); } }
    public string Glyph => Ok switch { true => "", false => "", _ => "" };
    public Brush Brush => Waiting ? Ui.Warn : Ok switch { true => Ui.Good, false => Ui.Bad, _ => Ui.Faint };
}

// ------------------------------------------------------------ Servers

public sealed class ServerItem : Observable
{
    public string Ip { get; set; }
    public int Port { get; set; }
    public string Label { get; set; }
    bool _fav;
    public bool Favorite { get => _fav; set { if (Set(ref _fav, value)) Raise(nameof(StarGlyph)); } }

    string _name, _map, _players, _ping, _route = "—";
    int _pingMs = -1, _playerCount, _max;
    bool? _protected;
    bool _isLast;
    DateTime? _lastAt;
    List<string> _playerNames;

    /// <summary>The last server you joined through the app (gets a different color in the list).</summary>
    [JsonIgnore] public bool IsLast { get => _isLast; set { if (Set(ref _isLast, value)) Raise(nameof(LastText)); } }
    [JsonIgnore] public DateTime? LastAt { get => _lastAt; set { if (Set(ref _lastAt, value)) Raise(nameof(LastText)); } }
    [JsonIgnore] public string LastText => !IsLast ? null : LastAt == null ? "Last server you joined" : "You played here " + Fmt.Ago(LastAt);

    [JsonIgnore] public string Key => $"{Ip}:{Port}";
    [JsonIgnore] public string StarGlyph => Favorite ? "" : "";
    [JsonIgnore] public string Title => !string.IsNullOrWhiteSpace(Label) ? Label : !string.IsNullOrWhiteSpace(ServerName) ? ServerName : "Server not responding";
    [JsonIgnore] public string ServerName { get => _name; set { if (Set(ref _name, value)) Raise(nameof(Title)); } }
    [JsonIgnore] public string Map { get => _map; set => Set(ref _map, value); }
    [JsonIgnore] public string PlayersText { get => _players; set => Set(ref _players, value); }
    [JsonIgnore] public int PlayerCount { get => _playerCount; set { if (Set(ref _playerCount, value)) Raise(nameof(Fill)); } }
    [JsonIgnore] public int MaxPlayers { get => _max; set { if (Set(ref _max, value)) { Raise(nameof(Fill)); Raise(nameof(HasPlayers)); } } }
    [JsonIgnore] public bool HasPlayers => MaxPlayers > 0;
    [JsonIgnore] public double Fill => MaxPlayers > 0 ? Math.Min(1, (double)PlayerCount / MaxPlayers) * 60 : 0;
    [JsonIgnore] public string PingText { get => _ping; set => Set(ref _ping, value); }
    [JsonIgnore] public int PingMs { get => _pingMs; set { if (Set(ref _pingMs, value)) Raise(nameof(PingBrush)); } }
    [JsonIgnore] public Brush PingBrush => PingMs < 0 ? Ui.Faint : PingMs < 60 ? Ui.Good : PingMs < 120 ? Ui.Warn : Ui.Bad;
    [JsonIgnore] public string RouteText { get => _route; set => Set(ref _route, value); }
    [JsonIgnore] public bool? Protected { get => _protected; set { if (Set(ref _protected, value)) { Raise(nameof(RouteBrush)); Raise(nameof(RouteBg)); } } }
    [JsonIgnore] public Brush RouteBrush => Protected switch { true => Ui.Good, false => Ui.Bad, _ => Ui.Warn };
    [JsonIgnore] public Brush RouteBg => Protected switch { true => Ui.GoodSoft, false => Ui.BadSoft, _ => Ui.WarnSoft };
    [JsonIgnore] public List<string> PlayerNames { get => _playerNames; set { if (Set(ref _playerNames, value)) Raise(nameof(Tip)); } }
    [JsonIgnore] public string Tip
    {
        get
        {
            var head = $"{Key}\nDouble-click to join through Steam.";
            if (PlayerNames == null || PlayerNames.Count == 0) return head;
            return head + "\n\nPlayers now:\n" + string.Join("\n", PlayerNames.Take(24)) + (PlayerNames.Count > 24 ? "\n…" : "");
        }
    }
}

// ------------------------------------------------------------ Steam accounts

public sealed class AccountMeta
{
    public string Note { get; set; }
    public string Tags { get; set; }
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    public bool ExcludeQuick { get; set; }      // skipped: left out of quick switch
    public string LastServer { get; set; }      // IP:port of the last server you joined (through the app) with this account
    public string LastServerName { get; set; }
    public DateTime? LastServerAt { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public DateTime? LastUsed { get; set; }
    public string AvatarPath { get; set; }
    public SteamProfile Profile { get; set; }
}

public sealed class AccountItem : Observable
{
    public LocalAccount Local { get; }
    public AccountMeta Meta { get; }
    bool _active, _streamer, _busy;
    ImageSource _avatar;

    public AccountItem(LocalAccount local, AccountMeta meta)
    {
        Local = local; Meta = meta;
        LoadAvatar();
    }

    public string SteamId => Local.SteamId;
    public string Login => Local.AccountName;
    public string LoginShown => Streamer ? "••••••••" : Login;
    public string Name => Meta.Profile?.Persona ?? Local.PersonaName ?? Login;
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

    public bool Active { get => _active; set { if (Set(ref _active, value)) { Raise(nameof(CardBorder)); } } }
    public bool Streamer { get => _streamer; set { if (Set(ref _streamer, value)) { Raise(nameof(LoginShown)); Raise(nameof(Tip)); } } }
    public bool Busy { get => _busy; set => Set(ref _busy, value); }
    public bool Favorite { get => Meta.Favorite; set { Meta.Favorite = value; Raise(nameof(Favorite)); Raise(nameof(StarGlyph)); } }
    public string StarGlyph => Favorite ? "" : "";
    public string Note => Meta.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Meta.Note);
    public string Tags => Meta.Tags;
    public bool HasTags => !string.IsNullOrWhiteSpace(Meta.Tags);

    public ImageSource Avatar { get => _avatar; private set => Set(ref _avatar, value); }
    public bool HasAvatar => Avatar != null;

    public void LoadAvatar()
    {
        var path = Meta.AvatarPath;
        if (path == null || !File.Exists(path)) path = Steam.LocalAvatar(SteamId);
        if (path == null || !File.Exists(path)) { Avatar = null; Raise(nameof(HasAvatar)); return; }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 128;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            Avatar = bmp;
        }
        catch { Avatar = null; }
        Raise(nameof(HasAvatar));
    }

    SteamProfile P => Meta.Profile;
    public bool Checked => P != null;
    public bool HasVac => P?.VacBans > 0;
    public bool HasGameBan => P?.GameBans > 0;
    public bool HasTradeBan => P?.TradeBan != null;
    public bool HasCommunityBan => P?.CommunityBan == true;
    public bool Limited => P?.Limited == true;
    public bool Private => P?.Private == true;
    public bool Clean => Checked && !HasVac && !HasGameBan && !HasTradeBan && !HasCommunityBan;
    public string VacText => P == null ? null : P.VacBans > 1 ? $"{P.VacBans} VAC" : "VAC";
    public string GameBanText => P == null ? null : P.GameBans > 1 ? $"{P.GameBans} game bans" : "game ban";
    public string BanAgeText => P?.DaysSinceLastBan is int d ? $"last {d} d ago" : null;
    public string LevelText => P?.Level is int l ? $"Level {l}" : null;
    public string HoursText => P?.Cs2Hours is double h ? $"{h:0} h CS2" : null;
    public bool HasLevel => P?.Level != null;
    public bool HasHours => P?.Cs2Hours != null;

    public bool InCooldown => Meta.CooldownUntil > DateTime.Now;
    public bool Excluded => Meta.ExcludeQuick;
    public string QuickGlyph => "";
    public Brush QuickBrush => Excluded ? Ui.LineStrong : Ui.Muted;
    public string QuickTip => Excluded
        ? "Skipped in quick switch. Click to include it again."
        : "In quick switch. Click to skip this account.";
    public string QuickMenuText => Excluded ? "Include in quick switch" : "Skip in quick switch";
    public bool HasLastServer => Meta.LastServer != null;
    public string LastServerText => Meta.LastServer == null ? null
        : $"Last: {(string.IsNullOrWhiteSpace(Meta.LastServerName) ? Meta.LastServer : Meta.LastServerName)} · {Fmt.Ago(Meta.LastServerAt)}";
    /// <summary>Eligible for quick switch: not skipped, no cooldown, no VAC/game ban.</summary>
    public bool Usable => !Excluded && !InCooldown && !HasVac && !HasGameBan;
    public string CooldownText => InCooldown ? "Cooldown " + Fmt.Left(Meta.CooldownUntil.Value - DateTime.Now) : null;

    public Brush CardBorder => Active ? Ui.Accent : Ui.Line;

    public string Tip
    {
        get
        {
            var lines = new List<string> { Name, $"Login: {LoginShown}", $"SteamID64: {SteamId}" };
            if (P != null)
            {
                lines.Add("");
                lines.Add(Clean ? "No bans on record on the public profile." : "Bans on record:");
                if (HasVac) lines.Add($"• {P.VacBans} VAC ban(s)" + (BanAgeText != null ? $", {BanAgeText}" : ""));
                if (HasGameBan) lines.Add($"• {P.GameBans} game ban(s)");
                if (HasTradeBan) lines.Add($"• Trade ban: {P.TradeBan}");
                if (HasCommunityBan) lines.Add("• Steam Community ban");
                if (Limited) lines.Add("• Limited account (no 5 USD spent)");
                if (Private) lines.Add("• Private profile: hours and level may not show");
                if (P.MemberSince != null) lines.Add($"Member since {P.MemberSince}");
                lines.Add($"Checked {Fmt.Ago(P.Checked)}");
            }
            else lines.Add("\nNo status yet: click \"Refresh\".");
            if (InCooldown) lines.Add($"\nCooldown set by you until {Fmt.When(Meta.CooldownUntil)}");
            if (Excluded) lines.Add("Skipped in quick switch.");
            if (Meta.LastServer != null) lines.Add($"Last server: {Meta.LastServerName ?? Meta.LastServer} ({Meta.LastServer}), {Fmt.When(Meta.LastServerAt)}");
            if (HasNote) lines.Add("\nNote: " + Note);
            lines.Add("\nClick Log in to switch. Right-click for more options.");
            return string.Join("\n", lines);
        }
    }

    public void RefreshAll()
    {
        foreach (var n in new[]
        {
            nameof(Name), nameof(Initial), nameof(Checked), nameof(HasVac), nameof(HasGameBan), nameof(HasTradeBan), nameof(HasCommunityBan),
            nameof(Limited), nameof(Private), nameof(Clean), nameof(VacText), nameof(GameBanText), nameof(BanAgeText), nameof(LevelText),
            nameof(HoursText), nameof(HasLevel), nameof(HasHours), nameof(InCooldown), nameof(CooldownText), nameof(Tip), nameof(Note),
            nameof(HasNote), nameof(Tags), nameof(HasTags), nameof(StarGlyph), nameof(Favorite), nameof(Excluded), nameof(Usable), nameof(QuickGlyph), nameof(QuickBrush), nameof(QuickTip), nameof(QuickMenuText),
            nameof(HasLastServer), nameof(LastServerText),
        }) Raise(n);
    }

    public void TickCooldown()
    {
        Raise(nameof(InCooldown));
        Raise(nameof(CooldownText));
        Raise(nameof(Usable));
    }
}

public static class Ui
{
    static Brush B(string key) => (Brush)System.Windows.Application.Current.Resources[key];
    public static Brush Good => B("Good");
    public static Brush GoodSoft => B("GoodSoft");
    public static Brush Warn => B("Warn");
    public static Brush WarnSoft => B("WarnSoft");
    public static Brush Bad => B("Bad");
    public static Brush BadSoft => B("BadSoft");
    public static Brush Faint => B("Faint");
    public static Brush Text => B("Text");
    public static Brush Accent => B("Accent");
    public static Brush Line => B("Line");
    public static Brush LineStrong => B("LineStrong");
    public static Brush Muted => B("Muted");
    public static Brush Info => B("Info");
}
