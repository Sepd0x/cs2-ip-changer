using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Commands from hotkeys and buttons: a sound and the activity line at once, the step while it runs, a sound and how it ended
public partial class MainWindow
{
    string _command, _commandStep;
    bool _commandDeclined;
    readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    /// <summary>Runs a command with feedback. The work returns true when it did what was asked.</summary>
    async Task RunCommand(string name, string firstStep, Func<Task<bool>> work)
    {
        await TakeOver(name);
        if (!BeginCommand(name, firstStep)) return;
        var ok = false;
        try { ok = await work(); }
        catch (Exception ex) { Log.Error($"{name}: {ex.Message}"); Toast(ex.Message, Tone.Error); }
        finally { EndCommand(ok); }
    }

    CancellationTokenSource _commandCts;
    /// <summary>More than 0: the running command is only waiting (your answer in the app, CS2's menu, Steam, the server after a crash).</summary>
    int _commandWaiting;

    /// <summary>The running command was stopped by a newer one: it returns without doing anything else.</summary>
    bool CommandCancelled => _commandCts?.IsCancellationRequested == true;

    /// <summary>Marks a wait inside the running command: while it lasts, a new command stops it instead of being refused.</summary>
    IDisposable CommandWait()
    {
        var cts = _commandCts;
        _commandWaiting++;
        ShowCancel();
        return new Scope(() => { if (_commandCts == cts && _commandWaiting > 0) _commandWaiting--; ShowCancel(); });
    }

    public enum CancelKind { None, Now, AfterStep }

    /// <summary>
    /// What Cancel in the top bar does. While a command only waits (your answer, CS2, Steam): it stops now. During an IP change or an
    /// account switch inside New IP + next account or a rejoin: it stops after that step, which always finishes (never cut in half).
    /// </summary>
    public static CancelKind CancelFor(bool command, bool waiting, bool midStep, bool inSwitch, bool cancelled) =>
        !command || cancelled ? CancelKind.None : waiting && !midStep ? CancelKind.Now : midStep && inSwitch ? CancelKind.AfterStep : CancelKind.None;

    /// <summary>Once a stop after the step is asked for, Cancel stays for the waits after it (they stop at once).</summary>
    CancelKind CancelNow => CancelFor(_command != null, _commandWaiting > 0, _changingIp || _switching, _rejoining, CommandCancelled) is var k
        && k == CancelKind.AfterStep && _stopAfterStep ? CancelKind.None : k;

    /// <summary>Cancel was pressed during a step of a switch: it ends after that step (see <see cref="StopAfterStep"/>).</summary>
    bool _stopAfterStep;

    void ShowCancel()
    {
        var kind = CancelNow;
        ActivityCancel.Visibility = kind == CancelKind.None ? Visibility.Collapsed : Visibility.Visible;
        ActivityCancel.ToolTip = kind == CancelKind.AfterStep ? Loc.T("Stops after the step that is running: the IP change or the login finishes first, then nothing else starts.")
            : Loc.T("Stops what the app is waiting for. Nothing that already happened is undone.");
    }

