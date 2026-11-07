using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SRT.Core;

namespace SRT;

// Configs: CS2's config copied between your accounts (sync modes, copy, snapshots, compare, backups). The files: Core/Steam/Cs2Configs.cs
public partial class MainWindow
{
    Cs2ConfigStore _cfg;
    /// <summary>Demo (development build): made-up userdata and store folders instead of the real ones.</summary>
    string _cfgDemoUserData = null, _cfgDemoStore = null;
    readonly ObservableCollection<CfgAccountRow> _cfgRows = new();
    /// <summary>The Copy "To" list: every account except the one picked under From.</summary>
    System.ComponentModel.ICollectionView _cfgToView;
    bool _cfgLoading;

    Cs2ConfigStore Cfg
    {
        get
        {
            if (_cfg != null) return _cfg;
            var userData = _demo ? _cfgDemoUserData : Steam.InstallDir() is string d ? Path.Combine(d, "userdata") : null;
            var root = _demo ? _cfgDemoStore ?? Path.Combine(Path.GetTempPath(), "CS2IPChanger-demo", "configs") : Paths.File("configs");
            _cfg = new Cs2ConfigStore(userData, root);
            if (_demo) _cfg.Cs2Running = () => false;
            return _cfg;
        }
    }

    // ------------------------------------------------------------ Triggers (one-line calls from Accounts, Servers and SteamWatch)

    /// <summary>After the account list loads: a new account gets the config once. The first time, every account is just remembered.</summary>
    void ConfigsAccountsLoaded()
    {
        if (_demo) return;
        try
        {
            FlushCfgQueue();   // copies left from an earlier run (nothing happens while CS2 is open)
            var st = Cfg.State;
            var known = st.Known;
            var ids = NewAccounts(ref known, _steamIds);
            if (st.Known == null) { st.Known = known; Cfg.SaveState(); return; }
            if (ids.Count == 0) return;
            Cfg.SaveState();
            var fresh = _accounts.Where(a => ids.Contains(a.SteamId)).ToList();   // a hidden account is remembered, never copied to
            if (!_s.ConfigOnNewAccount || _s.ConfigMode == CfgMode.Off) return;
            var jobs = fresh.SelectMany(a => CfgPlan(CfgTrigger.New, a.SteamId, PreviousAccount(a.SteamId))).ToList();
            RunCfgJobs(jobs, CfgTrigger.New, false);
        }
        catch (Exception ex) { Log.Warn("Config sync for a new account: " + Cs2ConfigStore.Why(ex)); }
    }

    /// <summary>Before the app logs in to an account (Log in, Switch): CS2 is closed by then, so the copy goes in now.</summary>
    void ConfigsBeforeLogin(AccountItem a)
    {
        if (_demo) return;
        try
        {
            FlushCfgQueue();
            if (!_s.ConfigOnLogin || _s.ConfigMode == CfgMode.Off) return;
            // The account in use, else the last one logged in, this one included: logging in to that same account again copies nothing.
            var prev = PreviousAccount(null);
            RunCfgJobs(CfgPlan(CfgTrigger.Login, a.SteamId, prev), CfgTrigger.Login, false);
        }
        catch (Exception ex) { Log.Warn("Config sync before login: " + Cs2ConfigStore.Why(ex)); }
    }

    /// <summary>Right before the app opens CS2 on the account already logged in (Join with CS2 closed, Steam restart).</summary>
    void ConfigsBeforeCs2()
    {
        if (_demo) return;
        try
        {
            FlushCfgQueue();
            if (!_s.ConfigOnLogin || _s.ConfigMode is CfgMode.Off or CfgMode.Previous || Steam.InUseSteamId() is not string active) return;
            RunCfgJobs(CfgPlan(CfgTrigger.Login, active, null), CfgTrigger.Cs2, false);
        }
        catch (Exception ex) { Log.Warn("Config sync before CS2: " + Cs2ConfigStore.Why(ex)); }
    }

    /// <summary>CS2 closed: the queued copies go in, and with Latest change wins the account changed last passes its config on.</summary>
    void ConfigsCs2Closed()
    {
        if (_demo) return;
        try
        {
            FlushCfgQueue();
            // Closed by the app (the account switch already copied): CS2 killed that way saved nothing new.
            if (_s.ConfigMode != CfgMode.Latest || _cs2.ClosedByAppRecently) return;
            RunCfgJobs(CfgPlan(CfgTrigger.Closed, null, null), CfgTrigger.Closed, false);
        }
        catch (Exception ex) { Log.Warn("Config sync after CS2 closed: " + Cs2ConfigStore.Why(ex)); }
    }

    void FlushCfgQueue()
    {
        if (Cfg.State.Pending.Count == 0) return;
        var results = Cfg.FlushPending(NewSyncId(), j => CfgSourceLabel(j));
        if (results.Count > 0) ReportCfg(results, CfgTrigger.Queued, false);
    }

    /// <summary>The copies for a trigger, from the settings (Sync on/off per account, mode, categories).</summary>
    List<CfgJob> CfgPlan(string trigger, string target, string previous)
    {
        var ids = _accounts.Select(a => a.SteamId).ToList();
        var opt = _s.ConfigOptOut.ToHashSet();
        // An account with Sync off is never the source: the latest change among the others wins.
        var latest = _s.ConfigMode == CfgMode.Latest ? Cfg.PickLatest(ids.Where(i => !opt.Contains(i))) : null;
        return Cs2Config.Plan(_s.ConfigMode, _s.ConfigMainId, previous, latest, trigger, target, ids, opt, _s.ConfigCategories);
    }

    /// <summary>The account used before this one: the one in use, else the last one logged in.</summary>
    string PreviousAccount(string except) =>
        _accounts.Where(a => a.SteamId != except)
            .OrderByDescending(a => a.Active)
            .ThenByDescending(a => a.Meta.LastUsed ?? DateTimeOffset.FromUnixTimeSeconds(a.Local.Timestamp).LocalDateTime)
            .FirstOrDefault()?.SteamId;

    static string NewSyncId() => DateTime.Now.ToString("yyyyMMddHHmmssfff");

