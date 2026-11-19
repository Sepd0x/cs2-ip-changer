using System.Windows;
using SRT.Core;

namespace SRT;

// Regions: which CS2 matchmaking regions cs2.exe can reach (Firewall rule on Valve's relays, see Core/Network/Regions.cs)
public partial class MainWindow
{
    System.Collections.ObjectModel.ObservableCollection<RegionItem> _regions => _regionGame.Rows;
    List<RegionInfo> _regionInfos => _regionGame?.Infos;
    SdrConfig _sdr => _regionGame?.Config;
    readonly SemaphoreSlim _regionGate = new(1);
    bool _regionsReady;

    /// <summary>Once, when the window loads (the page is in the tree even while hidden): reads the list and puts the rule back.</summary>
    async void PageRegions_Loaded(object sender, RoutedEventArgs e)
    {
        if (_regionsReady) return;
        _regionsReady = true;
        InitRegionGames();
        if (_demo) return;   // the demo fills the page itself
        // After Restore network the rule is gone; it comes back with protection (Run leak test or the next start).
        _queryTimer.Tick += async (_, _) => await Task.WhenAll(_regionGames.Select(KeepRegionRule));
        // Nothing blocked here, but the rule is machine-wide: another copy of the app (or a reset settings.json) may have left one.
        if (!_restored)
        {
            var remaining = await Task.Run(Firewall.Remaining);
            foreach (var state in _regionGames.Where(g => g.Settings.BlockedRegions.Count == 0 && Regions.RuleLeft(remaining, game: g.Game)))
            {
                if (_restored) return;
                await Task.Run(() => Firewall.Remove(state.Game.RuleName));
                Log.Warn($"Regions: removed a leftover {state.Game.Name} rule from another copy of the app.");
            }
        }
        if (_restored) return;
        _regionRulesReady = true;
        RegionGamePicker.IsEnabled = true;
        await Task.WhenAll(_regionGames.Where(g => g == _cs2Regions || g.Settings.BlockedRegions.Count > 0 || g.PendingPreset != null)
            .Select(g => LoadRegions(atStart: true, state: g)));
    }

