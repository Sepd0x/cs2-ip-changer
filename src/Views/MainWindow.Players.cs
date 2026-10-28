using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SRT.Core;

namespace SRT;

// Players on the servers: flags, following a player, joining them
public partial class MainWindow
{
    readonly ObservableCollection<PlayerFlag> _flags = new();
    DateTime _profilesAt = DateTime.MinValue;
    ServerItem _followServer;

    void LoadFlags()
    {
        if (!_demo) foreach (var f in Store.Load(Paths.File("flags.json"), () => new List<PlayerFlag>()).Where(f => !string.IsNullOrWhiteSpace(f.Name))) _flags.Add(f);
        FlagList.ItemsSource = _flags;
        UpdateFlagsEmpty();
    }

    void SaveFlags()
    {
        if (!_demo) Store.Save(Paths.File("flags.json"), _flags.ToList());
        UpdateFlagsEmpty();
        ApplyFlags();
    }

    void UpdateFlagsEmpty() => FlagsEmpty.Visibility = _flags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    List<PlayerRow> ToRows(ServerItem s, List<A2SPlayer> list) =>
        list?.Select(p => new PlayerRow { Name = p.Name, Score = p.Score, Time = p.Time, Server = s, Flag = FlagFor(p.Name) }).ToList();

    PlayerFlag FlagFor(string name) => _flags.FirstOrDefault(f => f.Matches(name));

    /// <summary>Matches every player on every server to the flags again (after a flag changes or a name comes from a profile).</summary>
    void ApplyFlags()
    {
        foreach (var s in _servers.Where(s => s.Players != null))
        {
            foreach (var p in s.Players) p.Flag = FlagFor(p.Name);
            s.RaiseFlags();
        }
    }

