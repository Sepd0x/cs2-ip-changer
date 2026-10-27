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
            using var cts = new CancellationTokenSource(20000);
            var r = await Updater.Latest(cts.Token);
            _s.LastUpdateCheck = DateTime.Now;
            var wasKnown = _s.UpdateLatest;
            _s.UpdateLatest = r?.Version;
            SaveSettings();
            if (r != null && Updater.IsNewer(r.Version, AppInfo.Version))
            {
                _update = r;
                if (wasKnown != r.Version)
                {
                    Log.Info($"Version {r.Version} is available on GitHub.");
                    Toast($"CS2 IP Changer {r.Version} is available. Click Update under the version number.", Tone.Info);
                }
            }
        }
        catch (Exception ex)
        {
            _updateRetryAt = DateTime.Now.AddHours(1);   // offline or rate-limited: not every tick
            Log.Info("Update check failed: " + ex.Message);
        }
        finally { _checkingUpdate = false; ShowUpdateNotice(); }
    }

    void ShowUpdateNotice()
    {
        var v = _s.UpdateLatest;
        var show = _s.CheckUpdates && Updater.IsNewer(v, AppInfo.Version);
        UpdateBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) UpdateBtn.ToolTip = $"Version {v} is available. Downloads it from GitHub, checks its SHA-256 and restarts the app.";
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
            using (var cts = new CancellationTokenSource(20000)) _update = await Updater.Latest(cts.Token) ?? _update;
            var r = _update;
            if (r == null || !Updater.IsNewer(r.Version, AppInfo.Version))
            {
                _s.UpdateLatest = r?.Version;
                SaveSettings();
                ShowUpdateNotice();
                Toast("You already have the latest version.", true);
                return;
            }
            // Never installs without the release's own hash; the dev build (DLLs next to the exe) can't replace itself either.
            if (r.ExeUrl == null || r.ShaUrl == null || !Updater.CanSelfUpdate)
            {
                OpenUrl(r.PageUrl);
                Toast(r.ExeUrl != null && r.ShaUrl == null ? "This release has no .sha256 file, so the app won't install it. Opened the release page."
                    : "Opened the release page on GitHub.", r.ShaUrl != null);
                return;
            }
            HideProgress();
            if (!await Ask("Update CS2 IP Changer", $"Download version {r.Version} from GitHub, check its SHA-256 and restart the app?\n\nProtection stays on while it restarts. If you're in a server, you'll drop for a few seconds while the new version checks the phone's IP.", "Update")) return;
            ShowProgress("Downloading the update…");
            var sha = await Updater.Download(r, new Progress<string>(t => ProgressText.Text = t));
            Log.Ok($"Update {r.Version} downloaded, SHA-256 matches ({sha[..12]}…). Restarting.");
            ProgressText.Text = "Restarting…";
            Updater.Install(sha, PrepareExitForUpdate);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Update: " + ex.Message);
            Toast("Update failed: " + ex.Message, Tone.Error);
            if (_closingDone) { _closingDone = false; RegisterHotkeys(); }
        }
        finally { HideProgress(); _updating = false; UpdateBtn.IsEnabled = true; }
    }

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
        Log.Ok($"Updated to {AppInfo.Version}.");
        Toast($"Updated to {AppInfo.Version}.", true);
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _s.CheckUpdates = CheckUpdatesBox.IsChecked == true;
        SaveSettings();
        if (_s.CheckUpdates) _ = CheckForUpdates(force: true);
        else ShowUpdateNotice();
    }
}