    string CfgName(string steamId) => _accounts.FirstOrDefault(a => a.SteamId == steamId)?.Name ?? "an account";

    string CfgSourceLabel(CfgJob j) => j.Snapshot != null ? $"snapshot {j.Snapshot}" : CfgName(j.SourceId);

    internal static string CfgReasonText(string r) => r switch
    {
        CfgTrigger.New => "new account", CfgTrigger.Login => "before login", CfgTrigger.Cs2 => "before CS2 opened", CfgTrigger.Closed => "CS2 closed",
        CfgTrigger.Manual => "Sync now", CfgTrigger.Copy => "copy", CfgTrigger.Restore => "before a restore", CfgTrigger.Queued => "after CS2 closed", _ => r,
    };

    void RunCfgJobs(List<CfgJob> jobs, string why, bool manual)
    {
        if (jobs.Count == 0) return;
        var syncId = NewSyncId();
        var results = new List<CfgResult>();
        foreach (var j in jobs)
        {
            j.Reason = why;
            try { results.Add(Cfg.Push(j, syncId, CfgSourceLabel(j))); }
            catch (Exception ex) { results.Add(new CfgResult(CfgOutcome.Failed, j.Target, j.Categories, Cs2ConfigStore.Why(ex))); }
        }
        ReportCfg(results, why, manual, jobs);
    }

    /// <summary>
    /// The copies that wait for CS2 to close, in one Log line: Sync now with CS2 open wrote one line per account, 31 the same
    /// (diagnostics of 7 nov., 01:08:22). Null when none waits.
    /// </summary>
    public static string QueuedLine(string reason, List<string> names) => names.Count switch
    {
        0 => null,
        1 => $"Config ({reason}): CS2 is open, so the copy to {names[0]} waits until it closes.",
        _ => $"Config ({reason}): CS2 is open, so the copies to {names.Count} accounts wait until it closes.",
    };

    /// <summary>One Log line per account changed (never the file contents), and the line under Sync now.</summary>
    void ReportCfg(List<CfgResult> results, string why, bool manual, List<CfgJob> jobs = null)
    {
        string From(CfgResult r) => jobs?.FirstOrDefault(j => j.Target == r.Target) is CfgJob j ? " from " + CfgSourceLabel(j) : "";
        var reason = CfgReasonText(why);
        foreach (var r in results)
        {
            var who = CfgName(r.Target);
            switch (r.Outcome)
            {
                case CfgOutcome.Copied: Log.Ok($"Config ({reason}): copied {Cs2Config.CategoryList(r.Categories)}{From(r)} to {who}. Backup saved."); break;
                case CfgOutcome.Queued: break;   // one line for all of them, below
                case CfgOutcome.Same: if (manual) Log.Info($"Config ({reason}): {who} already has the same config."); break;
                case CfgOutcome.Skipped: Log.Info($"Config ({reason}): {who} not changed: {r.Detail}."); break;
                default: Log.Warn($"Config ({reason}): {who} not changed: {r.Detail}."); break;
            }
        }
        if (QueuedLine(reason, results.Where(r => r.Outcome == CfgOutcome.Queued).Select(r => CfgName(r.Target)).ToList()) is string waits) Log.Info(waits);
        int copied = results.Count(r => r.Outcome == CfgOutcome.Copied), same = results.Count(r => r.Outcome == CfgOutcome.Same),
            queued = results.Count(r => r.Outcome == CfgOutcome.Queued), failed = results.Count(r => r.Outcome is CfgOutcome.Failed or CfgOutcome.Skipped);
        if (copied + queued + failed == 0 && !manual) return;   // an automatic check that found nothing to do stays quiet
        var parts = new List<string>();
        if (copied > 0) parts.Add($"{copied} copied");
        if (same > 0) parts.Add($"{same} already the same");
        if (queued > 0) parts.Add($"{queued} waiting for CS2 to close");
        if (failed > 0) parts.Add($"{failed} not changed (see Log)");
        Cfg.State.LastResult = $"{Fmt.When(AppClock.Now)} · {char.ToUpper(reason[0]) + reason[1..]}: {string.Join(", ", parts)}.";
        Cfg.SaveState();
        if (manual) Toast(failed > 0 ? "Some accounts weren't changed. Details are in the Log." : queued > 0 ? "CS2 is open: the copy goes in when it closes." : copied > 0 ? $"Config copied to {Fmt.Count(copied, "account")}. " + (copied == 1 ? "Backup saved." : "Backups saved.") : "Every account already has the same config.", failed == 0);
        if (PageConfigs.IsVisible) ShowConfigs();
    }

    // ------------------------------------------------------------ The page