    async void PageRegions_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!PageRegions.IsVisible || _demo || !_regionRulesReady) return;
        await OpenRegionGame(_regionGame);
    }

    async void RegionsRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_regionRulesReady) return;
        var state = _regionGame;
        await LoadRegions(atStart: false, asked: true, state: state);
        // Keep the Valve warning visible even when the ping finishes before it expires.
        if (state == _regionGame) await PingRegions(quiet: state.Config?.FromCache == true);
    }

    /// <summary>Valve's list (or the saved copy), the rows, and the rule rebuilt when the list's revision changed.</summary>
    async Task LoadRegions(bool atStart, bool quiet = false, bool asked = false, RegionGameState state = null)
    {
        state ??= _regionGame;
        if (state.Loading || _restored) return;
        state.Loading = true;
        state.TriedAt = DateTime.Now;
        if (state == _regionGame && state.Config == null) ShowRegionsStatus();
        try
        {
            var (cfg, infos) = await Task.Run(async () =>
            {
                var config = await Regions.Load(game: state.Game);
                return (config, config == null ? null : Regions.Group(config));
            });
            if (_restored) return;
            if (cfg == null)
            {
                if (state != _regionGame) return;
                if (state.Config == null)
                {
                    state.Loading = false;
                    RegionHeader.Visibility = Visibility.Hidden;
                    ShowRegionsStatus();
                    if (!atStart && !quiet) Toast(Loc.T("Couldn't reach Valve. Check the internet connection and press Refresh again."), false);
                }
                else if (!atStart && !quiet) Toast(Loc.F("Couldn't reach Valve: still using the list from {0}.", Fmt.When(state.Config.FetchedAt)), false);
                return;
            }
            // Refresh offline said nothing about Valve (review C17); opening the page offline stays quiet (it tries every time)
            if (cfg.FromCache && asked && state == _regionGame) Toast(Loc.F("Couldn't reach Valve: still using the list saved on {0}.", Fmt.When(cfg.FetchedAt)), false);
            var changed = state.Config != null && state.Config.Revision != cfg.Revision;
            state.Config = cfg;
            // Same list as before (the hourly read, mostly): the rows stay as they are, so the list doesn't jump to the top.
            if (!Regions.SameRows(state.Infos, infos)) ShowRegionRows(state, infos);
            if (await ApplyPendingRegionPreset(state)) return;
            var kept = KeepShortcut(state, atStart, quiet);
            // Valve adds and moves relays: a new revision means new IPs to block for the same regions.
            var settings = state.Settings;
            if (settings.BlockedRegions.Count > 0 && (atStart || changed || kept || cfg.Revision != settings.RegionsRevision))
            {
                if (cfg.Revision != settings.RegionsRevision && settings.RegionsRevision != 0)
                    Log.Info($"Regions: {state.Game.Name}'s relay list changed (revision {cfg.Revision}), rebuilding the rule.");
                await ApplyRegions(state, quiet: true);
            }
            else ShowRegionsStatus();
        }
        finally { state.Loading = false; ShowRegionsStatus(); }
    }

    /// <summary>After a shortcut (Only this region, Closest 3, your continent only, Ping under): cities Valve added since then stay off too. True when the set grew.</summary>
    bool KeepShortcut(RegionGameState state, bool atStart, bool quiet)
    {
        var settings = state.Settings;
        if (settings.RegionsKeep is not { Count: > 0 } keep) return false;
        var now = Regions.KeepOnly(state.Infos, keep);
        if (now == null)
        {
            if (!quiet) Log.Warn("Regions: none of the regions your shortcut kept on is in Valve's list any more. The saved set stays.");
            if (!atStart && !quiet && state == _regionGame) Toast(Loc.T("The regions your shortcut kept on aren't in Valve's list any more. Pick again."), false);
            return false;
        }
        var added = now.Except(settings.BlockedRegions).ToList();
        if (added.Count == 0) return false;
        settings.BlockedRegions = settings.BlockedRegions.Union(added).OrderBy(x => x, StringComparer.Ordinal).ToList();
        SaveSettings();
        foreach (var r in state.Rows) r.Allowed = !settings.BlockedRegions.Contains(r.Id);
        Log.Info($"Regions: Valve added {Fmt.Count(added.Count, "region")}, kept off by your shortcut: {string.Join(", ", added)}.");
        return true;
    }

    /// <summary>Fills the list, keeping the pings already measured.</summary>
    void ShowRegionRows(RegionGameState state, List<RegionInfo> infos)
    {
        state.Infos = infos;
        var old = state.Rows.ToDictionary(r => r.Id, r => r.PingMs);
        var blocked = state.Settings.BlockedRegions.ToHashSet();
        state.Rows.Clear();
        foreach (var info in infos)
            state.Rows.Add(new RegionItem { Info = info, Allowed = !blocked.Contains(info.Id), PingMs = old.GetValueOrDefault(info.Id, -2) });
        ShowRegionsStatus();
    }

    // ------------------------------------------------------------ Ping

    async Task PingRegions(bool quiet)
    {
        var state = _regionGame;
        if (_demo || state.Pinging || state.Infos == null) return;
        var local = PingVia?.LocalIp;
        if (local == null)
        {
            // Through the phone while protection is on (the number has to be what the game feels, never from the home IP), else at home
            if (!quiet) Toast(PingedOnHome ? Loc.T("No connection to measure the ping through.") : NoLinkWhy(_last) + Loc.T(" With protection on, the ping is only measured through the phone. Turning regions on and off still works."), false);
            ShowRegionsStatus();
            return;
        }
        state.Pinging = true;
        var pingedHome = PingedOnHome;
        var homeIsPhone = pingedHome && _last != null && HomeIsPhone(_last);
        ShowRegionsStatus();
        try
        {
            var pings = await Regions.PingAll(state.Infos, local);
            foreach (var r in state.Rows) r.PingMs = pings.GetValueOrDefault(r.Id, -1);
            state.PingedAt = DateTime.Now;
            state.PingedHome = pingedHome;
            state.HomeIsPhone = homeIsPhone;
            state.View?.Refresh();
            if (!quiet && state == _regionGame) Toast(Loc.F("{0} of {1} regions answered.", pings.Count(p => p.Value >= 0), pings.Count), pings.Any(p => p.Value >= 0));
        }
        finally
        {
            state.Pinging = false;
            ShowRegionsStatus();
        }
    }

    /// <summary>
    /// The path CS2 uses: the home connection with protection off or in No phone mode (beta.10: Regions doesn't wait for the cable),
    /// the phone while protection is on. With protection on and the phone not there, nothing is measured: the ping would leave from
    /// the home IP while you asked for the phone. The page says where the ping was measured.
    /// </summary>
    Adapter PingVia => PingOnHome(_s.NoPhone, _protOn) ? HomeAdapter : _last?.Egress;

    public static bool PingOnHome(bool noPhone, bool protOn) => noPhone || !protOn;

    /// <summary>
    /// The game uses the home connection on purpose: No phone mode, or protection off with Settings > Use the app with protection off.
    /// Then Servers, Join and Log in with CS2 go ahead on the home connection instead of waiting for protection.
    /// </summary>
    public static bool HomeOnPurpose(bool noPhone, bool protOn, bool useWithOff) => noPhone || (!protOn && useWithOff);

    bool OnHome => HomeOnPurpose(_s.NoPhone, _protOn, _s.UseWithProtectionOff);

    bool PingedOnHome => PingOnHome(_s.NoPhone, _protOn);

    string MeasureFirst => PingVia == null && !PingedOnHome ? Loc.T("Measure the ping first. ") + NoLinkWhy(_last) + Loc.T(" With protection on, the ping is only measured through the phone.") : Loc.T("Measure the ping first: press Refresh.");

    /// <summary>The page's grey line while protection is on and nothing can be measured: connect the phone, or wait for its check.</summary>
    public static string NoPingSuffix(GuardReport rep) => rep?.Candidate != null
        ? Loc.T(" · the ping waits until the phone's connection is checked (protection is on)")
        : Loc.T(" · connect the phone to measure ping (protection is on)");

    Dictionary<string, int> MeasuredPings() => _regions.Where(r => r.PingMs >= 0).ToDictionary(r => r.Id, r => r.PingMs);

    // ------------------------------------------------------------ Turning regions on and off

    void RegionToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RegionItem r) return;
        var set = _regionGame.Settings.BlockedRegions.ToHashSet();
        if (r.Allowed) set.Remove(r.Id); else set.Add(r.Id);
        _ = SetBlocked(set, keepUnknown: true);
    }

    void RegionOnly_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RegionItem r || _regionInfos == null) return;
        _ = SetBlocked(Regions.AllExcept(_regionInfos, new[] { r.Id }), keep: new[] { r.Id });
    }

    void RegionsClosest_Click(object sender, RoutedEventArgs e)
    {
        if (NoRegionList()) return;
        var closest = Regions.Closest(MeasuredPings(), 3);
        if (closest.Count < 3) { Toast(ClosestText(_regions.Count(r => r.PingMs > -2), closest.Count, MeasureFirst), false); return; }
        _ = SetBlocked(Regions.AllExcept(_regionInfos, closest), keep: closest);
    }

    /// <summary>
    /// Closest 3 with fewer than 3 pings: measure first only when nothing was measured (review C15: with 2 answers it asked to measure,
    /// and Refresh gave the same). <paramref name="measured"/> counts the regions pinged, answered or not.
    /// </summary>
    public static string ClosestText(int measured, int answered, string measureFirst) =>
        measured == 0 ? measureFirst
        : answered == 0 ? Loc.T("No region answered the ping. Press Refresh to measure again.")
        : Loc.F("Only {0} answered the ping, so there aren't 3 to keep. Use Only this region, or Refresh to measure again.", Fmt.Count(answered, "region"));

    /// <summary>Your continent: from the lowest ping, else from your home country (null = the shortcut hides).</summary>
    string MyContinent => Regions.HomeContinent(_regions.Select(r => r.Info).ToList(), MeasuredPings(), _homeInfo?.Country ?? _egressInfo?.Country);

    void RegionsContinent_Click(object sender, RoutedEventArgs e)
    {
        if (NoRegionList()) return;
        if (RegionsContinentBtn.Tag is not string c) return;
        _ = SetBlocked(Regions.AllExcept(_regionInfos, _regionInfos.Where(r => r.Continent == c).Select(r => r.Id)), keep: new[] { Regions.ContinentKey(c) });
    }

    void RegionsAllowAll_Click(object sender, RoutedEventArgs e) => _ = SetBlocked(new HashSet<string>());

    void RegionsMaxPing_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RegionsMaxPingText == null || _regionGame == null) return;
        RegionsMaxPingText.Text = Loc.F("{0} ms", (int)e.NewValue);
        _regionGame.Settings.RegionsMaxPing = (int)e.NewValue;
    }

    void RegionsUnder_Click(object sender, RoutedEventArgs e)
    {
        if (NoRegionList()) return;
        SaveSettings();
        var max = (int)RegionsMaxPing.Value;
        var pings = MeasuredPings();
        if (pings.Count == 0) { Toast(ClosestText(_regions.Count(r => r.PingMs > -2), 0, MeasureFirst), false); return; }
        var keep = pings.Where(p => p.Value <= max).Select(p => p.Key).ToList();
        if (keep.Count == 0) { Toast(Loc.F("No region answered under {0} ms. Move the slider up.", max), false); return; }
        _ = SetBlocked(Regions.AllExcept(_regionInfos, keep), keep: keep);
    }

    bool NoRegionList()
    {
        if (_regionInfos != null) return false;
        Toast(Loc.T("No regions yet: press Refresh when you're online."), false);
        return true;
    }

    /// <summary>
    /// Saves the new set and puts the rule in place. <paramref name="keepUnknown"/>: a single toggle keeps cities that are
    /// blocked but not in today's list (Valve may bring them back); the shortcuts start over. <paramref name="keep"/>: what a
    /// shortcut keeps on, saved so cities Valve adds later stay off too; a single toggle or Allow all clears it.
    /// </summary>
    async Task SetBlocked(HashSet<string> blocked, bool keepUnknown = false, IEnumerable<string> keep = null, bool onStart = false, RegionGameState state = null)
    {
        state ??= _regionGame;
        if (state.Infos == null) return;
        var settings = state.Settings;
        var known = state.Infos.Select(r => r.Id).ToHashSet();
        if (!keepUnknown) blocked.IntersectWith(known);
        if (known.Count > 0 && known.All(blocked.Contains))
        {
            Toast(state == _cs2Regions ? Loc.T("Keep at least one region on, or CS2 can't find any match.")
                : Loc.F("Keep at least one region on, or {0} can't find any match.", state.Game.Name), false);
            foreach (var r in state.Rows) r.Allowed = !settings.BlockedRegions.Contains(r.Id);
            return;
        }
        settings.BlockedRegions = blocked.OrderBy(x => x, StringComparer.Ordinal).ToList();
        settings.RegionsKeep = keep?.OrderBy(x => x, StringComparer.Ordinal).ToList();
        SaveSettings();
        foreach (var r in state.Rows) r.Allowed = !blocked.Contains(r.Id);
        ShowRegionsStatus();
        if (_demo) return;
        await ApplyRegions(state, quiet: onStart);
        if (!onStart && state == _regionGame && state.Error == null && state.Game.IsRunning)
            Toast(state == _cs2Regions ? Cs2OpenRegionsText(blocked.Count)
                : Loc.F("{0} is open. Restart it between matches if it still uses the previous regions.", state.Game.Name), Tone.Info);
    }

    /// <summary>After a change with CS2 open (review C13: Allow all also said "the block is already on").</summary>
    public static string Cs2OpenRegionsText(int blockedCount) => blockedCount == 0
        ? Loc.T("CS2 is open: every region is allowed again, but CS2 only measures the regions when it starts. Restart it between matches.")
        : Loc.T("CS2 is open: the block is already on, but CS2 only measures the regions when it starts. Restart it between matches.");

    /// <summary>Puts the Firewall rule in line with the saved set (or removes it). One at a time, always the latest set.</summary>
    async Task ApplyRegions(RegionGameState state, bool quiet)
    {
        // Not tied to protection: Turn off protection and the no phone mode leave the rule; only Allow all (or --restore) lifts it.
        if (_demo || state.Infos == null) { ShowRegionsStatus(); return; }
        await _regionGate.WaitAsync();
        try
        {
            if (_restored) return;
            var game = state.Game;
            var settings = state.Settings;
            var infos = state.Infos;
            var blocked = settings.BlockedRegions.ToList();
            var rev = state.Config.Revision;
            var blockedIds = blocked.ToHashSet(StringComparer.Ordinal);
            var hasBlockedRelays = infos.Any(info => info.RelayIps.Count > 0 && blockedIds.Contains(info.Id));
            var exe = hasBlockedRelays ? RegionGameExe(state) : null;
            var missingKey = $"regions-missing-game|{game.AppId}";
            if (exe == null && hasBlockedRelays)
            {
                state.Error = state == _cs2Regions
                    ? Loc.T("CS2 wasn't found on this PC, so there's nothing to block yet. Open CS2 once with the app running.")
                    : Loc.F("{0} wasn't found in the Steam libraries. Install it in Steam and press Refresh.", game.Name);
                state.RuleOn = false;
                state.NoGame = true;
                // A click on a shortcut said nothing before (review C16); the Log says it once until CS2 is found
                Log.WarnOnce(missingKey, $"Regions: {game.Exe} wasn't found.");
                if (!quiet && state == _regionGame) Toast(state.Error, false);
                return;
            }
            state.NoGame = false;
            Log.Again(missingKey);
            var rule = Regions.Rule(infos, blocked, exe, game);
            var err = await Task.Run(() =>
            {
                try
                {
                    if (rule == null) Firewall.Remove(game.RuleName);
                    else Firewall.Apply(rule);
                    // Removing never throws (Firewall.Remove only logs), so read it back like a written rule.
                    if (rule == null) return Regions.RuleLeft(Firewall.Remaining(), unreadableCounts: true, game: game) ? Loc.T("the old rule is still there after removing it") : null;
                    return Firewall.Verify(rule) ? null : Loc.T("the rule didn't check out after writing it");
                }
                catch (Exception ex) { return (ex.InnerException ?? ex).Message; }
            });
            var was = state.Error;
            state.Error = err == null ? null : Loc.F("Couldn't change the Firewall: {0}", err);
            state.RuleOn = err == null && rule != null;
            if (err != null) state.RetryAt = DateTime.Now.AddMinutes(2);
            if (err == null)
            {
                settings.RegionsRevision = rev;
                SaveSettings();
                Log.Ok(rule == null ? $"Regions: {game.Name}, all allowed, rule removed."
                    : $"Regions: {game.Exe} blocked from {rule.RemoteIps.Count} Valve relays ({state.Rows.Count(r => !r.Allowed)} regions off).");
            }
            else if (!quiet || was == null)
            {
                Log.Error($"Regions: {game.Name}: " + Loc.En(state.Error));
                if (!quiet && state == _regionGame) Toast(state.Error, Tone.Error);
            }
        }
        finally
        {
            _regionGate.Release();
            ShowRegionsStatus();
        }
    }

    /// <summary>
    /// Every 30 s, with or without protection: puts the rule back when anything else removed it, tries a failed write or removal
    /// again every 2 min, and reads Valve's list again every hour (new relays to block).
    /// </summary>
    async Task KeepRegionRule(RegionGameState state)
    {
        if (_restored || state.Checking) return;
        var blocked = state.Settings.BlockedRegions;
        var count = state.Infos == null ? blocked.Count : state.Rows.Count(row => !row.Allowed);
        var retry = Regions.ApplyDue(count, state.RuleOn, state.Error, state.NoGame, DateTime.Now, state.RetryAt);
        var refresh = !state.Loading && !state.Pinging && ((blocked.Count > 0 && (DateTime.Now - state.TriedAt).TotalHours >= 1)
            || (state.PendingPreset != null && (DateTime.Now - state.TriedAt).TotalMinutes >= 2));
        // With nothing blocked the check only runs to try a failed Allow all again (the old rule is still there).
        if (!refresh && (state.Infos == null || (count == 0 && !retry))) return;
        state.Checking = true;
        try
        {
            if (refresh) await LoadRegions(atStart: false, quiet: true, state: state);
            else if (retry) await ApplyRegions(state, quiet: true);
            else if (state.RuleOn && _regionGate.CurrentCount > 0)   // ApplyRegions running: it reads back its own write
            {
                var rule = Regions.Rule(state.Infos, blocked, RegionGameExe(state), state.Game);
                var gone = rule != null && await Task.Run(() => !Firewall.Verify(rule));
                if (gone && state.RuleOn && _regionGate.CurrentCount > 0 && !_restored)
                {
                    Log.Info("Regions: the Firewall rule was removed or changed from outside, putting it back.");
                    state.RuleOn = false;
                    await ApplyRegions(state, quiet: true);
                }
                else ShowRegionsStatus();   // the guard's Firewall state may have changed
            }
        }
        finally { state.Checking = false; }
    }

    // ------------------------------------------------------------ Status

    /// <summary>The Regions block in Checks: it outlives protection and closing the app, so Connection says it is on.</summary>
    void UpdateRegionsCheck()
    {
        if (_cRegions == null) return;
        var total = _cs2Regions?.Rows.Count ?? 0;
        var off = total > 0 ? _cs2Regions.Rows.Count(r => !r.Allowed) : _s.BlockedRegions.Count;
        _cRegions.Detail = RegionsCheckText(total, off, _cs2Regions?.Error != null, _cs2Regions?.NoGame == true);
        _cRegions.Ok = null;
        _cRegions.Waiting = off > 0;   // grey when every region is allowed, amber while some are blocked
    }

    /// <summary>"8 of 30 allowed", "All 30 allowed"; before Valve's list is read, the count turned off.</summary>
    /// <remarks>Review C16: with cs2.exe not found there is no rule at all, and after a failed write the rule didn't change, so
    /// "allowed" would read as a block that isn't there.</remarks>
    public static string RegionsCheckText(int total, int off, bool failed = false, bool noCs2 = false) =>
        off == 0 ? (total > 0 ? Loc.F("All {0} allowed", total) : Loc.T("All allowed"))
        : noCs2 ? Loc.T("Not blocked yet: see Regions")
        : failed ? Loc.T("Firewall rule didn't change: see Regions")
        : total > 0 ? Loc.F("{0} of {1} allowed (Regions page)", total - off, total)
        : Loc.F("{0} turned off (Regions page)", off);

    /// <summary>The page's title line: what CS2 can play in, or what you picked while the rule isn't there.</summary>
    public static string RegionsTitle(int on, int total, bool failed, bool noCs2) =>
        on >= total ? Loc.F("All {0} regions allowed: CS2 picks as usual", total)
        : noCs2 ? Loc.F("{0} of {1} regions picked, not blocked yet", on, total)
        : failed ? Loc.F("{0} of {1} regions picked: the Firewall rule didn't change", on, total)
        : Loc.F("CS2 can play in {0} of {1} regions", on, total);

    /// <summary>The pings on the page were measured on the other path (through the phone, or without it) than the game uses now.</summary>
    public static bool PingsStale(bool anyPing, bool pingedHome, bool nowHome) => anyPing && pingedHome != nowHome;

    /// <summary>
    /// Protection or No phone changed since the ping (review C14: the line said "through the phone" while the game went home, and
    /// Closest 3 picked from those numbers): the old pings go, and with the page open they are measured again on the new path.
    /// </summary>
    void RemeasureIfPathChanged()
    {
        var state = _regionGame;
        if (_demo || state.Pinging || !PingsStale(_regions.Any(r => r.PingMs >= 0), state.PingedHome, PingedOnHome)) return;
        foreach (var r in _regions) r.PingMs = -2;
        state.PingedAt = DateTime.MinValue;
        state.View?.Refresh();
        if (PageRegions.IsVisible && PingVia?.LocalIp != null) _ = PingRegions(quiet: true);
    }

    void ShowRegionsStatus()
    {
        UpdateRegionsCheck();
        var state = _regionGame;
        if (RegionsStatus == null || state == null) return;
        RemeasureIfPathChanged();
        // Nothing to act on until Valve's list is here (the buttons would only answer "No regions yet"). The demo fills the page itself.
        RegionsClosestBtn.IsEnabled = RegionsContinentBtn.IsEnabled = RegionsAllowAllBtn.IsEnabled = RegionsUnderBtn.IsEnabled = _regionInfos != null || (_demo && _sdr != null);
        UpdateRegionPresetButtons();
        // "<continent> only" for where you play from; hidden until the app knows it (a ping, or your home country).
        var mine = MyContinent;
        RegionsContinentBtn.Tag = mine;
        RegionsContinentBtn.Content = Loc.F("{0} only", mine == null ? null : Loc.T(mine));
        RegionsContinentBtn.ToolTip = Loc.F("Keeps every region in {0} and turns the rest off", mine == null ? null : Loc.T(mine));
        RegionsContinentBtn.Visibility = mine == null ? Visibility.Collapsed : Visibility.Visible;
        var total = _regions.Count;
        var off = _regions.Count(r => !r.Allowed);
        var on = total - off;
        RegionHeader.Visibility = total == 0 ? Visibility.Hidden : Visibility.Visible;
        RegionsRefreshBtn.IsEnabled = !state.Pinging;
        RegionsRefreshBtn.Content = Loc.T(state.Pinging ? "Measuring…" : "Refresh");
        // No list yet: one line on top, and the reason in the empty list (no blank lines under the status).
        RegionsEmpty.Visibility = _sdr == null || total == 0 ? Visibility.Visible : Visibility.Collapsed;
        RegionsRule.Visibility = RegionsSource.Visibility = _sdr == null ? Visibility.Collapsed : Visibility.Visible;
        RegionsEmptyTitle.Text = Loc.T("No regions yet");
        if (_sdr == null)
        {
            var loading = state.Loading || !_regionRulesReady && !_demo;
            RegionsStatus.Text = RegionsEmptyTitle.Text = loading ? Loc.T("Loading regions…") : Loc.T("Couldn't load the regions");
            RegionsStatus.Foreground = Ui.Faint;
            RegionsEmptyText.Text = loading ? Loc.T("Getting the region list from Valve. A few seconds.")
                : Loc.T("The app couldn't reach Valve and has no saved copy. Check the internet connection and press Refresh.");
            RegionsMaxPingHint.Visibility = Visibility.Collapsed;
            return;
        }
        RegionsStatus.Text = state == _cs2Regions || (off > 0 && state.Error != null)
            ? RegionsTitle(on, total, state.Error != null, state.NoGame)
            : off == 0 ? Loc.F("All {0} regions allowed for {1}", total, state.Game.Name)
            : Loc.F("{0} of {1} regions allowed for {2}", on, total, state.Game.Name);
        RegionsStatus.Foreground = off > 0 && on < 3 ? Ui.Warn : Ui.Text;

        // The rule is written and read back even while Windows Firewall is off, but then nothing enforces it.
        var fwOff = off > 0 ? _last?.FirewallProblem?.TrimEnd('.') : null;
        RegionsRule.Foreground = state.Error != null || fwOff != null ? Ui.Bad : Ui.Muted;
        RegionsRule.Text = state.Error
            ?? (off == 0 ? Loc.T("No Firewall rule. Turn regions off below or use a shortcut.")
            : fwOff != null ? Loc.F("{0}. The rule is in place, but Windows doesn't enforce it until the Firewall is back on.", fwOff)
            : state.RuleOn ? (state == _cs2Regions ? Loc.T("Block is on. Only CS2 is affected.") : Loc.F("Block is on. Only {0} is affected.", state.Game.Name))
                + (on < 3 ? Loc.T(" Few regions left: expect longer queues.") : "")
            : Loc.T("Putting the Firewall rule in place…"));

        var src = Loc.F("Region list updated {0}", Fmt.Ago(_sdr.FetchedAt));
        if (_sdr.FromCache) src = Loc.F("Offline: using the list saved on {0}", Fmt.When(_sdr.FetchedAt));
        var pinged = _regions.Any(r => r.PingMs >= 0);
        src += state.Pinging ? Loc.T(" · measuring ping…")
            : pinged ? Loc.F(" · ping measured {0}", Fmt.Ago(state.PingedAt)) + (state.HomeIsPhone ? Loc.T(" on the phone's connection (the PC has no other network)") : state.PingedHome ? Loc.T(" without the phone") : Loc.T(" through the phone"))
            : PingVia == null && !PingedOnHome ? NoPingSuffix(_last)
            : "";
        RegionsSource.Text = src;
        // The path of the pings shown (the same as the line), else the one the next measure takes
        var pingPath = (pinged ? state.PingedHome : PingedOnHome) ? Loc.T("without the phone (protection off or No phone)") : Loc.T("through the phone");
        RegionsSource.ToolTip = state == _cs2Regions
            ? Loc.F("Valve list revision {0}. Ping is measured {1}, the same path the game uses now.", _sdr.Revision, pingPath)
            : Loc.F("Valve list revision {0}. Ping is measured {1}. The Regions page does not route {2} through the phone.", _sdr.Revision, pingPath, state.Game.Name);

        // CS2's own limit: a ping cap below the slowest region you allow means that region never comes up.
        var slowest = _regions.Where(r => r.Allowed && r.PingMs >= 0).OrderByDescending(r => r.PingMs).FirstOrDefault();
        var worst = slowest?.PingMs ?? -1;
        RegionsMaxPingHint.Visibility = state == _cs2Regions && off > 0 && worst > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (worst > 0 && state == _cs2Regions)
            RegionsMaxPingHint.Text = Loc.F("Your slowest allowed region is {0} at {1} ms. In CS2, open Settings > Game and set Max Acceptable Matchmaking Ping to {1} or more, or CS2 won't use {0}.", slowest.City, worst);
    }
}
