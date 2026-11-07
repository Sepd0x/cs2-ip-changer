using System.Diagnostics;
using System.Windows;
using SRT.Core;

namespace SRT;

// Update check (GitHub releases, once a day) and the update itself
public partial class MainWindow
{
    Updater.Release _update;
    bool _checkingUpdate, _updating, _justUpdated;
    DateTime _updateRetryAt;

    async Task CheckForUpdates(bool force = false)
    {
        if (_demo || _checkingUpdate || !_s.CheckUpdates) { ShowUpdateNotice(); return; }
        if (!force && ((_s.LastUpdateCheck is DateTime t && (DateTime.Now - t).TotalHours < 24) || DateTime.Now < _updateRetryAt)) { ShowUpdateNotice(); return; }
        _checkingUpdate = true;
        try
        {
            Updater.Release r;
            try
            {
                using var cts = new CancellationTokenSource(20000);
                r = await Updater.Latest(_s.TestVersions, cts.Token);
            }
            catch (Exception ex)
            {
                _updateRetryAt = DateTime.Now.AddHours(1);   // offline or rate-limited: not every tick
                Log.Info("Update check failed: " + ex.Message);
                return;
            }
            _s.LastUpdateCheck = DateTime.Now;
            var wasKnown = _s.UpdateLatest;
            _s.UpdateLatest = r?.Version;
            _s.UpdateLatestTest = r?.Test == true;
            // The check itself worked: a settings file that can't be written is its own Log line, and the new version is still said
            try { SaveSettings(); }
            catch (Exception ex) { Log.Warn("Couldn't save the settings after the update check: " + ex.Message); }
            if (r != null && Updater.IsNewer(r.Version, AppInfo.Version))
            {
                _update = r;
                if (wasKnown != r.Version)
                {
                    Log.Info($"{(r.Test ? "Test version" : "Version")} {r.Version} is available on GitHub.");
                    Telemetry.Count("upd_seen");
                    Toast(UpdateToast(r.Version, r.Test), Tone.Info);
                }
            }
        }
        finally { _checkingUpdate = false; ShowUpdateNotice(); }
    }

    /// <summary>The toast when the check finds a newer version; a test version (pre-release) says so.</summary>
    public static string UpdateToast(string v, bool test) => test
        ? $"Test version {v} is out. Click Update under the version number."
        : $"CS2 IP Changer {v} is available. Click Update under the version number.";

    /// <summary>The Update button's tooltip.</summary>
    public static string UpdateTip(string v, bool test) =>
        $"{(test ? "Test version" : "Version")} {v} is available. Downloads it from GitHub, checks its SHA-256 and restarts the app.";

    /// <summary>The version in the Update question: "test version 0.1.0-beta.12" or "version 0.1.0-beta.12".</summary>
    public static string UpdateWhat(string v, bool test) => (test ? "test version " : "version ") + v;