    static readonly System.Text.RegularExpressions.Regex CfgResultTime = new(@"^(\d{1,2} [A-Za-z]{3} \d{2}:\d{2}) · (.+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The last sync line, with its time in mono like every other absolute time on the page.</summary>
    void ShowCfgResult(string text)
    {
        CfgResult.Inlines.Clear();
        var m = CfgResultTime.Match(text ?? "");
        if (!m.Success) { CfgResult.Text = text; return; }
        CfgResult.Inlines.Add(new System.Windows.Documents.Run(m.Groups[1].Value) { FontFamily = (FontFamily)FindResource("Mono") });
        CfgResult.Inlines.Add(new System.Windows.Documents.Run(" · " + m.Groups[2].Value));
    }

    void ShowConfigs()
    {
        _cfgLoading = true;
        try
        {
            foreach (var rb in new[] { CfgModeOff, CfgModeMain, CfgModePrevious, CfgModeLatest }) rb.IsChecked = (string)rb.Tag == _s.ConfigMode;
            if (!CfgMode.All.Contains(_s.ConfigMode)) CfgModeOff.IsChecked = true;
            foreach (var cb in CfgCats.Children.OfType<CheckBox>()) cb.IsChecked = _s.ConfigCategories.Contains((string)cb.Tag);
            CfgOnNewBox.IsChecked = _s.ConfigOnNewAccount;
            CfgOnLoginBox.IsChecked = _s.ConfigOnLogin;
            CfgMainBox.IsEnabled = _s.ConfigMode == CfgMode.Main;
            // Sync Off: the When boxes and Sync now do nothing, so they show as disabled. The ticks stay saved for the next mode.
            var syncOn = _s.ConfigMode != CfgMode.Off;
            CfgOnNewBox.IsEnabled = CfgOnLoginBox.IsEnabled = CfgSyncNowBtn.IsEnabled = syncOn;   // one primary on the page: Copy
            CfgWhenLabel.Opacity = syncOn ? 1 : 0.5;

            var accounts = _accounts.OrderByDescending(a => a.SteamId == _s.ConfigMainId).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var snaps = Cfg.Snapshots();
            var accItems = accounts.Select(a => new CfgSourceItem(a, null)).ToList();
            var all = accItems.Concat(snaps.Select(s => new CfgSourceItem(null, s.Name))).ToList();
            // From and Compare start on the main account, else the one in use, else the one used last: never empty with accounts.
            var mainOrActive = _s.ConfigMainId is string m && _accounts.Any(a => a.SteamId == m) ? m : PreviousAccount(null);
            // The main account is a setting (Accounts > Use as main config changes it too), not a pick to keep.
            CfgMainBox.ItemsSource = accItems;
            CfgMainBox.SelectedItem = accItems.FirstOrDefault(i => i.SteamId == _s.ConfigMainId);
            Fill(CfgFromBox, all, "a:" + mainOrActive);
            Fill(CfgSnapFrom, accItems, "a:" + mainOrActive);
            Fill(CfgCmpA, all, "a:" + mainOrActive);
            var mainConv = Cfg.Read(mainOrActive).Get(CfgPart.Convars);   // read once, not once per account
            Fill(CfgCmpB, all, all.FirstOrDefault(i => i.SteamId != mainOrActive && i.IsAccount && !Cs2Config.SameSettings(CfgPart.Convars, Cfg.Read(i.SteamId).Get(CfgPart.Convars), mainConv) && Cfg.HasConfig(i.SteamId))?.Key
                ?? all.FirstOrDefault(i => i.SteamId != mainOrActive)?.Key);

            var selected = _cfgRows.Where(r => r.Selected).Select(r => r.SteamId).ToHashSet();
            _cfgRows.Clear();
            foreach (var a in accounts)
            {
                var set = Cfg.Read(a.SteamId);
                var changed = Cfg.Changed(a.SteamId);
                var xh = Cs2Config.ReadCrosshair(set);
                var linked = Cfg.IsLinked(a.SteamId);
                var (xhText, changedText) = CfgRowText(set, linked, changed);
                _cfgRows.Add(new CfgAccountRow
                {
                    Acc = a,
                    Sync = !_s.ConfigOptOut.Contains(a.SteamId),
                    Selected = selected.Contains(a.SteamId),
                    IsMain = _s.ConfigMode == CfgMode.Main &&a.SteamId == _s.ConfigMainId,
                    Pending = Cfg.State.Pending.Any(j => j.Target == a.SteamId),
                    Linked = linked,
                    CrosshairText = xhText,
                    ChangedText = changedText,
                    CrosshairImage = CrosshairImage(xh, 26),   // the Image in the Accounts rows is 26 px
                    GroupKey = set.Empty ? CfgAccountRow.NoConfig : Cs2Config.CrosshairSummary(set) ?? "default",
                });
            }
            foreach (var r in _cfgRows) r.PropertyChanged += (_, ev) => { if (ev.PropertyName == nameof(CfgAccountRow.Selected)) ShowCfgToCount(); };
            ShowCfgAccounts();
            _cfgToView = new System.Windows.Data.CollectionViewSource { Source = _cfgRows }.View;
            _cfgToView.Filter = o => o is CfgAccountRow r && r.SteamId != (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId && CfgAccountRow.Matches(r, CfgToSearch.Text);
            CfgToList.ItemsSource = _cfgToView;
            ShowCfgToCount();
            CfgAccountsEmpty.Visibility = _cfgRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // No accounts: nothing to copy to or save, so the buttons don't look like they'd do something.
            var any = _cfgRows.Count > 0;
            CfgCopyBtn.IsEnabled = CfgToAllBtn.IsEnabled = CfgToNoneBtn.IsEnabled = CfgSnapSaveBtn.IsEnabled = any;
            CfgCopyHint.Text = any ? "Each account gets a backup first (Configs > Backups)." : "No accounts to copy to yet.";

            CfgSnapList.ItemsSource = snaps.Select(s => new CfgSnapshotRow(s)).ToList();
            CfgSnapEmpty.Visibility = snaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // The 60 newest, and each account's original (its config from before the app changed it) even when older
            var allBackups = Cfg.Backups();
            var backups = allBackups.Take(60).Concat(allBackups.Skip(60).Where(b => b.Original)).Select(b => new CfgBackupRow(b, CfgName(b.SteamId))).ToList();
            CfgBackupList.ItemsSource = backups;
            CfgBackupEmpty.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CfgUndoBtn.IsEnabled = Cfg.CanUndo;   // nothing to put back: greyed out like Sync now
            ShowCfgResult(CfgSyncLine(_s.ConfigMode, _s.ConfigCategories.Count > 0, Cfg.State.LastResult));
        }
        catch (Exception ex) { Log.Warn("Configs page: " + Cs2ConfigStore.Why(ex)); }
        finally { _cfgLoading = false; }
        UpdateCompare();
    }

    /// <summary>
    /// An account's two lines on the Configs page. A CS2 folder that is a link or junction is neither read nor written (the app runs as
    /// admin), so it isn't "no config yet" and the sync never reaches it.
    /// </summary>
    public static (string Crosshair, string Changed) CfgRowText(CfgSet set, bool linked, DateTime? changed) =>
        linked ? ("CS2 folder is a link", "The app doesn't read or write it")
        : set.Empty ? ("No CS2 config yet", "Opens with CS2's defaults (or gets the sync)")
        : (Cs2Config.CrosshairSummary(set) ?? "Default crosshair", changed != null ? "Changed in game " + Fmt.Ago(changed) : "Not changed in game since the last copy");

    /// <summary>The line under Sync now: the last result, unless the sync is on with nothing ticked to copy.</summary>
    public static string CfgSyncLine(string mode, bool anyTicked, string last) =>
        mode != CfgMode.Off && !anyTicked ? "Sync is on, but nothing is ticked in Copy > What to copy."
        : last ?? (mode == CfgMode.Off ? "Sync is off. To copy once, use Copy." : "No sync yet.");

    /// <summary>Undo last sync when some accounts stayed as they were.</summary>
    public static string UndoToast(int n, IReadOnlyList<string> names) => n == 0
        ? $"Nothing was undone: {string.Join(", ", names)} stayed as they were. Restore them from Backups."
        : $"Undone on {Fmt.Count(n, "account")}. {string.Join(", ", names)} stayed as they were: restore them from Backups.";

    /// <summary>Accounts > Use as main config: what reaches the others, from the When boxes that are on.</summary>
    public static string MainConfigToast(string name, bool onNew, bool onLogin) => $"{name} is the main config now. " + (onNew, onLogin) switch
    {
        (true, true) => "New accounts and logins through the app get it (Configs page).",
        (true, false) => "New accounts get it (Configs page).",
        (false, true) => "Logins through the app get it (Configs page).",
        _ => "Turn on Configs > Sync > When so the sync uses it.",
    };

    static void Fill(ComboBox box, List<CfgSourceItem> items, string key)
    {
        var keep = (box.SelectedItem as CfgSourceItem)?.Key ?? key;
        box.ItemsSource = items;
        box.SelectedItem = items.FirstOrDefault(i => i.Key == keep) ?? items.FirstOrDefault(i => i.Key == key);
    }

    void CfgMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_cfgLoading || sender is not RadioButton rb) return;
        _s.ConfigMode = (string)rb.Tag;
        if (_s.ConfigMode == CfgMode.Off) Cfg.DropQueued(_ => true);   // Sync off: the copies waiting for CS2 to close go too
        if (_s.ConfigMode == CfgMode.Main && _s.ConfigMainId == null) _s.ConfigMainId = (CfgMainBox.SelectedItem as CfgSourceItem)?.SteamId ?? _accounts.FirstOrDefault(a => a.Active)?.SteamId;
        SaveSettings();
        Log.Info("Config sync: " + _s.ConfigMode switch
        {
            CfgMode.Main => _s.ConfigMainId == null || _accounts.All(a => a.SteamId != _s.ConfigMainId) ? "from a main account (pick one below)." : $"from the main account ({CfgName(_s.ConfigMainId)}).",
            CfgMode.Previous => "from the previous account.",
            CfgMode.Latest => "from the account changed last.", _ => "off.",
        });
        ShowConfigs();
    }

