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
        if (!BeginCommand(name, firstStep)) return;
        var ok = false;
        try { ok = await work(); }
        catch (Exception ex) { Log.Error($"{name}: {ex.Message}"); Toast(ex.Message, Tone.Error); }
        finally { EndCommand(ok); }
    }

    /// <summary>False (with a sound and the reason) when another command is still running.</summary>
    bool BeginCommand(string name, string step)
    {
        if (_command != null || _changingIp || _switching || _rejoining)
        {
            Sound(Tones.Kind.Busy);
            var what = _command ?? (_changingIp ? "Change IP" : "the account switch");
            Toast($"Still busy with {what}" + (_commandStep != null ? $" ({_commandStep.TrimEnd('…', '.')})" : "") + $". {name} didn't start.", Tone.Warn);
            Log.Info($"{name} ignored: still busy with {what}.");
            return false;
        }
        _command = name;
        _commandDeclined = false;
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
        ActivityText.Text = ok ? $"{name} · done" : $"{name} · didn't finish (see Log)";
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
