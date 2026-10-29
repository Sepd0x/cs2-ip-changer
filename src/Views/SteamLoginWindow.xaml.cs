using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SRT.Core;

namespace SRT;

/// <summary>
/// Signs in to one Steam account for the Profiles page, with the password (and Steam Guard) or a QR code for the Steam app.
/// The password goes from the PasswordBox (a SecureString) straight into Steam's RSA key; no string ever holds it, and the
/// bytes are wiped right after. What comes back is a refresh token, which the caller saves encrypted and then drops.
/// </summary>
public partial class SteamLoginWindow : Window
{
    readonly SteamAuth _auth;
    readonly string _login;
    readonly CancellationTokenSource _cts = new();
    CancellationTokenSource _attempt;
    CancellationToken _pollingFor;
    AuthStart _start;
    GuardType _codeType;

    /// <summary>The refresh token, read once by the caller (who saves it and clears this).</summary>
    public string Token { get; set; }
    public string TokenSteamId { get; private set; }

    public SteamLoginWindow(SteamWeb web, string shownName, string login, bool hideLogin)
    {
        InitializeComponent();
        _auth = new SteamAuth(web);
        _login = login;
        CancelBtn.IsCancel = true;
        Title_.Text = "Sign in to " + shownName;
        Intro.Text = "Needed to change this account's profile (name, picture, privacy and more). The app keeps only a sign-in key from Steam on this PC, locked to your Windows account, so you don't have to type your password every time. "
                     + "To remove it later: Steam > Settings > Security > Authorized devices (\"" + SteamAuth.DeviceName + "\").";
        LoginText.Text = hideLogin ? "Hidden (Hide usernames is on)" : login;
        if (hideLogin) LoginText.ClearValue(System.Windows.Controls.TextBlock.FontFamilyProperty);
        if (string.IsNullOrEmpty(login)) { ModePwd.IsEnabled = false; ModeQr.IsChecked = true; }
        Loaded += (_, _) => { if (ModePwd.IsChecked == true) Pwd.Focus(); };
        Closed += (_, _) => { _cts.Cancel(); Pwd.Clear(); };
    }