    void CfgMain_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_cfgLoading || CfgMainBox.SelectedItem is not CfgSourceItem it || it.SteamId == _s.ConfigMainId) return;
        _s.ConfigMainId = it.SteamId;
        SaveSettings();
        Log.Info($"Config sync: the main account is now {it.Label}.");
        ShowConfigs();
    }

    void CfgCategory_Click(object sender, RoutedEventArgs e)
    {
        _s.ConfigCategories = CfgCats.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        SaveSettings();
        ShowCfgResult(CfgSyncLine(_s.ConfigMode, _s.ConfigCategories.Count > 0, Cfg.State.LastResult));
    }

    void CfgWhen_Click(object sender, RoutedEventArgs e)
    {
        _s.ConfigOnNewAccount = CfgOnNewBox.IsChecked == true;
        _s.ConfigOnLogin = CfgOnLoginBox.IsChecked == true;
        SaveSettings();
    }

    void CfgOptOut_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CfgAccountRow r) return;
        _s.ConfigOptOut.Remove(r.SteamId);
        if (!r.Sync) _s.ConfigOptOut.Add(r.SteamId);
        SaveSettings();
        if (!r.Sync && Cfg.DropQueued(j => j.Target == r.SteamId) > 0 && PageConfigs.IsVisible) ShowConfigs();
        Toast(r.Sync ? $"The sync copies to {r.Name} again." : $"The sync won't touch {r.Name}'s config.", true);
    }

    void CfgSyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (_s.ConfigMode == CfgMode.Off) { Toast("Pick a mode under Sync first, or use Copy.", false); return; }
        if (_s.ConfigMode == CfgMode.Main && _s.ConfigMainId == null) { Toast("Pick the main account first.", false); return; }
        if (_s.ConfigCategories.Count == 0) { Toast("Tick something in Copy > What to copy first: the sync copies what is ticked there.", false); return; }
        var jobs = CfgPlan(CfgTrigger.Manual, null, _accounts.FirstOrDefault(a => a.Active)?.SteamId ?? PreviousAccount(null));
        if (jobs.Count == 0) { Toast(_s.ConfigMode == CfgMode.Latest ? "No account has a config changed in game yet." : "No account to copy to: turn Sync on for some.", false); return; }
        RunCfgJobs(jobs, CfgTrigger.Manual, true);
    }

    void CfgUndo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var failed = new List<string>();
            var n = Cfg.UndoLast(NewSyncId(), failed);
            if (n < 0) { Toast("Close CS2 first: it would save over the undo.", false); return; }
            if (n == 0 && failed.Count == 0) { Toast("Nothing to undo yet.", false); return; }
            if (n > 0) Log.Ok($"Config: undid the last sync ({Fmt.Count(n, "account")} back as {(n == 1 ? "it was" : "they were")}).");
            if (failed.Count > 0)
            {
                var names = string.Join(", ", failed.Select(CfgName));
                Log.Warn($"Config undo: {names} stayed as they were. Restore them from Backups.");
                Toast(UndoToast(n, failed.Select(CfgName).ToList()), n == 0 ? Tone.Error : Tone.Warn);
            }
            else Toast($"Undone: {Fmt.Count(n, "account")} back as {(n == 1 ? "it was" : "they were")} before the last sync.", true);
            ShowConfigs();
        }
        catch (Exception ex) { Log.Error("Config undo: " + Cs2ConfigStore.Why(ex)); Toast("Couldn't undo: " + Cs2ConfigStore.Why(ex), Tone.Error); }
    }

    void CfgRestore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CfgBackupRow row) return;
        try
        {
            if (!Cfg.Restore(row.Backup, NewSyncId())) { Toast("Close CS2 first: it would save over the restored files.", false); return; }
            Log.Ok($"Config: restored the backup of {row.Account} from {row.When}.");
            Toast($"{row.Account}: config back as it was on {row.When}. Undo last sync reverts it.", true);
            ShowConfigs();
        }
        catch (Exception ex) { Log.Error("Config restore: " + Cs2ConfigStore.Why(ex)); Toast("Couldn't restore: " + Cs2ConfigStore.Why(ex), Tone.Error); }
    }

    // ------------------------------------------------------------ The Accounts list with many accounts (beta.10)

    /// <summary>Groups opened with Show (by crosshair), kept while the page is rebuilt.</summary>
    readonly HashSet<string> _cfgOpen = new();

    void ShowCfgAccounts()
    {
        var mainName = _cfgRows.FirstOrDefault(r => r.IsMain)?.Name;
        var list = CfgDisplay(_cfgRows, CfgAccSearch.Text, _cfgOpen, mainName);
        foreach (var g in list.OfType<CfgGroupRow>()) g.Toggle = () => { if (!_cfgOpen.Remove(g.Key)) _cfgOpen.Add(g.Key); ShowCfgAccounts(); };
        CfgAccountsList.ItemsSource = list;
        CfgAccCount.Text = _cfgRows.Count < 2 ? "" : string.IsNullOrWhiteSpace(CfgAccSearch.Text) ? Fmt.Count(_cfgRows.Count, "account")
            : $"{list.OfType<CfgAccountRow>().Count()} of {_cfgRows.Count}";
        CfgAccSearchHost.Visibility = _cfgRows.Count >= 8 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// What the Accounts list shows: the main account and the one in use on their own at the top; then accounts that share a crosshair
    /// (3 or more) as one line each, "23 accounts with the same crosshair as nightfall", opened with Show; then the rest by name. An
    /// account waiting for CS2 to close or with Sync off is never folded into a group (it is the one that differs). A search shows the
    /// matching accounts, no groups.
    /// </summary>
    public static List<object> CfgDisplay(IReadOnlyList<CfgAccountRow> rows, string query, ISet<string> open, string mainName, int minGroup = 3)
    {
        var res = new List<object>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            res.AddRange(rows.Where(r => CfgAccountRow.Matches(r, query)));
            return res;
        }
        bool Alone(CfgAccountRow r) => r.IsMain || r.Active || r.Pending || !r.Sync || r.Linked;
        res.AddRange(rows.Where(r => r.IsMain).Concat(rows.Where(r => r.Active && !r.IsMain)));
        var mainKey = rows.FirstOrDefault(r => r.IsMain)?.GroupKey;
        var rest = rows.Where(r => !r.IsMain && !r.Active).ToList();
        var groups = rest.Where(r => !Alone(r)).GroupBy(r => r.GroupKey).Where(g => g.Count() >= minGroup)
            .OrderBy(g => g.Key == CfgAccountRow.NoConfig).ThenByDescending(g => g.Key == mainKey).ThenByDescending(g => g.Count()).ToList();
        var folded = groups.SelectMany(g => g).ToHashSet();
        foreach (var g in groups)
        {
            var members = g.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var first = members[0];
            var group = new CfgGroupRow
            {
                Key = g.Key, Rows = members, Open = open.Contains(g.Key), CrosshairImage = first.CrosshairImage,
                Title = g.Key == CfgAccountRow.NoConfig ? $"{members.Count} accounts with no CS2 config yet"
                    : g.Key == mainKey && mainName != null ? $"{members.Count} accounts with the same crosshair as {mainName}"
                    : $"{members.Count} accounts with the same crosshair",
                Detail = g.Key == CfgAccountRow.NoConfig ? "They open with CS2's defaults, or get the sync." : first.CrosshairText,
                Names = string.Join(", ", members.Take(6).Select(r => r.Name)) + (members.Count > 6 ? $" and {members.Count - 6} more" : ""),
            };
            res.Add(group);
            if (group.Open) res.AddRange(members);
        }
        res.AddRange(rest.Where(r => !folded.Contains(r)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));
        return res;
    }

    void CfgGroupToggle_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as CfgGroupRow)?.Toggle?.Invoke();

    void CfgAccSearch_Changed(object sender, TextChangedEventArgs e) { if (!_cfgLoading) ShowCfgAccounts(); }

    void CfgToSearch_Changed(object sender, TextChangedEventArgs e) => _cfgToView?.Refresh();

    void ShowCfgToCount()
    {
        var from = (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId;
        var n = _cfgRows.Count(r => r.Selected && r.SteamId != from);
        CfgToCount.Text = n == 0 ? "None ticked" : $"{n} of {Fmt.Count(_cfgRows.Count(r => r.SteamId != from), "account")} ticked";
    }

    // ------------------------------------------------------------ Copy

    /// <summary>All ticks the accounts shown under To (with a filter typed, only those).</summary>
    void CfgToAll_Click(object sender, RoutedEventArgs e)
    {
        if (_cfgToView == null) return;
        foreach (var r in _cfgToView.OfType<CfgAccountRow>()) r.Selected = true;
    }

    void CfgToNone_Click(object sender, RoutedEventArgs e) { foreach (var r in _cfgRows) r.Selected = false; }

    void CfgFrom_Changed(object sender, SelectionChangedEventArgs e)
    {
        var from = (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId;
        if (from != null) foreach (var r in _cfgRows) if (r.SteamId == from) r.Selected = false;   // a hidden tick must not stay on
        _cfgToView?.Refresh();
        if (CfgToCount != null) ShowCfgToCount();
    }

    void CfgCopy_Click(object sender, RoutedEventArgs e)
    {
        if (CfgFromBox.SelectedItem is not CfgSourceItem from) { Toast("Pick where to copy from.", false); return; }
        var targets = _cfgRows.Where(r => r.Selected && r.SteamId != from.SteamId).ToList();
        if (targets.Count == 0)
        {
            Toast(_cfgRows.Any(r => r.Selected) ? "That is the account you copy from. Tick a different one under To." : "Tick at least one account under To.", false);
            return;
        }
        if (_s.ConfigCategories.Count == 0) { Toast("Tick something in Copy > What to copy.", false); return; }
        var jobs = targets.Select(t => new CfgJob { SourceId = from.SteamId, Snapshot = from.Snapshot, Target = t.SteamId, Categories = _s.ConfigCategories.ToList(), At = AppClock.Now }).ToList();
        RunCfgJobs(jobs, CfgTrigger.Copy, true);
    }

    /// <summary>Account menu > Copy config to: opens Copy with this account as the source.</summary>
    void AccCfgCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        foreach (var r in _cfgRows) r.Selected = false;
        NavConfigs.IsChecked = true;
        ShowConfigs();
        CfgFromBox.SelectedItem = (CfgFromBox.ItemsSource as List<CfgSourceItem>)?.FirstOrDefault(i => i.SteamId == a.SteamId);
        Dispatcher.BeginInvoke(() => CfgCopyGroup.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        Toast($"Copying from {a.Name}: tick the accounts under To, then Copy.", true);
    }

    /// <summary>Account menu > Use as main config: the sync copies from this account from now on.</summary>
    void AccCfgMain_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        _s.ConfigMainId = a.SteamId;
        _s.ConfigMode = CfgMode.Main;
        _s.ConfigOptOut.Remove(a.SteamId);
        SaveSettings();
        Log.Info($"Config sync: from the main account ({a.Name}).");
        Toast(MainConfigToast(a.Name, _s.ConfigOnNewAccount, _s.ConfigOnLogin), _s.ConfigOnNewAccount || _s.ConfigOnLogin);
        if (PageConfigs.IsVisible) ShowConfigs();
    }

    // ------------------------------------------------------------ Snapshots

    async void CfgSnapSave_Click(object sender, RoutedEventArgs e)
    {
        if (CfgSnapFrom.SelectedItem is not CfgSourceItem from || from.SteamId == null) { Toast("Pick the account to save.", false); return; }
        if (Cfg.IsLinked(from.SteamId)) { Toast("That account's CS2 folder is a link or junction, so the app doesn't read it.", false); return; }
        DialogInput.Text = "";
        DialogInput.Tag = "E.g. Main, Practice, Retakes";
        if (!await Ask($"Snapshot of {from.Label}", "Saves this account's CS2 config under a name. You can copy it to other accounts later, or export it to another PC.", "Save", "Cancel", inputs: 1, label1: "Name")) return;
        var name = DialogInput.Text;   // the next dialog uses the same box
        if (Cfg.FindSnapshot(name) is CfgSnapshot old
            && !await Ask($"Replace snapshot \"{old.Name}\"?", $"It was saved on {Fmt.When(old.Created)}. Saving replaces it.", "Replace", "Cancel", danger: true)) return;
        try
        {
            var s = Cfg.SaveSnapshot(name, Cfg.Read(from.SteamId), from.Label);
            Log.Ok($"Config: saved snapshot \"{s.Name}\" from {from.Label}.");
            Toast($"Snapshot \"{s.Name}\" saved.", true);
            ShowConfigs();
        }
        catch (Exception ex) { Toast(Cs2ConfigStore.Why(ex), false); }
    }

    void CfgSnapUse_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CfgSnapshotRow s) return;
        CfgFromBox.SelectedItem = (CfgFromBox.ItemsSource as List<CfgSourceItem>)?.FirstOrDefault(i => i.Snapshot == s.Name);
        CfgCopyGroup.BringIntoView();
        Toast($"Copying snapshot \"{s.Name}\": tick the accounts under To, then Copy.", true);
    }

    void CfgSnapExport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CfgSnapshotRow s) return;
        if (Rec.On) { Toast("Off in the recording build: Windows' file window isn't filtered. Use the normal app to export.", false); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Snapshot (*.zip)|*.zip", FileName = $"CS2 config - {s.Name}.zip", Title = "Export snapshot" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Cfg.ExportSnapshot(s.Name, dlg.FileName);
            Log.Ok($"Config: exported snapshot \"{s.Name}\".");
            Toast($"Exported to {dlg.FileName}. It has the config files only, no account names.", true);
        }
        catch (Exception ex) { Log.Error("Config export: " + Cs2ConfigStore.Why(ex)); Toast("Couldn't export: " + Cs2ConfigStore.Why(ex), Tone.Error); }
    }

    void CfgSnapImport_Click(object sender, RoutedEventArgs e)
    {
        if (Rec.On) { Toast("Off in the recording build: Windows' file window isn't filtered. Use the normal app to import.", false); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Snapshot (*.zip)|*.zip", Title = "Import snapshot" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var name = Cfg.ImportSnapshot(dlg.FileName);
            Log.Ok($"Config: imported snapshot \"{name}\".");
            Toast($"Snapshot \"{name}\" imported. Copy it to accounts from Copy.", true);
            ShowConfigs();
        }
        catch (Exception ex) { Log.Warn("Config import: " + Cs2ConfigStore.Why(ex)); Toast("Couldn't import: " + Cs2ConfigStore.Why(ex), false); }
    }

    async void CfgSnapDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CfgSnapshotRow s) return;
        if (!await Ask("Delete snapshot", $"Delete \"{s.Name}\"? Your accounts keep their config; only the saved copy goes.", "Delete", danger: true)) return;
        try { Cfg.DeleteSnapshot(s.Name); Log.Info($"Config: deleted snapshot \"{s.Name}\"."); ShowConfigs(); }
        catch (Exception ex) { Toast("Couldn't delete: " + Cs2ConfigStore.Why(ex), false); }
    }

    // ------------------------------------------------------------ Compare

    void CfgCompare_Changed(object sender, SelectionChangedEventArgs e) { if (!_cfgLoading) UpdateCompare(); }

    CfgSet CfgRead(CfgSourceItem it) => it == null ? null : it.Snapshot != null ? Cfg.ReadSnapshot(it.Snapshot) : Cfg.Read(it.SteamId);

    void UpdateCompare()
    {
        var a = CfgCmpA.SelectedItem as CfgSourceItem;
        var b = CfgCmpB.SelectedItem as CfgSourceItem;
        var sa = CfgRead(a);
        var sb = CfgRead(b);
        CfgCmpImgA.Source = CrosshairImage(Cs2Config.ReadCrosshair(sa), 32);
        CfgCmpImgB.Source = CrosshairImage(Cs2Config.ReadCrosshair(sb), 32);
        string None(CfgSourceItem it) => it?.SteamId is string id && Cfg.IsLinked(id) ? "CS2 folder is a link" : "No CS2 config";
        CfgCmpXhA.Text = sa == null ? "" : sa.Empty ? None(a) : Cs2Config.CrosshairSummary(sa) ?? "Default crosshair";
        CfgCmpXhB.Text = sb == null ? "" : sb.Empty ? None(b) : Cs2Config.CrosshairSummary(sb) ?? "Default crosshair";
        // No empty preview boxes: a side without a crosshair shows only its text, and the row goes when nothing is picked.
        if (CfgCmpImgA.Parent is FrameworkElement boxA) boxA.Visibility = CfgCmpImgA.Source != null ? Visibility.Visible : Visibility.Collapsed;
        if (CfgCmpImgB.Parent is FrameworkElement boxB) boxB.Visibility = CfgCmpImgB.Source != null ? Visibility.Visible : Visibility.Collapsed;
        if ((CfgCmpImgA.Parent as FrameworkElement)?.Parent is FrameworkElement side && side.Parent is FrameworkElement row)
            row.Visibility = sa == null && sb == null ? Visibility.Collapsed : Visibility.Visible;
        if (a == null || b == null || a.Key == b.Key)
        {
            var count = (CfgCmpA.ItemsSource as List<CfgSourceItem>)?.Count ?? 0;
            CfgDiffCount.Text = count == 0 ? "No accounts or snapshots to compare yet."
                : count == 1 ? "Only one account or snapshot so far: nothing to compare it with."
                : "Pick two different accounts or snapshots.";
            CfgDiffList.ItemsSource = null;
            return;
        }
        try
        {
            var diffs = Cs2Config.Diff(sa, sb);
            CfgDiffCount.Text = diffs.Count == 0 ? "Same settings." : diffs.Count == 1 ? "1 setting differs." : $"{diffs.Count} settings differ.";
            CfgDiffList.ItemsSource = diffs.Select(d => new CfgDiffRow(d)).ToList();
        }
        catch (Exception ex) { CfgDiffCount.Text = "Couldn't compare: " + Cs2ConfigStore.Why(ex); CfgDiffList.ItemsSource = null; }
    }

    /// <summary>The crosshair as CS2 draws it at 1080p, on a <paramref name="box"/> px square (26 in the lists, 32 in Compare).</summary>
    static ImageSource CrosshairImage(Crosshair c, double box)
    {
        if (c == null) return null;
        var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, box, box)) };
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, box, box));
            var fill = new SolidColorBrush(Color.FromArgb(Math.Max(c.Alpha, (byte)70), c.R, c.G, c.B));
            var edge = new SolidColorBrush(Color.FromArgb(c.OutlineA, c.OutlineR, c.OutlineG, c.OutlineB));
            foreach (var s in CrosshairShapes(c, box)) dc.DrawRectangle(s.Outline ? edge : fill, null, s.R);
        }
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }

    /// <summary>
    /// The rectangles of the preview on a <paramref name="box"/> px square, outlines first: CS2's pixels at 1080p
    /// (<see cref="Cs2Config.CrosshairPixels"/>) at twice the size, or smaller when that doesn't fit, everything by the same amount so
    /// the bars keep their length against the gap (beta.12 cut big bars at the edge and kept the gap, 7 nov.).
    /// </summary>
    public static List<CrosshairShape> CrosshairShapes(Crosshair c, double box)
    {
        var shapes = new List<CrosshairShape>();
        if (c == null) return shapes;
        var p = Cs2Config.CrosshairPixels(c, 1080);
        var lines = p.Lines && p.Length > 0;
        var o = p.Outline > 0 ? p.OutlineWidth : 0;
        var t = Math.Max(p.Thickness, 1);
        // How far from the center it reaches, outline included (a negative gap puts the bars across the center)
        var reach = Math.Max(lines ? Math.Max(p.Gap + p.Length, -p.Gap) : 0, t / 2.0) + o;
        double len, gap, th, ow, a, right, left;
        // Whole pixels, so the lines stay sharp; rounding up can leave a bar past the edge, so a little smaller until it fits
        for (var s = Math.Min(2, (box / 2 - 1) / Math.Max(reach, 0.5)); ; s *= 0.95)
        {
            len = Math.Max(1, Math.Round(p.Length * s)); gap = Math.Round(p.Gap * s); th = Math.Max(1, Math.Round(t * s)); ow = o > 0 ? Math.Max(1, Math.Round(o * s)) : 0;
            a = Math.Floor(box / 2 - th / 2);      // top and left of the bars across the center
            var cx = a + th / 2;                   // the center line
            right = Math.Round(cx + gap, MidpointRounding.AwayFromZero);
            left = 2 * cx - right;                 // the mirror of the right bar's start
            var far = Math.Max(a + th, lines ? Math.Max(right + len, left) : 0);   // the right edge (the left one is its mirror)
            if (far + ow <= Math.Min(box, 2 * cx) || s < 0.05) break;
        }
        var rects = new List<Rect>();
        if (lines)
        {
            rects.Add(new Rect(right, a, len, th));
            rects.Add(new Rect(left - len, a, len, th));
            rects.Add(new Rect(a, right, th, len));
            if (!p.T) rects.Add(new Rect(a, left - len, th, len));
        }
        if (p.Dot) rects.Add(new Rect(a, a, th, th));
        foreach (var r in rects)
            shapes.Add(new CrosshairShape(p.Outline == 2 ? new Rect(r.X - ow, r.Y - ow, r.Width + ow, r.Height + ow) : new Rect(r.X - ow, r.Y - ow, r.Width + 2 * ow, r.Height + 2 * ow), true));
        if (ow == 0) shapes.Clear();
        foreach (var r in rects) shapes.Add(new CrosshairShape(r, false));
        return shapes;
    }
}

