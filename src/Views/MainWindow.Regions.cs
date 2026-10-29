using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using SRT.Core;

namespace SRT;

// Regions: which CS2 matchmaking regions cs2.exe can reach (Firewall rule on Valve's relays, see Core/Network/Regions.cs)
public partial class MainWindow
{
    readonly ObservableCollection<RegionItem> _regions = new();
    ICollectionView _regionView;
    SdrConfig _sdr;
    List<RegionInfo> _regionInfos;
    readonly SemaphoreSlim _regionGate = new(1);
    bool _regionsReady, _regionsLoading, _pingingRegions, _regionRuleOn, _regionNoCs2, _regionChecking;
    string _regionError;
    DateTime _regionsPingedAt = DateTime.MinValue;
    DateTime _regionRetryAt, _regionsTriedAt;   // a failed write is tried again after 2 min; Valve's list is read again every hour

    /// <summary>Once, when the window loads (the page is in the tree even while hidden): reads the list and puts the rule back.</summary>
    async void PageRegions_Loaded(object sender, RoutedEventArgs e)
    {
        if (_regionsReady) return;
        _regionsReady = true;
        _regionView = CollectionViewSource.GetDefaultView(_regions);
        _regionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegionItem.Continent)));
        _regionView.SortDescriptions.Add(new SortDescription(nameof(RegionItem.ContinentRank), ListSortDirection.Ascending));
        _regionView.SortDescriptions.Add(new SortDescription(nameof(RegionItem.SortPing), ListSortDirection.Ascending));
        _regionView.SortDescriptions.Add(new SortDescription(nameof(RegionItem.City), ListSortDirection.Ascending));
        RegionList.ItemsSource = _regionView;
        RegionsMaxPing.Value = Math.Clamp(_s.RegionsMaxPing, 20, 200);
        RegionsMaxPingText.Text = $"{(int)RegionsMaxPing.Value} ms";
        if (_demo) return;   // the demo fills the page itself
        // After Restore network the rule is gone; it comes back with protection (Run leak test or the next start).
        _queryTimer.Tick += (_, _) => KeepRegionRule();
        // Nothing blocked here, but the rule is machine-wide: another copy of the app (or a reset settings.json) may have left one.
        if (_s.BlockedRegions.Count == 0 && !_restored && await Task.Run(() => Regions.RuleLeft(Firewall.Remaining())))
        {
            await Task.Run(() => Firewall.Remove(Firewall.RegionsRuleName));
            Log.Warn("Regions: found a leftover rule from another copy of the app and removed it.");
        }
        await LoadRegions(atStart: true);
    }

    async void PageRegions_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!PageRegions.IsVisible || _demo) return;
        if (_sdr == null || (DateTime.Now - _sdr.FetchedAt).TotalHours >= 1) await LoadRegions(atStart: false);
        if ((DateTime.Now - _regionsPingedAt).TotalMinutes >= 2) await PingRegions(quiet: true);
    }

    async void RegionsRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadRegions(atStart: false);
        await PingRegions(quiet: false);
    }

    /// <summary>Valve's list (or the saved copy), the rows, and the rule rebuilt when the list's revision changed.</summary>
    async Task LoadRegions(bool atStart, bool quiet = false)
    {
        if (_regionsLoading) return;
        _regionsLoading = true;
        _regionsTriedAt = DateTime.Now;
        if (_sdr == null) ShowRegionsStatus();   // the empty list says it's reading
        try
        {
            var cfg = await Regions.Load();
            if (cfg == null)
            {
                if (_sdr == null)
                {
                    _regionsLoading = false;
                    RegionHeader.Visibility = Visibility.Hidden;
                    ShowRegionsStatus();
                    if (!atStart && !quiet) Toast("Couldn't reach Valve. Check the internet connection and press Refresh again.", false);
                }
                else if (!atStart && !quiet) Toast("Couldn't reach Valve: still using the list from " + Fmt.When(_sdr.FetchedAt) + ".", false);
                return;
            }
            var changed = _sdr != null && _sdr.Revision != cfg.Revision;
            _sdr = cfg;
            // Same list as before (the hourly read, mostly): the rows stay as they are, so the list doesn't jump to the top.
            var infos = Regions.Group(cfg);
            if (!Regions.SameRows(_regionInfos, infos)) ShowRegionRows(infos);
            var kept = KeepShortcut(atStart, quiet);
            // Valve adds and moves relays: a new revision means new IPs to block for the same regions.
            if (_s.BlockedRegions.Count > 0 && (atStart || changed || kept || cfg.Revision != _s.RegionsRevision))
            {
                if (cfg.Revision != _s.RegionsRevision && _s.RegionsRevision != 0) Log.Info($"Regions: Valve's relay list changed (revision {cfg.Revision}), rebuilding the rule.");
                await ApplyRegions(quiet: true);
            }
            else ShowRegionsStatus();
        }
        finally { _regionsLoading = false; }
    }

    /// <summary>After a shortcut (Only this region, Closest 3, your continent only, Ping under): cities Valve added since then stay off too. True when the set grew.</summary>
    bool KeepShortcut(bool atStart, bool quiet)
    {
        if (_s.RegionsKeep is not { Count: > 0 } keep) return false;
        var now = Regions.KeepOnly(_regionInfos, keep);
        if (now == null)
        {
            if (!quiet) Log.Warn("Regions: none of the regions your shortcut kept on is in Valve's list any more. The saved set stays.");
            if (!atStart && !quiet) Toast("The regions your shortcut kept on aren't in Valve's list any more. Pick again.", false);
            return false;
        }
        var added = now.Except(_s.BlockedRegions).ToList();
        if (added.Count == 0) return false;
        _s.BlockedRegions = _s.BlockedRegions.Union(added).OrderBy(x => x, StringComparer.Ordinal).ToList();
        SaveSettings();
        foreach (var r in _regions) r.Allowed = !_s.BlockedRegions.Contains(r.Id);
        Log.Info($"Regions: Valve added {Fmt.Count(added.Count, "region")}, kept off by your shortcut: {string.Join(", ", added)}.");
        return true;
    }

    /// <summary>Fills the list, keeping the pings already measured.</summary>
    void ShowRegionRows(List<RegionInfo> infos)
    {
        _regionInfos = infos;
        var old = _regions.ToDictionary(r => r.Id, r => r.PingMs);
        var blocked = _s.BlockedRegions.ToHashSet();
        _regions.Clear();
        foreach (var info in infos)
            _regions.Add(new RegionItem { Info = info, Allowed = !blocked.Contains(info.Id), PingMs = old.GetValueOrDefault(info.Id, -2) });
        RegionHeader.Visibility = _regions.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        ShowRegionsStatus();
    }

    // ------------------------------------------------------------ Ping

    async Task PingRegions(bool quiet)
    {
        if (_demo || _pingingRegions || _regionInfos == null) return;
        var local = PingVia?.LocalIp;
        if (local == null)
        {
            // Never through the home network while the game goes through the phone: the number has to be what the game feels.
            if (!quiet) Toast(_s.NoPhone ? "No connection to measure the ping through." : "Phone disconnected: the ping is only measured through the phone.", false);
            ShowRegionsStatus();
            return;
        }
        _pingingRegions = true;
        RegionsRefreshBtn.IsEnabled = false;
        RegionsRefreshBtn.Content = "Measuring…";
        try
        {
            var pings = await Regions.PingAll(_regionInfos, local);
            foreach (var r in _regions) r.PingMs = pings.GetValueOrDefault(r.Id, -1);
            _regionsPingedAt = DateTime.Now;
            _regionView?.Refresh();
            if (!quiet) Toast($"{pings.Count(p => p.Value >= 0)} of {pings.Count} regions answered.", pings.Any(p => p.Value >= 0));
        }
        finally
        {
            _pingingRegions = false;
            RegionsRefreshBtn.IsEnabled = true;
            RegionsRefreshBtn.Content = "Refresh";
            ShowRegionsStatus();
        }
    }

    /// <summary>The path CS2 uses: the phone, or the home connection in the no phone mode.</summary>
    Adapter PingVia => _s.NoPhone ? HomeAdapter : _last?.Egress;

    string MeasureFirst => _s.NoPhone ? "Measure the ping first: press Refresh." : "Measure the ping first: connect the phone and press Refresh.";

    Dictionary<string, int> MeasuredPings() => _regions.Where(r => r.PingMs >= 0).ToDictionary(r => r.Id, r => r.PingMs);

    // ------------------------------------------------------------ Turning regions on and off

    void RegionToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RegionItem r) return;
        var set = _s.BlockedRegions.ToHashSet();
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
        if (closest.Count < 3) { Toast(MeasureFirst, false); return; }
        _ = SetBlocked(Regions.AllExcept(_regionInfos, closest), keep: closest);
    }

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
        if (RegionsMaxPingText == null || _s == null) return;
        RegionsMaxPingText.Text = $"{(int)e.NewValue} ms";
        _s.RegionsMaxPing = (int)e.NewValue;
    }

    void RegionsUnder_Click(object sender, RoutedEventArgs e)
    {
        if (NoRegionList()) return;
        SaveSettings();
        var max = (int)RegionsMaxPing.Value;
        var pings = MeasuredPings();
        if (pings.Count == 0) { Toast(MeasureFirst, false); return; }
        var keep = pings.Where(p => p.Value <= max).Select(p => p.Key).ToList();
        if (keep.Count == 0) { Toast($"No region answered under {max} ms. Move the slider up.", false); return; }
        _ = SetBlocked(Regions.AllExcept(_regionInfos, keep), keep: keep);
    }

    bool NoRegionList()
    {
        if (_regionInfos != null) return false;
        Toast("No regions yet: press Refresh when you're online.", false);
        return true;
    }

    /// <summary>
    /// Saves the new set and puts the rule in place. <paramref name="keepUnknown"/>: a single toggle keeps cities that are
    /// blocked but not in today's list (Valve may bring them back); the shortcuts start over. <paramref name="keep"/>: what a
    /// shortcut keeps on, saved so cities Valve adds later stay off too; a single toggle or Allow all clears it.
    /// </summary>
    async Task SetBlocked(HashSet<string> blocked, bool keepUnknown = false, IEnumerable<string> keep = null)
    {
        if (NoRegionList()) return;
        var known = _regionInfos.Select(r => r.Id).ToHashSet();
        if (!keepUnknown) blocked.IntersectWith(known);
        if (known.Count > 0 && known.All(blocked.Contains))
        {
            Toast("Keep at least one region on, or CS2 can't find any match.", false);
            foreach (var r in _regions) r.Allowed = !_s.BlockedRegions.Contains(r.Id);
            return;
        }
        _s.BlockedRegions = blocked.OrderBy(x => x, StringComparer.Ordinal).ToList();
        _s.RegionsKeep = keep?.OrderBy(x => x, StringComparer.Ordinal).ToList();
        SaveSettings();
        foreach (var r in _regions) r.Allowed = !blocked.Contains(r.Id);
        ShowRegionsStatus();
        if (_demo) return;
        await ApplyRegions(quiet: false);
        if (_regionError == null && Steam.Cs2Running) Toast("CS2 is open: the block is already on, but CS2 only measures the regions when it starts. Restart it between matches.", Tone.Info);
    }

    /// <summary>Puts the Firewall rule in line with the saved set (or removes it). One at a time, always the latest set.</summary>
    async Task ApplyRegions(bool quiet)
    {
        // Not tied to protection: Turn off protection and the no phone mode leave the rule; only Allow all (or --restore) lifts it.
        if (_demo || _regionInfos == null) { ShowRegionsStatus(); return; }
        await _regionGate.WaitAsync();
        try
        {
            var infos = _regionInfos;
            var blocked = _s.BlockedRegions.ToList();
            var rev = _sdr.Revision;
            var cs2 = blocked.Count > 0 ? Steam.Cs2Path(_s) : null;
            if (blocked.Count > 0 && cs2 == null)
            {
                _regionError = "CS2 wasn't found on this PC, so there's nothing to block yet. Open CS2 once with the app running.";
                _regionRuleOn = false;
                _regionNoCs2 = true;
                return;
            }
            _regionNoCs2 = false;
            var err = await Task.Run(() =>
            {
                try
                {
                    Regions.Apply(infos, blocked, cs2);
                    var rule = Regions.Rule(infos, blocked, cs2);
                    // Removing never throws (Firewall.Remove only logs), so read it back like a written rule.
                    if (rule == null) return Regions.RuleLeft(Firewall.Remaining(), unreadableCounts: true) ? "the old rule is still there after removing it" : null;
                    return Firewall.Verify(rule) ? null : "the rule didn't check out after writing it";
                }
                catch (Exception ex) { return (ex.InnerException ?? ex).Message; }
            });
            var was = _regionError;
            _regionError = err == null ? null : "Couldn't change the Firewall: " + err;
            _regionRuleOn = err == null && blocked.Count > 0;
            if (err != null) _regionRetryAt = DateTime.Now.AddMinutes(2);
            if (err == null)
            {
                _s.RegionsRevision = rev;
                SaveSettings();
                var n = Regions.BlockedIps(infos, blocked).Count;
                Log.Ok(blocked.Count == 0 ? "Regions: all allowed, rule removed." : $"Regions: cs2.exe blocked from {n} Valve relays ({_regions.Count(r => !r.Allowed)} regions off).");
            }
            else if (!quiet || was == null)
            {
                Log.Error("Regions: " + _regionError);
                if (!quiet) Toast(_regionError, Tone.Error);
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
    async void KeepRegionRule()
    {
        var count = _s.BlockedRegions.Count;
        // With nothing blocked the check only runs to try a failed Allow all again (the old rule is still there).
        if (_regionChecking || _regionInfos == null || (count == 0 && !Regions.ApplyDue(0, _regionRuleOn, _regionError, _regionNoCs2, DateTime.Now, _regionRetryAt))) { ShowRegionsStatus(); return; }
        _regionChecking = true;
        try
        {
            if (count > 0 && !_regionsLoading && !_pingingRegions && (DateTime.Now - _regionsTriedAt).TotalHours >= 1) await LoadRegions(atStart: false, quiet: true);
            else if (Regions.ApplyDue(count, _regionRuleOn, _regionError, _regionNoCs2, DateTime.Now, _regionRetryAt)) await ApplyRegions(quiet: true);
            else if (_regionRuleOn && _regionGate.CurrentCount > 0)   // ApplyRegions running: it reads back its own write
            {
                var infos = _regionInfos; var blocked = _s.BlockedRegions.ToList(); var cs2 = Steam.Cs2Path(_s);
                var gone = await Task.Run(() => { var rule = Regions.Rule(infos, blocked, cs2); return rule != null && !Firewall.Verify(rule); });
                if (gone && _regionRuleOn && _regionGate.CurrentCount > 0)
                {
                    Log.Info("Regions: the Firewall rule was removed or changed from outside, putting it back.");
                    _regionRuleOn = false;
                    await ApplyRegions(quiet: true);
                }
                else ShowRegionsStatus();   // the guard's Firewall state may have changed
            }
        }
        finally { _regionChecking = false; }
    }

    // ------------------------------------------------------------ Status

    void ShowRegionsStatus()
    {
        if (RegionsStatus == null) return;
        // Nothing to act on until Valve's list is here (the buttons would only answer "No regions yet"). The demo fills the page itself.
        RegionsClosestBtn.IsEnabled = RegionsContinentBtn.IsEnabled = RegionsAllowAllBtn.IsEnabled = RegionsUnderBtn.IsEnabled = _regionInfos != null || (_demo && _sdr != null);
        // "<continent> only" for where you play from; hidden until the app knows it (a ping, or your home country).
        var mine = MyContinent;
        RegionsContinentBtn.Tag = mine;
        RegionsContinentBtn.Content = mine + " only";
        RegionsContinentBtn.ToolTip = $"Keeps every region in {mine} and turns the rest off";
        RegionsContinentBtn.Visibility = mine == null ? Visibility.Collapsed : Visibility.Visible;
        var total = _regions.Count;
        var off = _regions.Count(r => !r.Allowed);
        var on = total - off;
        // No list yet: one line on top, and the reason in the empty list (no blank lines under the status).
        RegionsEmpty.Visibility = _sdr == null || total == 0 ? Visibility.Visible : Visibility.Collapsed;
        RegionsRule.Visibility = RegionsSource.Visibility = _sdr == null ? Visibility.Collapsed : Visibility.Visible;
        RegionsEmptyTitle.Text = "No regions yet";
        if (_sdr == null)
        {
            RegionsStatus.Text = RegionsEmptyTitle.Text = _regionsLoading ? "Loading regions…" : "Couldn't load the regions";
            RegionsStatus.Foreground = Ui.Faint;
            RegionsEmptyText.Text = _regionsLoading ? "Getting the region list from Valve. A few seconds."
                : "The app couldn't reach Valve and has no saved copy. Check the internet connection and press Refresh.";
            RegionsMaxPingHint.Visibility = Visibility.Collapsed;
            return;
        }
        RegionsStatus.Text = off == 0 ? $"All {total} regions allowed: CS2 picks as usual" : $"CS2 can play in {on} of {total} regions";
        RegionsStatus.Foreground = off > 0 && on < 3 ? Ui.Warn : Ui.Text;

        // The rule is written and read back even while Windows Firewall is off, but then nothing enforces it.
        var fwOff = off > 0 ? _last?.FirewallProblem?.TrimEnd('.') : null;
        RegionsRule.Foreground = _regionError != null || fwOff != null ? Ui.Bad : Ui.Muted;
        RegionsRule.Text = _regionError
            ?? (off == 0 ? "No Firewall rule. Turn regions off below or use a shortcut."
            : fwOff != null ? fwOff + ". The rule is in place, but Windows doesn't enforce it until the Firewall is back on."
            : _regionRuleOn ? "Block is on. Only CS2 is affected." + (on < 3 ? " Few regions left: expect longer queues." : "")
            : "Putting the Firewall rule in place…");

        var src = $"Region list updated {Fmt.Ago(_sdr.FetchedAt)}";
        if (_sdr.FromCache) src = $"Offline: using the list saved on {Fmt.When(_sdr.FetchedAt)}";
        var pinged = _regions.Any(r => r.PingMs >= 0);
        src += _pingingRegions ? " · measuring ping…"
            : pinged ? $" · ping measured {Fmt.Ago(_regionsPingedAt)}" + (PingVia == null ? (_s.NoPhone ? "" : " · connect the phone to measure again") : "")
            : PingVia == null ? (_s.NoPhone ? "" : " · connect the phone to measure ping")
            : "";
        RegionsSource.Text = src;
        RegionsSource.ToolTip = $"Valve list revision {_sdr.Revision}. Ping is measured " + (_s.NoPhone ? "on your home connection" : "through the phone") + ", the same path the game uses.";

        // CS2's own limit: a ping cap below the slowest region you allow means that region never comes up.
        var slowest = _regions.Where(r => r.Allowed && r.PingMs >= 0).OrderByDescending(r => r.PingMs).FirstOrDefault();
        var worst = slowest?.PingMs ?? -1;
        RegionsMaxPingHint.Visibility = off > 0 && worst > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (worst > 0)
            RegionsMaxPingHint.Text = $"Your slowest allowed region is {slowest.City} at {worst} ms. In CS2, open Settings > Game and set Max Acceptable Matchmaking Ping to {worst} or more, or CS2 won't use {slowest.City}.";
    }
}
