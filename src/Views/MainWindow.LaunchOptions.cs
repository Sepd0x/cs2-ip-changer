using System.Windows;
using SRT.Core;

namespace SRT;

// CS2's launch options: -condebug -conclearlog added in Steam, so kicks and bans are seen when Steam opens CS2 too (4 nov.)
public partial class MainWindow
{
    static string SteamUserData => Steam.InstallDir() is string d ? Path.Combine(d, "userdata") : null;

    /// <summary>The Settings button is running: the hook that runs when the app closes Steam leaves it to the button.</summary>
    volatile bool _launchOptsManual;

    /// <summary>At start: the hook for each time the app closes Steam (account switch, Steam restarted through the phone), and a first
    /// pass if Steam is closed now.</summary>
    void InitLaunchOptions()
    {
        if (_demo) return;
        Steam.WhileClosed = () =>
        {
            if (_s.Cs2LaunchOptions && !_launchOptsManual) LogLaunchOptions(ApplyLaunchOptions(), quiet: true);
        };
        AddLaunchOptionsIfSteamClosed();
    }

    /// <summary>With Steam closed, adds them now (in the background). With Steam open, the next time the app closes it.</summary>
    void AddLaunchOptionsIfSteamClosed()
    {
        if (_demo || !_s.Cs2LaunchOptions || !Steam.Closed) return;
        _ = Task.Run(() =>
        {
            var r = ApplyLaunchOptions();
            Dispatcher.BeginInvoke(() => { LogLaunchOptions(r, quiet: true); ShowLaunchOptions(); });
        });
    }

    static List<Cs2LaunchOptions.AccountResult> ApplyLaunchOptions() =>
        Cs2LaunchOptions.ApplyAll(SteamUserData, Paths.File("launch-options"), () => !Steam.Closed);

    /// <summary>One Log line for what changed; quiet = nothing when nothing changed (it runs every time Steam is closed).</summary>
    static void LogLaunchOptions(List<Cs2LaunchOptions.AccountResult> r, bool quiet)
    {
        var added = r.Count(x => x.Changed);
        var failed = r.Where(x => !x.Changed && x.Why != "already there").ToList();
        if (added > 0) Log.Ok($"CS2 launch options: added -condebug -conclearlog on {added} account{(added == 1 ? "" : "s")} (backup in SRT_Dados\\launch-options). Kicks and bans are now seen when Steam opens CS2 too.");
        foreach (var f in failed) Log.Warn($"CS2 launch options: one account was left as it is ({f.Why}).");
        if (!quiet && added == 0 && failed.Count == 0) Log.Info(r.Count == 0 ? "CS2 launch options: no Steam account on this PC has launch options yet." : "CS2 launch options: every account already has -condebug -conclearlog.");
    }

    /// <summary>"3 of 4 accounts have it" under the button.</summary>
    void ShowLaunchOptions()
    {
        if (_demo) { LaunchOptsText.Text = "4 of 4 accounts have it."; return; }
        var c = Cs2LaunchOptions.Count(SteamUserData);
        LaunchOptsText.Text = c is not (int with, int total) ? "" : total == 0 ? "No account has opened Steam on this PC yet."
            : with == total ? $"All {total} account{(total == 1 ? "" : "s")} have it." : $"{with} of {total} accounts have it.";
    }

    async void LaunchOpts_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Demo: launch options added.", true); return; }
        if (SteamUserData == null) { Toast("Steam wasn't found on this PC.", false); return; }
        var reopen = false;
        if (!Steam.Closed)
        {
            if (!await Ask("Close Steam to add them?",
                    "Steam keeps CS2's launch options in memory and writes them back when it closes, so they can only be added while Steam is closed.\n\n"
                    + "The app closes Steam, adds -condebug -conclearlog to CS2 on every account on this PC (what you have there stays, and a backup is kept), then opens Steam again."
                    + (Steam.Cs2Running ? "\n\nCS2 is open: it closes too. If you are in a match, you will drop." : ""),
                    "Close Steam and add", "Not now"))
                return;
            reopen = true;
        }
        LaunchOptsBtn.IsEnabled = false;
        _launchOptsManual = true;
        ShowProgress("Adding -condebug -conclearlog to CS2's launch options…");
        try
        {
            if (reopen)
            {
                _cs2ClosedByAppAt = DateTime.Now;   // closed on purpose: not a crash
                if (!await Steam.Close()) { Toast("Steam didn't close, so nothing was changed.", false); return; }
            }
            var r = await Task.Run(ApplyLaunchOptions);
            LogLaunchOptions(r, quiet: false);
            var added = r.Count(x => x.Changed);
            var left = r.Count(x => !x.Changed && x.Why != "already there");
            Toast(added > 0 ? $"Added on {added} account{(added == 1 ? "" : "s")}. CS2 now writes its console log however it is opened." + (left > 0 ? $" {left} left as they were (see Log)." : "")
                : left > 0 ? "Nothing was added: see the Log for why." : "Every account already has them.", left == 0);
        }
        catch (Exception ex) { Log.Error("CS2 launch options: " + ex.Message); Toast("Couldn't add the launch options: " + ex.Message, Tone.Error); }
        finally
        {
            _launchOptsManual = false;
            HideProgress();
            LaunchOptsBtn.IsEnabled = true;
            if (reopen) try { Steam.Launch(_s.SteamSilent ? "-silent" : ""); } catch (Exception ex) { Toast(ex.Message, Tone.Error); }
            ShowLaunchOptions();
        }
    }
}