/// <summary>One rectangle of the crosshair preview: the outline (drawn first) or the crosshair itself.</summary>
public sealed record CrosshairShape(Rect R, bool Outline);

/// <summary>An account on the Configs page (Accounts list and the To ticks in Copy).</summary>
public sealed class CfgAccountRow : Observable
{
    bool _sync = true, _selected;
    public AccountItem Acc { get; init; }
    public string SteamId => Acc.SteamId;
    /// <summary>The name in sentences: spaces and the blank characters Steam names use as padding (Hangul filler, braille blank) trimmed, so a toast never reads "copies to  atlas".</summary>
    public string Name => ShownName(Acc.Name);
    public static string ShownName(string n)
    {
        var t = (n ?? "").Trim().Trim('\u3164', '\u115F', '\u1160', '\uFFA0', '\u2800', '\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF').Trim();
        return t.Length == 0 ? "this account" : t;
    }
    public string Initial => Acc.Initial;
    public ImageSource Avatar => Acc.Avatar;
    public bool HasAvatar => Acc.HasAvatar;
    public bool Active => Acc.Active;
    public bool Sync { get => _sync; set => Set(ref _sync, value); }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool IsMain { get; init; }
    public bool Pending { get; init; }
    /// <summary>Its CS2 folder is a link or junction: never read or written, so never folded in with the accounts that have no config.</summary>
    public bool Linked { get; init; }
    public string CrosshairText { get; init; }
    public string ChangedText { get; init; }
    public ImageSource CrosshairImage { get; init; }
    public bool HasCrosshair => CrosshairImage != null;
    /// <summary>Accounts with the same key share a crosshair (or have no config): the list folds them into one line.</summary>
    public string GroupKey { get; init; }
    public const string NoConfig = "\u0000none";
    /// <summary>The search under Accounts and the filter under To: name or login.</summary>
    public static bool Matches(CfgAccountRow r, string q) => string.IsNullOrWhiteSpace(q)
        || (r.Name ?? "").Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase) || (r.Acc?.Login ?? "").Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>One line for several accounts with the same crosshair (Configs > Accounts with many accounts).</summary>
