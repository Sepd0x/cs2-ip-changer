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
            if (st.Known == null) { st.Known = _accMeta.Keys.ToList(); Cfg.SaveState(); return; }
            var fresh = _accounts.Where(a => !st.Known.Contains(a.SteamId)).ToList();
            if (fresh.Count == 0) return;
            st.Known.AddRange(fresh.Select(a => a.SteamId));
            Cfg.SaveState();
            if (!_s.ConfigOnNewAccount || _s.ConfigMode == "off") return;
            var jobs = fresh.SelectMany(a => CfgPlan("new", a.SteamId, PreviousAccount(a.SteamId))).ToList();
            RunCfgJobs(jobs, "new", false);
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
            if (!_s.ConfigOnLogin || _s.ConfigMode == "off") return;
            // The account in use, else the last one logged in, this one included: logging in to that same account again copies nothing.
            var prev = PreviousAccount(null);
            RunCfgJobs(CfgPlan("login", a.SteamId, prev), "login", false);
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
            if (!_s.ConfigOnLogin || _s.ConfigMode is "off" or "previous" || Steam.InUseSteamId() is not string active) return;
            RunCfgJobs(CfgPlan("login", active, null), "cs2", false);
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
            // Closed by the account switch: the switch already copied, and CS2 killed that way saved nothing new.
            if (_s.ConfigMode != "latest" || (DateTime.Now - _cs2ClosedByAppAt).TotalSeconds < 60) return;
            RunCfgJobs(CfgPlan("closed", null, null), "closed", false);
        }
        catch (Exception ex) { Log.Warn("Config sync after CS2 closed: " + Cs2ConfigStore.Why(ex)); }
    }

    void FlushCfgQueue()
    {
        if (Cfg.State.Pending.Count == 0) return;
        var results = Cfg.FlushPending(NewSyncId(), j => CfgSourceLabel(j));
        if (results.Count > 0) ReportCfg(results, "queued", false);
    }

    /// <summary>The copies for a trigger, from the settings (Sync on/off per account, mode, categories).</summary>
    List<CfgJob> CfgPlan(string trigger, string target, string previous)
    {
        var ids = _accounts.Select(a => a.SteamId).ToList();
        var opt = _s.ConfigOptOut.ToHashSet();
        // An account with Sync off is never the source: the latest change among the others wins.
        var latest = _s.ConfigMode == "latest" ? Cfg.PickLatest(ids.Where(i => !opt.Contains(i))) : null;
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
        "new" => "new account", "login" => "before login", "cs2" => "before CS2 opened", "closed" => "CS2 closed", "manual" => "Sync now",
        "copy" => "copy", "restore" => "before a restore", "queued" => "after CS2 closed", _ => r,
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
                case CfgOutcome.Queued: Log.Info($"Config ({reason}): CS2 is open, so the copy to {who} waits until it closes."); break;
                case CfgOutcome.Same: if (manual) Log.Info($"Config ({reason}): {who} already has the same config."); break;
                case CfgOutcome.Skipped: Log.Info($"Config ({reason}): {who} not changed: {r.Detail}."); break;
                default: Log.Warn($"Config ({reason}): {who} not changed: {r.Detail}."); break;
            }
        }
        int copied = results.Count(r => r.Outcome == CfgOutcome.Copied), same = results.Count(r => r.Outcome == CfgOutcome.Same),
            queued = results.Count(r => r.Outcome == CfgOutcome.Queued), failed = results.Count(r => r.Outcome is CfgOutcome.Failed or CfgOutcome.Skipped);
        if (copied + queued + failed == 0 && !manual) return;   // an automatic check that found nothing to do stays quiet
        var parts = new List<string>();
        if (copied > 0) parts.Add($"{copied} copied");
        if (same > 0) parts.Add($"{same} already the same");
        if (queued > 0) parts.Add($"{queued} waiting for CS2 to close");
        if (failed > 0) parts.Add($"{failed} not changed (see Log)");
        Cfg.State.LastResult = $"{Fmt.When(DateTime.Now)} · {char.ToUpper(reason[0]) + reason[1..]}: {string.Join(", ", parts)}.";
        Cfg.SaveState();
        if (manual) Toast(failed > 0 ? "Some accounts weren't changed. Details are in the Log." : queued > 0 ? "CS2 is open: the copy goes in when it closes." : copied > 0 ? $"Config copied to {Fmt.Count(copied, "account")}. " + (copied == 1 ? "Backup saved." : "Backups saved.") : "Every account already has the same config.", failed == 0);
        if (PageConfigs.IsVisible) ShowConfigs();
    }

    // ------------------------------------------------------------ The page

    void ShowConfigs()
    {
        _cfgLoading = true;
        try
        {
            foreach (var rb in new[] { CfgModeOff, CfgModeMain, CfgModePrevious, CfgModeLatest }) rb.IsChecked = (string)rb.Tag == _s.ConfigMode;
            if (!new[] { "off", "main", "previous", "latest" }.Contains(_s.ConfigMode)) CfgModeOff.IsChecked = true;
            foreach (var cb in CfgCats.Children.OfType<CheckBox>()) cb.IsChecked = _s.ConfigCategories.Contains((string)cb.Tag);
            CfgOnNewBox.IsChecked = _s.ConfigOnNewAccount;
            CfgOnLoginBox.IsChecked = _s.ConfigOnLogin;
            CfgMainBox.IsEnabled = _s.ConfigMode == "main";
            // Sync Off: the When boxes and Sync now do nothing, so they show as disabled. The ticks stay saved for the next mode.
            var syncOn = _s.ConfigMode != "off";
            CfgOnNewBox.IsEnabled = CfgOnLoginBox.IsEnabled = CfgSyncNowBtn.IsEnabled = syncOn;
            CfgSyncNowBtn.Style = (Style)FindResource(syncOn ? "Primary" : "Btn");   // with Off, Copy is the one to press
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
            Fill(CfgCmpB, all, all.FirstOrDefault(i => i.SteamId != mainOrActive && i.IsAccount && !Cs2Config.SameText(Cfg.Read(i.SteamId).Get(CfgPart.Convars), Cfg.Read(mainOrActive).Get(CfgPart.Convars)) && Cfg.HasConfig(i.SteamId))?.Key
                ?? all.FirstOrDefault(i => i.SteamId != mainOrActive)?.Key);

            var selected = _cfgRows.Where(r => r.Selected).Select(r => r.SteamId).ToHashSet();
            _cfgRows.Clear();
            foreach (var a in accounts)
            {
                var set = Cfg.Read(a.SteamId);
                var changed = Cfg.Changed(a.SteamId);
                var xh = Cs2Config.ReadCrosshair(set);
                _cfgRows.Add(new CfgAccountRow
                {
                    Acc = a,
                    Sync = !_s.ConfigOptOut.Contains(a.SteamId),
                    Selected = selected.Contains(a.SteamId),
                    IsMain = _s.ConfigMode == "main" && a.SteamId == _s.ConfigMainId,
                    Pending = Cfg.State.Pending.Any(j => j.Target == a.SteamId),
                    CrosshairText = set.Empty ? "No CS2 config yet" : Cs2Config.CrosshairSummary(set) ?? "Default crosshair",
                    ChangedText = set.Empty ? "Opens with CS2's defaults (or gets the sync)" : changed != null ? "Changed in game " + Fmt.Ago(changed) : "Not changed in game since the last copy",
                    CrosshairImage = CrosshairImage(xh),
                });
            }
            CfgAccountsList.ItemsSource = _cfgRows;
            _cfgToView = new System.Windows.Data.CollectionViewSource { Source = _cfgRows }.View;
            _cfgToView.Filter = o => ((CfgAccountRow)o).SteamId != (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId;
            CfgToList.ItemsSource = _cfgToView;
            CfgAccountsEmpty.Visibility = _cfgRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // No accounts: nothing to copy to or save, so the buttons don't look like they'd do something.
            var any = _cfgRows.Count > 0;
            CfgCopyBtn.IsEnabled = CfgToAllBtn.IsEnabled = CfgToNoneBtn.IsEnabled = CfgSnapSaveBtn.IsEnabled = any;
            CfgCopyHint.Text = any ? "Copies what is ticked in Sync > What to copy. Each account gets a backup first." : "No accounts to copy to yet.";

            CfgSnapList.ItemsSource = snaps.Select(s => new CfgSnapshotRow(s)).ToList();
            CfgSnapEmpty.Visibility = snaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var backups = Cfg.Backups().Take(60).Select(b => new CfgBackupRow(b, CfgName(b.SteamId))).ToList();
            CfgBackupList.ItemsSource = backups;
            CfgBackupEmpty.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CfgUndoBtn.IsEnabled = Cfg.CanUndo;   // nothing to put back: greyed out like Sync now
            CfgResult.Text = Cfg.State.LastResult ?? (_s.ConfigMode == "off" ? "Sync is off. To copy once, use Copy." : "No sync yet.");
        }
        catch (Exception ex) { Log.Warn("Configs page: " + Cs2ConfigStore.Why(ex)); }
        finally { _cfgLoading = false; }
        UpdateCompare();
    }

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
        if (_s.ConfigMode == "off") Cfg.DropQueued(_ => true);   // Sync off: the copies waiting for CS2 to close go too
        if (_s.ConfigMode == "main" && _s.ConfigMainId == null) _s.ConfigMainId = (CfgMainBox.SelectedItem as CfgSourceItem)?.SteamId ?? _accounts.FirstOrDefault(a => a.Active)?.SteamId;
        SaveSettings();
        Log.Info("Config sync: " + _s.ConfigMode switch
        {
            "main" => _s.ConfigMainId == null || _accounts.All(a => a.SteamId != _s.ConfigMainId) ? "from a main account (pick one below)." : $"from the main account ({CfgName(_s.ConfigMainId)}).",
            "previous" => "from the previous account.",
            "latest" => "from the account changed last.", _ => "off.",
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
        if (_s.ConfigMode == "off") { Toast("Pick a mode under Sync first, or use Copy.", false); return; }
        if (_s.ConfigMode == "main" && _s.ConfigMainId == null) { Toast("Pick the main account first.", false); return; }
        var jobs = CfgPlan("manual", null, _accounts.FirstOrDefault(a => a.Active)?.SteamId ?? PreviousAccount(null));
        if (jobs.Count == 0) { Toast(_s.ConfigMode == "latest" ? "No account has a config changed in game yet." : "No account to copy to: turn Sync on for some.", false); return; }
        RunCfgJobs(jobs, "manual", true);
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
                Toast($"Undone on {Fmt.Count(n, "account")}. {names} stayed as they were: restore them from Backups.", n == 0 ? Tone.Error : Tone.Warn);
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

    // ------------------------------------------------------------ Copy

    void CfgToAll_Click(object sender, RoutedEventArgs e)
    {
        var from = (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId;
        foreach (var r in _cfgRows) r.Selected = r.SteamId != from;
    }

    void CfgToNone_Click(object sender, RoutedEventArgs e) { foreach (var r in _cfgRows) r.Selected = false; }

    void CfgFrom_Changed(object sender, SelectionChangedEventArgs e)
    {
        var from = (CfgFromBox.SelectedItem as CfgSourceItem)?.SteamId;
        if (from != null) foreach (var r in _cfgRows) if (r.SteamId == from) r.Selected = false;   // a hidden tick must not stay on
        _cfgToView?.Refresh();
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
        if (_s.ConfigCategories.Count == 0) { Toast("Tick something in Sync > What to copy.", false); return; }
        var jobs = targets.Select(t => new CfgJob { SourceId = from.SteamId, Snapshot = from.Snapshot, Target = t.SteamId, Categories = _s.ConfigCategories.ToList(), At = DateTime.Now }).ToList();
        RunCfgJobs(jobs, "copy", true);
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
        _s.ConfigMode = "main";
        _s.ConfigOptOut.Remove(a.SteamId);
        SaveSettings();
        Log.Info($"Config sync: from the main account ({a.Name}).");
        Toast($"{a.Name} is the main config now. New accounts and logins through the app get it (Configs page).", true);
        if (PageConfigs.IsVisible) ShowConfigs();
    }

    // ------------------------------------------------------------ Snapshots

    async void CfgSnapSave_Click(object sender, RoutedEventArgs e)
    {
        if (CfgSnapFrom.SelectedItem is not CfgSourceItem from || from.SteamId == null) { Toast("Pick the account to save.", false); return; }
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
        CfgCmpImgA.Source = CrosshairImage(Cs2Config.ReadCrosshair(sa));
        CfgCmpImgB.Source = CrosshairImage(Cs2Config.ReadCrosshair(sb));
        CfgCmpXhA.Text = sa == null ? "" : sa.Empty ? "No CS2 config" : Cs2Config.CrosshairSummary(sa) ?? "Default crosshair";
        CfgCmpXhB.Text = sb == null ? "" : sb.Empty ? "No CS2 config" : Cs2Config.CrosshairSummary(sb) ?? "Default crosshair";
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

    /// <summary>Roughly how CS2 draws it at 1080p (length = size × 2 px, gap + 4 px from the center), doubled, on a 32 px square.</summary>
    static ImageSource CrosshairImage(Crosshair c)
    {
        if (c == null) return null;
        const double mid = 16;
        var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 32, 32)) };
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 32, 32));
            var fill = new SolidColorBrush(Color.FromArgb(Math.Max(c.Alpha, (byte)70), c.R, c.G, c.B));
            // Drawn at twice the size so it can be seen in a 32 px box.
            double len, thick, gap;
            if (c.Pixels)
            {
                // The newer crosshair (style 7): pixels at the screen height it was set for, so first brought to 1080p.
                var k = 1080 / Math.Max(c.ScreenHeight ?? 1080, 1) * 2;
                len = Math.Clamp((c.Size ?? 8) * k, 0, 14);
                thick = Math.Clamp(Math.Round((c.Thickness ?? 2) * k), 1, 6);
                gap = Math.Clamp((c.Gap ?? 4) * k, 0, 12);
            }
            else
            {
                len = Math.Clamp((c.Size ?? 5) * 4, 0, 14);
                thick = Math.Clamp(Math.Round((c.Thickness ?? 0.5) * 4), 1, 6);
                gap = Math.Clamp(((c.Gap ?? 1) + 4) * 2, 0, 12);
            }
            var rects = new List<Rect>();
            if (len > 0)
            {
                rects.Add(new Rect(mid + gap, mid - thick / 2, len, thick));
                rects.Add(new Rect(mid - gap - len, mid - thick / 2, len, thick));
                rects.Add(new Rect(mid - thick / 2, mid + gap, thick, len));
                if (c.TStyle != true) rects.Add(new Rect(mid - thick / 2, mid - gap - len, thick, len));
            }
            if (c.Dot == true) rects.Add(new Rect(mid - thick / 2, mid - thick / 2, thick, thick));
            if (c.Outline == true)
            {
                var o = Math.Clamp(c.OutlineThickness ?? 1, 0.5, 3);
                foreach (var r in rects) { var x = r; x.Inflate(o, o); dc.DrawRectangle(Brushes.Black, null, x); }
            }
            foreach (var r in rects) dc.DrawRectangle(fill, null, r);
        }
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }
}

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
    public string CrosshairText { get; init; }
    public string ChangedText { get; init; }
    public ImageSource CrosshairImage { get; init; }
    public bool HasCrosshair => CrosshairImage != null;
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
        var parts = s.Parts.Select(p => p switch { "Keys" => "binds", "Convars" => "game and crosshair", "Video" => "video", "Machine" => "HUD and audio", _ => p });
        Detail = $"{Fmt.When(s.Created)} · {string.Join(", ", parts)}" + (s.From == "imported" ? " · imported" : "");
    }
    public string Name { get; }
    public string Detail { get; }
}

public sealed class CfgBackupRow
{
    public CfgBackupRow(CfgBackup b, string account)
    {
        Backup = b;
        Account = account;
        When = Fmt.When(b.At);
        Detail = MainWindow.CfgReasonText(b.Reason) + (b.From != null ? $" · from {b.From}" : "") + (b.Files.Count > 0 ? " · " + N(b.Files.Count, "file") : " · " + N(b.Created.Count, "new file"));
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
