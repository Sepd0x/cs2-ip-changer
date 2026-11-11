using System.Windows;
using SRT.Core;

namespace SRT;

// CS2's launch options: -condebug -conclearlog added in Steam, so kicks and bans are seen when Steam opens CS2 too (4 nov.)
public partial class MainWindow
{
    static string SteamUserData => Steam.InstallDir() is string d ? Path.Combine(d, "userdata") : null;

    /// <summary>The Settings button is running: the hook that runs when the app closes Steam leaves it to the button.</summary>
    volatile bool _launchOptsManual;

    /// <summary>At start: a first pass if Steam is closed now.</summary>
    void InitLaunchOptions()
    {
        if (_demo) return;
        AddLaunchOptionsIfSteamClosed();
    }

    /// <summary>
    /// Passed to each Steam close the app makes (account switch, Steam restarted through the phone, adding an account): the launch
    /// options go in while Steam is closed. Null in the demo.
    /// </summary>
    Action WhileSteamClosed => _demo ? null : () =>
    {
        if (_s.Cs2LaunchOptions && !_launchOptsManual) LogLaunchOptions(ApplyLaunchOptions(), quiet: true);
    };

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
        var failed = r.Where(x => x.Left).ToList();
        if (added > 0) Log.Ok(AddedText(added));
        foreach (var f in failed) Log.Warn(LeftText(f.Why));
        if (!quiet && added == 0 && failed.Count == 0) Log.Info(r.Count == 0 ? "CS2 launch options: no Steam account on this PC has launch options yet." : "CS2 launch options: every account already has -condebug -conclearlog.");
    }

    /// <summary>
    /// The Log line for the accounts that got the options. They are Steam's userdata folders, one for every account that ever logged in
    /// on this PC, so the number can be more than the accounts Steam lists now (7 nov.: "33 Steam accounts on this PC", then "added ...
    /// on 35 accounts" and one left): the line says which accounts it means.
    /// </summary>
    public static string AddedText(int added) =>
        $"CS2 launch options: added -condebug -conclearlog for {Fmt.Count(added, "Steam account")} that logged in on this PC at some point, "
        + "including any no longer in Steam's list (backup in SRT_Dados\\launch-options). Kicks and bans are now seen when Steam opens CS2 too.";

    /// <summary>
    /// The Log line for an account left as it was. The reason's own ", so it was left as it is" goes, so the line doesn't say it twice
    /// (diagnostics of 7 nov.). Patch keeps its words: other places and tests read them.
    /// </summary>
    public static string LeftText(string why) =>
        $"CS2 launch options: one account was left as it is ({(why ?? "").Replace(", so it was left as it is", "", StringComparison.Ordinal)}).";

    /// <summary>"3 of 4 accounts have it" under the button.</summary>
    void ShowLaunchOptions()
    {
        if (_demo) { LaunchOptsText.Text = Loc.F("{0} of {1} accounts have it.", 4, 4); return; }
        var c = Cs2LaunchOptions.Count(SteamUserData);
        LaunchOptsText.Text = c is not (int with, int total) ? "" : total == 0 ? Loc.T("No account has opened Steam on this PC yet.")
            : with == total ? Loc.F("All {0} have it.", Fmt.Count(total, "account")) : Loc.F("{0} of {1} accounts have it.", with, total);
    }

    async void LaunchOpts_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast(Loc.T("Demo: launch options added."), true); return; }
        if (SteamUserData == null) { Toast(Loc.T("Steam wasn't found on this PC."), false); return; }
        var reopen = false;
        if (!Steam.Closed)
        {
            if (!await Ask(Loc.T("Close Steam to add them?"),
                    Loc.T("Steam keeps CS2's launch options in memory and writes them back when it closes, so they can only be added while Steam is closed.") + "\n\n"
                    + Loc.T("The app closes Steam, adds -condebug -conclearlog to CS2 on every account on this PC (what you have there stays, and a backup is kept), then opens Steam again.")
                    + (Steam.Cs2Running ? "\n\n" + Loc.T("CS2 is open: it closes too. If you are in a match, you will drop.") : ""),
                    Loc.T("Close Steam and add"), Loc.T("Not now")))
                return;
            reopen = true;
        }
        LaunchOptsBtn.IsEnabled = false;
        _launchOptsManual = true;
        ShowProgress(Loc.T("Adding -condebug -conclearlog to CS2's launch options…"));
        try
        {
            if (reopen)
            {
                _cs2.Closing("the launch options change");   // Steam takes CS2 along: not a crash
                if (!await Steam.Close()) { Toast(Loc.T("Steam didn't close, so nothing was changed."), false); return; }
            }
            var r = await Task.Run(ApplyLaunchOptions);
            LogLaunchOptions(r, quiet: false);
            var added = r.Count(x => x.Changed);
            var left = r.Count(x => x.Left);
            Toast(added > 0 ? Loc.F("Added on {0}. CS2 now writes its console log however it is opened.", Fmt.Count(added, "account")) + (left > 0 ? " " + Loc.F("{0} left as they were (see Log).", left) : "")
                : left > 0 ? Loc.T("Nothing was added: see the Log for why.") : Loc.T("Every account already has them."), left == 0);
        }
        catch (Exception ex) { Log.Error("CS2 launch options: " + Loc.En(ex.Message)); Toast(Loc.T("Couldn't add the launch options: ") + ex.Message, Tone.Error); }
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