public sealed class CfgGroupRow
{
    public string Key { get; init; }
    public string Title { get; init; }
    public string Detail { get; init; }
    public string Names { get; init; }
    public List<CfgAccountRow> Rows { get; init; }
    public ImageSource CrosshairImage { get; init; }
    public bool HasCrosshair => CrosshairImage != null;
    public bool IsEmptyGroup => Key == CfgAccountRow.NoConfig;
    public bool IsCrosshairGroup => !IsEmptyGroup;
    public bool Open { get; init; }
    public string ToggleText => Open ? "Hide" : "Show";
    public string ToggleTip => Open ? "Back to one line for this group" : "Show each account of this group (Sync on or off one by one)";
    public Action Toggle { get; set; }
}

/// <summary>An account or a snapshot in the pickers.</summary>
public sealed class CfgSourceItem
{
    public CfgSourceItem(AccountItem account, string snapshot) { Account = account; Snapshot = snapshot; }
    public AccountItem Account { get; }
    public string Snapshot { get; }
    public string SteamId => Account?.SteamId;
    public string Key => Account != null ? "a:" + Account.SteamId : "s:" + Snapshot;
    public string Label => Account?.Name ?? Snapshot;
    public bool IsAccount => Account != null;
    public bool IsSnapshot => Account == null;
    public string Initial => Account?.Initial;
    public ImageSource Avatar => Account?.Avatar;
    public bool HasAvatar => Account?.HasAvatar == true;
    public override string ToString() => Label;
}