    void Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } }

    void Say(string text, Brush b = null) { Status.Text = text; Status.Foreground = b ?? (Brush)FindResource("Faint"); }
    void Bad(string text) => Say(text, (Brush)FindResource("Bad"));

    /// <summary>A sign-in that failed: shown in the window, and one Log line (never the username or the password).</summary>
    void Failed(string reason)
    {
        Bad(reason);
        Log.Warn(FailedLine(reason, _login));
    }

    /// <summary>The Log line for a failed sign-in. The reason is Steam's or the app's own text; a username in it is hidden anyway.</summary>
    public static string FailedLine(string reason, string login)
    {
        var r = Secrets.Clean((reason ?? "").Trim().TrimEnd('.'));
        if (!string.IsNullOrEmpty(login) && login.Length >= 2) r = System.Text.RegularExpressions.Regex.Replace(r, System.Text.RegularExpressions.Regex.Escape(login), "(hidden)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return "Profiles: sign-in to an account failed: " + (r.Length == 0 ? "no reason given" : r) + ".";
    }
    void Working(bool on, string text = null)
    {
        Busy.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        GoBtn.IsEnabled = !on;
        if (text != null) Say(text);
    }

    CancellationToken NewAttempt()
    {
        _attempt?.Cancel();
        _attempt = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        return _attempt.Token;
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || QrPanel == null) return;
        var qr = ModeQr.IsChecked == true;
        PwdPanel.Visibility = qr ? Visibility.Collapsed : Visibility.Visible;
        CodePanel.Visibility = Visibility.Collapsed;
        QrPanel.Visibility = qr ? Visibility.Visible : Visibility.Collapsed;
        GoBtn.Content = qr ? "New code" : "Sign in";
        Say("");
        NewAttempt();
        Working(false);
        if (qr) _ = StartQr();
    }

    async void Go_Click(object sender, RoutedEventArgs e)
    {
        if (CodePanel.Visibility == Visibility.Visible) await SubmitCode();
        else if (ModeQr.IsChecked == true) await StartQr();
        else await StartPassword();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts.Cancel();   // IsCancel closes the window

    /// <summary>The SecureString as UTF-8 bytes, with no managed string in between. The caller wipes the result.</summary>
    static byte[] Utf8(SecureString s)
    {
        var p = Marshal.SecureStringToGlobalAllocUnicode(s);
        var chars = new char[s.Length];
        try
        {
            Marshal.Copy(p, chars, 0, chars.Length);
            return Encoding.UTF8.GetBytes(chars);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(p);
            Array.Clear(chars);
        }
    }

    async Task StartPassword()
    {
        // Every read of SecurePassword is a new copy: one is taken, and disposed (zeroed) when this ends.
        using var sp = Pwd.SecurePassword;
        if (sp.Length == 0) { Bad("Type the password."); Pwd.Focus(); return; }
        var ct = NewAttempt();
        Working(true, "Signing in…");
        byte[] pw = null;
        try
        {
            pw = Utf8(sp);
            var (enc, ts) = await _auth.EncryptPassword(_login, pw, ct);
            CryptographicOperations.ZeroMemory(pw);
            pw = null;
            sp.Clear();
            Pwd.Clear();
            _start = await _auth.BeginWithCredentials(_login, enc, ts, ct);
            await Guards(ct);
        }
        catch (SteamWebException ex) { Failed(ex.Message); Working(false); Pwd.Focus(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Unexpected(ex); Pwd.Focus(); }
        finally { if (pw != null) CryptographicOperations.ZeroMemory(pw); }
    }

    async Task Guards(CancellationToken ct)
    {
        var g = _start.Guards;
        if (g.Contains(GuardType.None)) { Say("Signed in, finishing…"); StartPolling(ct); return; }
        if (g.Contains(GuardType.DeviceCode))
        {
            AskCode(GuardType.DeviceCode, "Code from the Steam app (Steam Guard)");
            if (g.Contains(GuardType.DeviceConfirmation)) { Say("Type the code from the Steam app, or approve the sign-in there."); StartPolling(ct); }
            else Say("Type the code from the Steam app.");
            return;
        }
        if (g.Contains(GuardType.EmailCode))
        {
            if (g.Contains(GuardType.MachineToken)) await _auth.SendEmailCode(_start, ct);
            AskCode(GuardType.EmailCode, "Code from Steam's email");
            Say("Steam emailed a code to this account's address.");
            return;
        }
        if (g.Contains(GuardType.DeviceConfirmation)) { Working(true, "Approve the sign-in in the Steam app on your phone."); StartPolling(ct); return; }
        if (g.Contains(GuardType.EmailConfirmation)) { Working(true, "Approve the sign-in from the email Steam sent."); StartPolling(ct); return; }
        Failed("Steam asked for a check the app doesn't know. Try the QR code.");
        Working(false);
    }

    void AskCode(GuardType type, string label)
    {
        _codeType = type;
        PwdPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Visible;
        CodeLabel.Text = label;
        GoBtn.Content = "Confirm";
        Working(false);
        Code.Focus();
    }

    /// <summary>Back to the password step after Steam dropped the session; the status text stays.</summary>
    void BackToPassword()
    {
        if (ModeQr.IsChecked == true || CodePanel.Visibility != Visibility.Visible) { Working(false); return; }
        CodePanel.Visibility = Visibility.Collapsed;
        PwdPanel.Visibility = Visibility.Visible;
        GoBtn.Content = "Sign in";
        Code.Clear();
        NewAttempt();
        Working(false);
        Pwd.Focus();
    }

    /// <summary>An answer the app can't read (not a Steam error): logged by type only, the window stays usable.</summary>
    void Unexpected(Exception ex)
    {
        Log.Warn(FailedLine("unexpected answer (" + ex.GetType().Name + ")", _login));
        Bad("Steam's answer was not what the app expects. Try again.");
        Working(false);
    }

    async Task SubmitCode()
    {
        var code = Code.Text.Trim();
        if (code.Length < 4) { Bad("Type the code."); return; }
        var ct = _attempt?.Token ?? NewAttempt();
        Working(true, "Checking the code…");
        try
        {
            await _auth.SubmitCode(_start, code, _codeType, ct);
            StartPolling(ct);
        }
        // 9/27: Steam ended the session, the code can't work any more: back to the password.
        catch (SteamWebException ex) when (ex.EResult is 9 or 27) { Failed(ex.Message); BackToPassword(); }
        catch (SteamWebException ex) { Failed(ex.Message); Working(false); Code.SelectAll(); Code.Focus(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Unexpected(ex); }
    }

    async Task StartQr()
    {
        var ct = NewAttempt();
        Working(true, "Asking Steam for a code…");
        try
        {
            _start = await _auth.BeginWithQr(ct);
            if (!ShowQr()) return;
            Working(false, "Waiting for the scan…");
            GoBtn.IsEnabled = true;
            StartPolling(ct);
        }
        catch (SteamWebException ex) { Failed(ex.Message); Working(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Unexpected(ex); }
    }

    /// <summary>Only Steam's own short links become a QR code.</summary>
    bool ShowQr()
    {
        if (!Uri.TryCreate(_start?.ChallengeUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || u.Host != "s.team")
        {
            Failed("Steam's answer was not what the app expects.");
            Working(false);
            return false;
        }
        QrImage.Source = QrCode.Encode(u.ToString()).ToImage(6);
        return true;
    }

    void StartPolling(CancellationToken ct)
    {
        if (_pollingFor == ct) return;
        _pollingFor = ct;
        _ = PollLoop(ct);
    }

    async Task PollLoop(CancellationToken ct)
    {
        var until = DateTime.Now.AddMinutes(4);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_start.Interval, 1, 10)), ct);
                var p = await _auth.Poll(_start, ct);
                if (p.NewChallengeUrl != null && ModeQr.IsChecked == true) { _start.ChallengeUrl = p.NewChallengeUrl; ShowQr(); }
                if (p.HadRemoteInteraction && ModeQr.IsChecked == true) Working(true, "Scanned. Approve the sign-in in the Steam app.");
                if (p.RefreshToken != null) { Finish(p.RefreshToken); return; }
                if (DateTime.Now > until) { Failed("That took too long. Start again."); BackToPassword(); return; }
            }
        }
        catch (SteamWebException ex) { Failed(ex.Message); if (ex.EResult is 9 or 27) BackToPassword(); else Working(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Unexpected(ex); }
        finally { if (_pollingFor == ct) _pollingFor = default; }
    }

    void Finish(string token)
    {
        var t = SteamToken.Read(token);
        if (t == null || !t.IsWebRefresh) { Failed("Steam's answer was not a web sign-in. Try again."); Working(false); return; }
        Token = token;
        TokenSteamId = t.SteamId;
        DialogResult = true;
    }
}
