using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SRT;

/// <summary>
/// A message in the app's own style, for the moments without the main window: the app is already running, --restore's result,
/// a data folder it can't use, and Install. With <paramref name="action"/>, the main button runs it (the window stays open, with a
/// status line) instead of closing.
/// </summary>
public partial class NoticeWindow : Window
{
    Func<NoticeWindow, Task> _action;

    /// <summary>The main button was clicked (an action that fails sets it back to false).</summary>
    public bool Accepted { get; set; }

    bool _working;

    // While an action runs (Install copying), Alt+F4 doesn't close the window and cut it short
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_working) e.Cancel = true;
        base.OnClosing(e);
    }

    public NoticeWindow(string title, string text, string ok = "OK", string other = null, bool problem = false, Func<NoticeWindow, Task> action = null)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        Body.Text = text;
        OkBtn.Content = ok;
        OtherBtn.Content = other ?? "";
        OtherBtn.Visibility = other == null ? Visibility.Collapsed : Visibility.Visible;
        OkBtn.IsCancel = other == null;   // with one button, Escape closes too
        if (problem) Heading.Foreground = (Brush)FindResource("Bad");
        _action = action;
    }

    /// <summary>Shows it on its own (no main window yet) and waits; true when the main button was clicked.</summary>
    public static bool Ask(string title, string text, string ok = "OK", string other = null, bool problem = false) =>
        new NoticeWindow(title, text, ok, other, problem).ShowAndWait();

    public bool ShowAndWait()
    {
        ShowDialog();
        return Accepted;
    }

    /// <summary>The action is done and can't run again: one Close button is left.</summary>
    public void Finish()
    {
        _action = null;
        OkBtn.Content = "Close";
        OtherBtn.Visibility = Visibility.Collapsed;
    }

    public void Say(string text, bool bad = false)
    {
        Status.Text = text;
        Status.Foreground = (Brush)FindResource(bad ? "Bad" : "Faint");
        Status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public bool Working
    {
        set
        {
            _working = value;
            Busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            OkBtn.IsEnabled = OtherBtn.IsEnabled = !value;
        }
    }

    async void Ok_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        if (_action == null) { Close(); return; }
        await _action(this);
    }

    void Other_Click(object sender, RoutedEventArgs e) { Accepted = false; Close(); }

    void Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } }
}