public sealed class CfgSnapshotRow
{
    public CfgSnapshotRow(CfgSnapshot s)
    {
        Name = s.Name;
        var parts = s.Parts.Select(p => p switch { "Keys" => "binds", "Convars" => "game and crosshair", "Video" => "video",
            "Machine" => s.Parts.Count > 1 ? "HUD/radar/audio" : Cs2Config.CategoryName("hud"), _ => p });   // in a list, as Cs2Config.CategoryList says it
        When = Fmt.When(s.Created);
        What = string.Join(", ", parts) + (s.From == "imported" ? " · imported" : "");
        Detail = $"{When} · {What}";
    }
    public string Name { get; }
    public string Detail { get; }
    /// <summary>The time on its own, so the row shows it in mono like Backups and Previous IPs.</summary>
    public string When { get; }
    public string What { get; }
}

public sealed class CfgBackupRow
{
    public CfgBackupRow(CfgBackup b, string account)
    {
        Backup = b;
        Account = account;
        When = Fmt.When(b.At);
        Detail = (b.Original ? "Original · " : "") + MainWindow.CfgReasonText(b.Reason) + (b.From != null ? $" · from {b.From}" : "") + (b.Files.Count > 0 ? " · " + N(b.Files.Count, "file") : " · " + N(b.Created.Count, "new file"));
    }
    static string N(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";
    public CfgBackup Backup { get; }
    public string Account { get; }
    public string When { get; }
    public string Detail { get; }
}

public sealed class CfgDiffRow
{
    public CfgDiffRow(CfgDiff d)
    {
        Key = d.Key; A = d.A; B = d.B;
        Label = d.Label.StartsWith("Crosshair: ") ? char.ToUpper(d.Label[11]) + d.Label[12..] : d.Label;   // the category column already says crosshair
        CategoryText = d.Category switch { "game" => "game", "hud" => "HUD, audio", _ => d.Category };
    }
    public string Key { get; }
    public string Label { get; }
    public string A { get; }
    public string B { get; }
    public string CategoryText { get; }
}