    void ActivityCancel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // the pill itself opens the Log
        var kind = CancelNow;
        if (kind == CancelKind.AfterStep)
        {
            Log.Info($"{Loc.En(_command)}: Cancel pressed while {Loc.En(_commandStep) ?? "running"}; it stops after this step.");
            _stopAfterStep = true;
            _commandDeclined = true;   // ends quietly
            ShowCancel();
            Toast(Loc.T("Stopping after this step: the IP change or login that is running finishes first."), Tone.Info);
            return;
        }
        if (kind != CancelKind.Now) return;
        Log.Info($"{Loc.En(_command)} cancelled while {Loc.En(_commandStep) ?? "waiting"}.");
        _commandDeclined = true;   // ends quietly, like a "no" in a dialog
        _commandCts?.Cancel();
        if (Dialog.Visibility == Visibility.Visible) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(false); }
        ShowCancel();
        Toast(Loc.T("Cancelled. Nothing else was started."), Tone.Info);
    }

    sealed class Scope(Action end) : IDisposable { public void Dispose() => end(); }

    /// <summary>
    /// In game you can't see a question in the app, and every hotkey you pressed got "still busy" while it waited:
    /// a command that is only waiting is stopped, and the new one runs. Changing the IP or the account is never cut in half.
    /// </summary>
    async Task TakeOver(string name)
    {
        if (_command == null || _commandWaiting <= 0 || _changingIp || _switching) return;
        Log.Info($"{Loc.En(_command)} stopped while {Loc.En(_commandStep) ?? "waiting"}: {Loc.En(name)} takes over.");
        _commandCts?.Cancel();
        if (Dialog.Visibility == Visibility.Visible) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(false); }
        for (var i = 0; i < 50 && (_command != null || _rejoining); i++) await Task.Delay(100);
    }

    /// <summary>False (with a sound and the reason) when another command is still running.</summary>
    bool BeginCommand(string name, string step)
    {
        if (_command != null || _changingIp || _switching || _rejoining)
        {
            Sound(Tones.Kind.Busy);
            var what = _command ?? (_changingIp ? Loc.T("Change IP") : Loc.T("the account switch"));
            Toast(_commandStep != null ? Loc.F("Still busy with {0} ({1}). {2} didn't start.", what, _commandStep.TrimEnd('…', '.'), name)
                : Loc.F("Still busy with {0}. {1} didn't start.", what, name), Tone.Warn);
            Log.Info($"{Loc.En(name)} ignored: still busy with {Loc.En(what)}.");
            return false;
        }
        // A question from before (rejoin offer after a kick, follow) answered by this command instead: close it,
        // or clicking it later would change the IP or switch account mid-match.
        if (Dialog.Visibility == Visibility.Visible) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(false); }
        _command = name;
        _commandDeclined = false;
        _stopAfterStep = false;
        _commandCts = new CancellationTokenSource();
        _commandWaiting = 0;
        Sound(Tones.Kind.Start);
        Step(step);
        return true;
    }

    /// <summary>What the running command is doing now (top bar, and the taskbar shows it's busy).</summary>
    void Step(string text)
    {
        if (_command == null) return;
        // "Attempt 1/3: …" reads "Switch · attempt 1/3: …" (but "CS2 …" stays as it is)
        if (text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1])) text = char.ToLowerInvariant(text[0]) + text[1..];
        _commandStep = text;
        _activityTimer.Stop();
        ActivityText.Text = $"{_command} · {text}";
        ActivityText.Foreground = Ui.Text;
        ActivityIcon.Text = "";
        ActivityIcon.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        if (ActivitySpin.HasAnimatedProperties == false)
            ActivitySpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        ActivityPill.Visibility = ActivityLine.Visibility = Visibility.Visible;
        Taskbar().ProgressState = TaskbarItemProgressState.Indeterminate;
    }

    void EndCommand(bool ok)
    {
        var name = _command;
        var declined = !ok && _commandDeclined;
        _command = _commandStep = null;
        ShowCancel();
        ActivitySpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        ActivityLine.Visibility = Visibility.Collapsed;
        if (declined)
        {
            // You said no in a dialog: nothing failed.
            ActivityPill.Visibility = Visibility.Collapsed;
            Taskbar().ProgressState = TaskbarItemProgressState.None;
            return;
        }
        Sound(ok ? Tones.Kind.Done : Tones.Kind.Failed);
        ActivityIcon.Text = ok ? "" : "";
        ActivityIcon.Foreground = ok ? Ui.Good : Ui.Bad;
        ActivityText.Text = ok ? Loc.F("{0} · done", name) : Loc.F("{0} · didn't finish (see Log)", name);
        ActivityText.Foreground = ok ? Ui.Good : Ui.Bad;
        ActivityPill.Visibility = Visibility.Visible;
        Taskbar().ProgressState = ok ? TaskbarItemProgressState.None : TaskbarItemProgressState.Error;
        Taskbar().ProgressValue = 1;
        _activityTimer.Stop();
        _activityTimer.Tick -= ActivityTimer_Tick;
        _activityTimer.Tick += ActivityTimer_Tick;
        _activityTimer.Start();
    }

    void ActivityTimer_Tick(object sender, EventArgs e)
    {
        _activityTimer.Stop();
        if (_command != null) return;
        ActivityPill.Visibility = Visibility.Collapsed;
        Taskbar().ProgressState = TaskbarItemProgressState.None;
    }

    void Activity_Click(object sender, MouseButtonEventArgs e) => NavLog.IsChecked = true;

    TaskbarItemInfo Taskbar() => TaskbarItemInfo ??= new TaskbarItemInfo();

    void Sound(Tones.Kind kind) { if (_s.CommandSounds) Tones.Play(kind); }
}
