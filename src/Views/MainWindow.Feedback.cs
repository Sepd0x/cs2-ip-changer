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
    void Toast(string text, bool ok)
    {
        ToastText.Text = text;
        ToastIcon.Text = ok ? "" : "";
        ToastIcon.Foreground = ok ? Ui.Good : Ui.Warn;
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

    void ShowProgress(string text) { ProgressText.Text = text; ProgressPanel.Visibility = Visibility.Visible; }
    void HideProgress() => ProgressPanel.Visibility = Visibility.Collapsed;

    TaskCompletionSource<bool> _dialogTcs;

    Task<bool> Ask(string title, string text, string ok, string cancel = "Cancel",
 int inputs = 0, bool danger = false, string label1 = null, string label2 = null)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();
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
        if (inputs > 0) DialogInput.Focus(); else DialogOk.Focus();
        return _dialogTcs.Task;
    }

    void DialogOk_Click(object sender, RoutedEventArgs e) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(true); }
    void DialogCancel_Click(object sender, RoutedEventArgs e) { Dialog.Visibility = Visibility.Collapsed; _dialogTcs?.TrySetResult(false); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Dialog.Visibility == Visibility.Visible && e.Key == Key.Escape) { DialogCancel_Click(this, null); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
