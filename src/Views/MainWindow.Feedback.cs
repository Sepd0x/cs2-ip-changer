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
        ToastText.Text = text;
        ToastIcon.Text = tone switch { Tone.Ok => "", Tone.Error => "", Tone.Info => "", _ => "" };
        ToastIcon.Foreground = tone switch { Tone.Ok => Ui.Good, Tone.Error => Ui.Bad, Tone.Info => Ui.Muted, _ => Ui.Warn };
        ToastBox.Visibility = Visibility.Visible;
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(text.Length / 14.0, 3.5, 9));
        _toastTimer.Start();
    }

    void HideToast()
    {
        _toastTimer.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        a.Completed += (_, _) => { if (ToastBox.Opacity == 0) ToastBox.Visibility = Visibility.Collapsed; };
        ToastBox.BeginAnimation(OpacityProperty, a);
    }

    // The progress line takes the place of the hero's buttons, so nothing below moves.
    void ShowProgress(string text) { ProgressText.Text = text; ProgressPanel.Visibility = Visibility.Visible; ActionRow.Visibility = Visibility.Hidden; }
    void HideProgress() { ProgressPanel.Visibility = Visibility.Collapsed; ActionRow.Visibility = Visibility.Visible; }

    TaskCompletionSource<bool> _dialogTcs;

    Task<bool> Ask(string title, string text, string ok, string cancel = "Cancel",
 int inputs = 0, bool danger = false, string label1 = null, string label2 = null)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();
        if (ToastBox.Visibility == Visibility.Visible) HideToast();   // the dialog says it already
        DialogTitle.Text = title;
        DialogText.Text = text;
        DialogOk.Content = ok;
        DialogOk.Style = (Style)FindResource(danger ? "Danger" : "Primary");
        DialogLabel1.Text = label1;
        DialogLabel2.Text = label2;
        DialogLabel1.Visibility = inputs >= 1 && label1 != null ? Visibility.Visible : Visibility.Collapsed;
        DialogLabel2.Visibility = inputs >= 2 && label2 != null ? Visibility.Visible : Visibility.Collapsed;
        DialogCancel.Content = cancel ?? "";
        DialogCancel.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
        DialogInput.Visibility = inputs >= 1 ? Visibility.Visible : Visibility.Collapsed;
        DialogInput2.Visibility = inputs >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Dialog.Visibility = Visibility.Visible;
        // After layout: a control that was collapsed a moment ago can't take focus yet.
        Dispatcher.BeginInvoke(() => { if (inputs > 0) Keyboard.Focus(DialogInput); else Keyboard.Focus(DialogOk); }, DispatcherPriority.Input);
        return _dialogTcs.Task;
    }

    void DialogOk_Click(object sender, RoutedEventArgs e) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(true); }
    void DialogCancel_Click(object sender, RoutedEventArgs e) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(false); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Dialog.Visibility == Visibility.Visible && e.Key == Key.Escape) { DialogCancel_Click(this, null); e.Handled = true; }
        else if (Dialog.Visibility == Visibility.Visible && e.Key == Key.Enter && Keyboard.FocusedElement != DialogCancel) { DialogOk_Click(this, null); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