    void ShowUpdateNotice()
    {
        var v = _s.UpdateLatest;
        var show = _s.CheckUpdates && Updater.IsNewer(v, AppInfo.Version);
        UpdateBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) UpdateBtn.ToolTip = UpdateTip(v, _s.UpdateLatestTest);
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (_demo) { Toast("Demo: update.", true); return; }
        _updating = true;
        UpdateBtn.IsEnabled = false;
        try
        {
            ShowProgress("Checking GitHub for the latest version…");
            using (var cts = new CancellationTokenSource(20000)) _update = await Updater.Latest(_s.TestVersions, cts.Token) ?? _update;
            var r = _update;
            if (r == null || !Updater.IsNewer(r.Version, AppInfo.Version))
            {
                _s.UpdateLatest = r?.Version;
                _s.UpdateLatestTest = r?.Test == true;
                SaveSettings();
                ShowUpdateNotice();
                Toast("You already have the latest version.", true);
                return;
            }
            // Never installs without the release's own hash; the dev build (DLLs next to the exe) can't replace itself either.
            if (NoInstallReason(r, Updater.CanSelfUpdate) is string why)
            {
                OpenUrl(r.PageUrl);
                Log.Warn($"Update {r.Version}: {why}");
                Toast(why, false);
                return;
            }
            HideProgress();
            var size = r.Size > 0 ? $" (about {Math.Max(1, (r.Size + 500_000) / 1_000_000)} MB)" : "";
            if (!await Ask("Update CS2 IP Changer", $"Download {UpdateWhat(r.Version, r.Test)}{size} from GitHub, check its SHA-256 and restart the app?{MobileDataNote()}\n\n" + (!_protOn ? "Protection stays off, as now." : "Protection stays on while it restarts. If you are on a server, you will be disconnected for a few seconds while the new version checks the phone's IP."), "Update")) { Telemetry.Count("upd_declined"); return; }
            Telemetry.Count("upd_accepted");
            ShowProgress("Downloading the update…");
            var sha = await Updater.Download(r, new Progress<string>(t => ProgressText.Text = t));
            Log.Ok($"Update {r.Version} downloaded, SHA-256 matches ({sha[..12]}…). Restarting.");
            ProgressText.Text = "Restarting…";
            Telemetry.BeforeRestart();   // beta.13: the stats go and are saved before the new copy reads them (they arrived twice)
            Updater.Install(sha, PrepareExitForUpdate, AppIds.Args.UpdateState(_protOn));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Telemetry.AfterFailedRestart();   // beta.13: the install failed after BeforeRestart, so this copy counts again
            Log.Error("Update: " + ex.Message);
            Telemetry.Count("upd_failed");
            Toast("Update failed: " + ex.Message, Tone.Error);
            if (_closingDone) { _closingDone = false; RegisterHotkeys(); App.ReacquireSingleInstance(); }
        }
        finally { HideProgress(); _updating = false; UpdateBtn.IsEnabled = true; }
    }

    /// <summary>Why Update opens the release page instead of installing (null: it installs). Each case its own reason.</summary>
    public static string NoInstallReason(Updater.Release r, bool canSelfUpdate) =>
        r.ExeUrl == null ? $"This release has no {AppIds.ExeName}. Opened the release page."
        : r.ShaUrl == null ? "This release has no .sha256 file, so the app won't install it. Opened the release page."
        : r.SigUrl == null ? "This release isn't signed with the project's key, so the app won't install it. Opened the release page."
        : !canSelfUpdate ? "This copy of the app can't replace itself (a development or recording build). Opened the release page."
        : null;

    /// <summary>For a download question: a warning when the rest of the PC goes out through the phone right now.</summary>
    string MobileDataNote() => _last?.PcDefaultIsEgress == true ? "\n\nThis download goes through the phone's mobile data." : "";

    /// <summary>Frees the hotkeys and the single-instance lock for the new copy. The rules stay: protection doesn't drop.</summary>
    void PrepareExitForUpdate()
    {
        _closingDone = true;
        UnregisterHotkeys();
        App.ReleaseSingleInstance();
    }

    void AfterUpdateStart()
    {
        _ = Updater.CleanupOld();
        if (!_justUpdated) return;
        Telemetry.Count("upd_done");
        Log.Ok($"Updated to {AppInfo.Version}.");
        Toast($"Updated to {AppInfo.Version}.", true);
    }

    /// <summary>Opens a web page in the browser as the signed-in user (a browser started by the app would have admin rights).</summary>
    static void OpenUrl(string url)
    {
        if (ShellLaunch.OpenAsUser(url, "", null) == null) return;
        // No fallback that starts the browser from here: it would run as admin. The link goes to the clipboard instead.
        try { Clipboard.SetText(url); } catch { }
    }

    void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _s.CheckUpdates = CheckUpdatesBox.IsChecked == true;
        SaveSettings();
        if (_s.CheckUpdates) _ = CheckForUpdates(force: true);
        else ShowUpdateNotice();
    }

    /// <summary>Settings > App > Get test versions. Turned off, a test version already seen is no longer offered.</summary>
    void TestVersions_Click(object sender, RoutedEventArgs e)
    {
        _s.TestVersions = TestVersionsBox.IsChecked == true;
        if (!_s.TestVersions && _s.UpdateLatestTest) { _s.UpdateLatest = null; _s.UpdateLatestTest = false; _update = null; }
        SaveSettings();
        if (_s.CheckUpdates) _ = CheckForUpdates(force: true);
        else ShowUpdateNotice();
    }
}