    void SrvPlayers_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServerItem s && s.HasPlayerList) s.Expanded = !s.Expanded;
    }

    /// <summary>Double-click on the open player list must not join the server.</summary>
    static bool InPlayerList(DependencyObject d)
    {
        while (d != null && d is not ListBoxItem)
        {
            if (d is FrameworkElement { Tag: "players" }) return true;
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // ------------------------------------------------------------ Flags (right-click a player)

    static PlayerRow Pl(object sender) => (sender as FrameworkElement)?.DataContext as PlayerRow;

    void PlayerStaff_Click(object sender, RoutedEventArgs e)
    {
        if (Pl(sender) is not PlayerRow p) return;
        var f = p.Flag ?? NewFlag(p.Name);
        f.Label = "Staff";
        f.Color = PlayerFlag.StaffColor;
        SaveFlags();
        Toast($"{p.Name} flagged as Staff. Anyone with this name matches: add the Steam profile in Edit flag to be sure.", true);
    }

    async void PlayerFlag_Click(object sender, RoutedEventArgs e)
    {
        if (Pl(sender) is not PlayerRow p) return;
        await EditFlag(p.Flag, p.Name);
    }

    void PlayerFollow_Click(object sender, RoutedEventArgs e)
    {
        if (Pl(sender) is not PlayerRow p) return;
        var f = p.Flag;
        if (f == null)
        {
            f = NewFlag(p.Name);
            f.Label = "Followed";
            f.Color = PlayerFlag.Colors[3];
        }
        SetFollow(f, !f.Follow);
    }

    void PlayerCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Pl(sender) is not PlayerRow p) return;
        try { Clipboard.SetText(p.Name); Toast("Name copied.", true); } catch { Toast("Couldn't copy. Try again.", false); }
    }

    void PlayerUnflag_Click(object sender, RoutedEventArgs e) { if (Pl(sender)?.Flag is PlayerFlag f) RemoveFlag(f); }

    PlayerFlag NewFlag(string name)
    {
        var f = new PlayerFlag { Name = name.Trim(), Added = DateTime.Now };
        _flags.Add(f);
        return f;
    }

    void SetFollow(PlayerFlag f, bool on)
    {
        f.Follow = on;
        f.SeenOn = null;
        SaveFlags();
        Toast(on ? $"Following {f.Shown}. You get a notice when they show up on another server in your list."
                 : $"You no longer follow {f.Shown}.", true);
        if (on) _ = CheckFollowed();
    }

    void RemoveFlag(PlayerFlag f)
    {
        _flags.Remove(f);
        if (_followServer != null && f.SeenOn == _followServer.Key) HideFollowBar();
        SaveFlags();
        Toast($"Flag removed from {f.Shown}.", true);
    }

    /// <summary>Flag name, color and (optionally) the Steam profile. existing == null adds a new flag for that name.</summary>
    async Task EditFlag(PlayerFlag existing, string name)
    {
        DialogInput.Text = existing?.Label ?? "";
        DialogInput.Tag = "E.g. Staff, Cheater, Friend";
        DialogInput2.Text = existing?.SteamId ?? "";
        DialogInput2.Tag = "steamcommunity.com/id/… or SteamID64";
        var who = existing?.Shown ?? name;
        if (!await Ask($"Flag {who}", "Stays on this PC only. Without a Steam profile, anyone with this name matches.", "Save", "Cancel",
                inputs: 2, label1: "Flag", label2: "Steam profile (optional)", color: existing?.Color ?? PlayerFlag.StaffColor)) return;
        var label = string.IsNullOrWhiteSpace(DialogInput.Text) ? "Staff" : DialogInput.Text.Trim();
        var color = _dialogColor;
        var profile = DialogInput2.Text.Trim();
        string id = null;
        if (profile.Length > 0)
        {
            id = await ResolveProfile(profile);
            if (id == null) { Toast("Couldn't read that profile. Use a steamcommunity.com link or a SteamID64.", false); return; }
        }
        var f = existing ?? (id != null ? _flags.FirstOrDefault(x => x.SteamId == id) : null) ?? NewFlag(name);
        f.Label = label;
        if (color != null) f.Color = color;
        if (f.SteamId != id) { f.SteamId = id; f.Persona = null; }
        SaveFlags();
        if (id != null) await RefreshProfiles(new[] { f });
        Toast($"{f.Shown}: {f.Label}." + (id == null ? " Matched by name." : ""), true);
    }

    async Task<string> ResolveProfile(string input)
    {
        if (_demo) return PlayerMatch.ParseProfile(input).SteamId ?? (PlayerMatch.ParseProfile(input).Vanity != null ? "76561198000000042" : null);
        try { return await Steam.ResolveProfile(input, _s.SteamApiKey); }
        catch (Exception ex) { Log.Warn("Steam profile: " + ex.Message); return null; }
    }

    /// <summary>Current Steam name and, with public game details, the server from the profile.</summary>
    async Task RefreshProfiles(IEnumerable<PlayerFlag> list)
    {
        var withId = list.Where(f => f.SteamId != null).ToList();
        if (_demo || withId.Count == 0) return;
        try
        {
            var got = await Steam.FetchSummaries(withId.Select(f => f.SteamId).Distinct().ToList(), _s.SteamApiKey);
            var renamed = false;
            foreach (var f in withId)
            {
                if (!got.TryGetValue(f.SteamId, out var x)) continue;
                if (!string.IsNullOrWhiteSpace(x.Persona) && x.Persona != f.Persona)
                {
                    if (f.Persona != null) Log.Info($"Flagged player {f.Persona} is now called {x.Persona}.");
                    f.Persona = x.Persona;
                    renamed = true;
                }
                f.ProfileServer = PlayerMatch.ServerKey(x.GameServer);
            }
            if (renamed) SaveFlags();
        }
        catch (Exception ex) { Log.Warn("Steam profiles: " + ex.Message); }
    }

    // ------------------------------------------------------------ Settings > Player flags

    static PlayerFlag Fl(object sender) => (sender as FrameworkElement)?.DataContext as PlayerFlag;

    void FlagFollow_Click(object sender, RoutedEventArgs e) { if (Fl(sender) is PlayerFlag f) SetFollow(f, f.Follow); }
    async void FlagEdit_Click(object sender, RoutedEventArgs e) { if (Fl(sender) is PlayerFlag f) await EditFlag(f, f.Name); }
    void FlagRemove_Click(object sender, RoutedEventArgs e) { if (Fl(sender) is PlayerFlag f) RemoveFlag(f); }

    async void FlagAdd_Click(object sender, RoutedEventArgs e)
    {
        DialogInput.Text = "";
        DialogInput.Tag = "steamcommunity.com/id/… or SteamID64";
        DialogInput2.Text = "";
        DialogInput2.Tag = "E.g. Staff, Cheater, Friend";
        if (!await Ask("Add a flag by profile", "The name comes from the Steam profile and follows name changes. Stays on this PC only.", "Add", "Cancel",
                inputs: 2, label1: "Steam profile", label2: "Flag", color: PlayerFlag.StaffColor)) return;
        var id = await ResolveProfile(DialogInput.Text);
        if (id == null) { Toast("Couldn't read that profile. Use a steamcommunity.com link or a SteamID64.", false); return; }
        var label = string.IsNullOrWhiteSpace(DialogInput2.Text) ? "Staff" : DialogInput2.Text.Trim();
        var f = _flags.FirstOrDefault(x => x.SteamId == id);
        if (f == null)
        {
            string persona = _demo ? "demo_player" : null;
            if (!_demo)
            {
                try { (await Steam.FetchSummaries(new[] { id }, _s.SteamApiKey)).TryGetValue(id, out var x); persona = x?.Persona; }
                catch (Exception ex) { Log.Warn("Steam profile: " + ex.Message); }
            }
            if (string.IsNullOrWhiteSpace(persona)) { Toast("Couldn't read the name on that profile. Try again in a minute.", false); return; }
            f = NewFlag(persona);
            f.SteamId = id;
            f.Persona = persona;
        }
        f.Label = label;
        if (_dialogColor != null) f.Color = _dialogColor;
        SaveFlags();
        Toast($"{f.Shown}: {f.Label}.", true);
    }

    // ------------------------------------------------------------ Following

    /// <summary>The server CS2 last joined, from its console. Null when CS2 is closed or the console isn't readable.</summary>
    string CurrentServer => _demo || !Steam.Cs2Running ? null : _consoleServer;

    /// <summary>After each refresh: where each followed player is now (the profile's server first, then the name).</summary>
    async Task CheckFollowed()
    {
        var followed = _flags.Where(f => f.Follow).ToList();
        if (followed.Count == 0) return;
        // With a key one call covers every profile; without one, the public XML is read less often.
        if ((DateTime.Now - _profilesAt).TotalSeconds >= (string.IsNullOrWhiteSpace(_s.SteamApiKey) ? 120 : 25))
        {
            _profilesAt = DateTime.Now;
            await RefreshProfiles(followed);
        }
        var mine = CurrentServer;
        foreach (var f in followed)
        {
            var byProfile = f.ProfileServer != null ? _servers.FirstOrDefault(s => s.Key == f.ProfileServer) : null;
            var at = byProfile ?? _servers.FirstOrDefault(s => s.Players?.Any(p => p.Flag == f) == true);
            if (at == null)
            {
                // Gone for a while: the next time they show up counts as new.
                if (f.SeenOn != null && DateTime.Now - f.SeenAt > TimeSpan.FromMinutes(5)) f.SeenOn = null;
                continue;
            }
            f.SeenAt = DateTime.Now;
            if (at.Key == f.SeenOn) continue;
            f.SeenOn = at.Key;
            if (at.Key == mine) continue;
            OnFollowedFound(f, at, byProfile != null);
        }
    }

    void OnFollowedFound(PlayerFlag f, ServerItem at, bool byProfile)
    {
        _followServer = at;
        var how = byProfile ? "" : f.SteamId == null ? " (matched by name)" : " (by name)";
        FollowText.Text = $"{f.Shown} ({f.Label}) is on {at.Title}{how}.";
        FollowBar.Visibility = Visibility.Visible;
        System.Media.SystemSounds.Asterisk.Play();
        Log.Info($"Followed player {f.Shown} is on {at.Key} ({at.Title})" + (byProfile ? ", from the Steam profile." : ", by name."));
        if (_s.AutoJoinFollowed) _ = AutoJoinFollowed(f, at);
        else if (!PageServers.IsVisible) Toast($"{f.Shown} is on {at.Title}. Join from Servers.", Tone.Info);   // on Servers the bar says it
    }

    async Task AutoJoinFollowed(PlayerFlag f, ServerItem at)
    {
        if (_rejoining || _changingIp || _switching || Dialog.Visibility == Visibility.Visible) return;
        // With CS2 open the app can't be sure you're not mid-match, so it always asks.
        if (!_demo && Steam.Cs2Running && !await Ask("Join the player you follow?", $"{f.Shown} is on {at.Title}. CS2 is open: if you're on a server, joining leaves it.", "Join", "Stay")) return;
        Log.Info($"Auto-join: following {f.Shown} to {at.Key}.");
        HideFollowBar();
        await Join(at);
    }

    async void FollowJoin_Click(object sender, RoutedEventArgs e)
    {
        var s = _followServer;
        HideFollowBar();
        if (s != null && _servers.Contains(s)) await Join(s);
    }

    void FollowDismiss_Click(object sender, RoutedEventArgs e) => HideFollowBar();

    void HideFollowBar() { FollowBar.Visibility = Visibility.Collapsed; _followServer = null; }
}
