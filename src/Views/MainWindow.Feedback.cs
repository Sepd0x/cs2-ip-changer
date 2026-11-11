using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Notifications and dialog
public partial class MainWindow
{
    enum Tone { Ok, Warn, Error, Info }

    void Toast(string text, bool ok) => Toast(text, ok ? Tone.Ok : Tone.Warn);

    void Toast(string text, Tone tone)
    {
        // What the notification said stays in the Log after it fades
        Log.Notice(tone switch { Tone.Ok => LogLevel.Ok, Tone.Error => LogLevel.Error, Tone.Info => LogLevel.Info, _ => LogLevel.Warn }, text);
        // The window is in the notification area: what a tray command or the tick has to say comes as a Windows notification.
        if (!IsVisible && tone != Tone.Info) _tray?.Balloon(tone == Tone.Error ? "CS2 IP Changer: problem" : "CS2 IP Changer", text);
        ToastText.Text = text;
        _toastAction = null;
        ToastActionBtn.Visibility = Visibility.Collapsed;
        ToastIcon.Text = tone switch { Tone.Ok => "", Tone.Error => "", Tone.Info => "", _ => "" };
        ToastIcon.Foreground = tone switch { Tone.Ok => Ui.Good, Tone.Error => Ui.Bad, Tone.Info => Ui.Muted, _ => Ui.Warn };
        // The strip on top: the brand line, red for an error.
        ToastStrip.Background = tone == Tone.Error ? Ui.Bad : (Brush)FindResource("TopLine");
        ToastBox.Visibility = Visibility.Visible;
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(text.Length / 14.0, 3.5, 9));
        _toastTimer.Start();
    }

    Func<Task> _toastAction;

    /// <summary>A toast with one button (beta.11). It stays a little longer, so there is time to press it.</summary>
    void Toast(string text, Tone tone, string action, Func<Task> run)
    {
        Toast(text, tone);
        _toastAction = run;
        ToastActionBtn.Content = action;
        ToastActionBtn.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(12);
        _toastTimer.Start();
    }

    async void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        var run = _toastAction;
        _toastAction = null;
        HideToast();
        if (run != null) await run();
    }

    void HideToast()
    {
        _toastTimer.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        a.Completed += (_, _) => { if (ToastBox.Opacity == 0) ToastBox.Visibility = Visibility.Collapsed; };
        ToastBox.BeginAnimation(OpacityProperty, a);
    }

    static readonly Regex AttemptLine = new(@"^Attempt (\d+)/(\d+): (.+)$");

    // The progress line takes the place of the hero's buttons, so nothing below moves.
    // An IP change ("Attempt 1/3: …") also turns the Status title into "Changing IP… attempt 1/3", so the line under it stays short.
    void ShowProgress(string text)
    {
        var m = AttemptLine.Match(text ?? "");
        if (m.Success)
        {
            var title = $"Changing IP… attempt {m.Groups[1].Value}/{m.Groups[2].Value}";
            var rest = m.Groups[3].Value;
            ProgressText.Text = char.ToUpperInvariant(rest[0]) + rest[1..];
            // The Mobile data check follows the change: off for N s, then coming back.
            _changingData = rest.Contains(" off ", StringComparison.OrdinalIgnoreCase) ? $"Off for {_s.OffSeconds} s" : "Coming back on…";
            if (title != _changingTitle)
            {
                _changingTitle = title;
                SetHero(_heroWanted.Kind, _heroWanted.Title, _heroWanted.Sub);
                ShowIps();
            }
            UpdateDataCheck();
        }
        else ProgressText.Text = text;
        ProgressPanel.Visibility = Visibility.Visible;
        ActionRow.Visibility = Visibility.Hidden;
        Step(text);
    }

    void HideProgress()
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ActionRow.Visibility = Visibility.Visible;
        if (_changingTitle == null) return;
        _changingTitle = null;
        _changingData = null;
        SetHero(_heroWanted.Kind, _heroWanted.Title, _heroWanted.Sub);
        ShowIps();
        UpdateDataCheck();
    }

    /// <summary>What the Mobile data check says while an IP change runs (null otherwise).</summary>
    string _changingData;

    TaskCompletionSource<bool> _dialogTcs;

    /// <param name="choices">Options to pick one of, each "title\nwhat it means" (Remove: keep settings or delete everything); the first is
    /// picked at the start, the answer is in <see cref="_dialogChoice"/>.</param>
    Task<bool> Ask(string title, string text, string ok, string cancel = "Cancel",
 int inputs = 0, bool danger = false, string label1 = null, string label2 = null, string color = null, string[] choices = null)
    {
        // A command stopped with Cancel asks nothing more: its next question counts as a "no"
        if (_command != null && CommandCancelled) { _dialogCancelClicked = false; return Task.FromResult(false); }
        if (!IsVisible && _tray != null) ShowFromTray();   // a question from a tray command needs the window
        _dialogColor = color;
        DialogColorLabel.Visibility = DialogColors.Visibility = color != null ? Visibility.Visible : Visibility.Collapsed;
        if (color != null) BuildColorSwatches();
        BuildChoices(choices);
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();
        _dialogCancelClicked = false;
        if (ToastBox.Visibility == Visibility.Visible) HideToast();   // the dialog says it already
        if (_command != null) { Sound(Tones.Kind.Ask); Step("waiting for your answer in the app"); }   // in game only the sound gets through
        DialogTitle.Text = title;
        DialogText.Text = text;
        DialogOk.Content = ok;
        DialogOk.Style = (Style)FindResource(danger ? "DangerStrong" : "Primary");
        DialogLabel1.Text = label1;
        DialogLabel2.Text = label2;
        DialogLabel1.Visibility = inputs >= 1 && label1 != null ? Visibility.Visible : Visibility.Collapsed;
        DialogLabel2.Visibility = inputs >= 2 && label2 != null ? Visibility.Visible : Visibility.Collapsed;
        DialogCancel.Content = cancel ?? "";
        DialogCancel.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
        DialogInput.Visibility = inputs >= 1 ? Visibility.Visible : Visibility.Collapsed;
        DialogInput2.Visibility = inputs >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Dialog.Visibility = Visibility.Visible;
        if (_command != null)
        {
            var wait = CommandWait();
            _dialogTcs.Task.ContinueWith(_ => Dispatcher.BeginInvoke(wait.Dispose));
        }
        // After layout: a control that was collapsed a moment ago can't take focus yet.
        Dispatcher.BeginInvoke(() => { if (inputs > 0) Keyboard.Focus(DialogInput); else Keyboard.Focus(DialogOk); }, DispatcherPriority.Input);
        return _dialogTcs.Task;
    }

    /// <summary>
    /// The last dialog's second button was clicked. False after Escape or when another dialog took its place, so a question
    /// whose two buttons both act (closing the app) can tell "no answer" from its second answer.
    /// </summary>
    bool _dialogCancelClicked;

    /// <summary>The color picked in the dialog (flags).</summary>
    string _dialogColor;

    /// <summary>The option picked in the dialog (0 = the first).</summary>
    int _dialogChoice;

    void BuildChoices(string[] choices)
    {
        _dialogChoice = 0;
        DialogChoices.Children.Clear();
        for (var i = 0; i < (choices?.Length ?? 0); i++)
        {
            var parts = choices[i].Split('\n', 2);
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = parts[0], Foreground = (Brush)FindResource("Text") });
            if (parts.Length > 1) content.Children.Add(new TextBlock { Text = parts[1], Style = (Style)FindResource("OptSub") });
            var n = i;
            var rb = new RadioButton { Style = (Style)FindResource("Choice"), GroupName = "dialogchoice", Content = content, IsChecked = i == 0 };
            rb.Checked += (_, _) => _dialogChoice = n;
            DialogChoices.Children.Add(rb);
        }
        DialogChoices.Visibility = choices?.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void BuildColorSwatches()
    {
        DialogColors.Children.Clear();
        foreach (var c in PlayerFlag.Colors)
        {
            var b = new RadioButton
            {
                GroupName = "flagcolor", IsChecked = string.Equals(c, _dialogColor, StringComparison.OrdinalIgnoreCase), Tag = c, Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                ToolTip = c, FocusVisualStyle = (Style)FindResource("FocusRing"),
                Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='RadioButton'>" +
                    "<Border x:Name='o' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' BorderThickness='2' BorderBrush='Transparent' Padding='3'><Border Background='{TemplateBinding Background}'/></Border>" +
                    "<ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='o' Property='BorderBrush' Value='#DDDDDD'/></Trigger></ControlTemplate.Triggers></ControlTemplate>"),
                Background = PlayerFlag.ToBrush(c),
            };
            b.Checked += (_, _) => _dialogColor = c;
            DialogColors.Children.Add(b);
        }
    }

    void DialogOk_Click(object sender, RoutedEventArgs e) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(true); }
    void DialogCancel_Click(object sender, RoutedEventArgs e) { _dialogCancelClicked = sender == DialogCancel; Dialog.Visibility = Visibility.Collapsed; _commandDeclined |= _command != null; _dialogTcs?.TrySetResult(false); }

    /// <summary>The X: closes the question without picking either button, like Esc (a full server: neither wait nor join).</summary>
    void DialogClose_Click(object sender, RoutedEventArgs e) => DialogCancel_Click(this, null);

    /// <summary>A click on the dark area around the box closes it the same way.</summary>
    void DialogBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != Dialog) return;
        e.Handled = true;
        DialogCancel_Click(this, null);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Dialog.Visibility != Visibility.Visible && TourKey(e.Key)) e.Handled = true;
        else if (Dialog.Visibility == Visibility.Visible && e.Key == Key.Escape) { DialogCancel_Click(this, null); e.Handled = true; }
        else if (Dialog.Visibility == Visibility.Visible && e.Key == Key.Enter && Keyboard.FocusedElement != DialogCancel) { DialogOk_Click(this, null); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
