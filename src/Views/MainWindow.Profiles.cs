using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Profiles: templates (name, picture, details, privacy) applied to the accounts you pick, with a preview, undo copies and
// saved sign-ins. The Steam side is in Core/Steam/SteamWeb.cs and ProfileSwitcher.cs; research notes in publish/perfis-pesquisa.md.
public partial class MainWindow
{
    readonly ObservableCollection<ProfileRow> _profRows = new();
    readonly ObservableCollection<TemplateItem> _templates = new();
    SessionStore _sessions;
    SnapshotStore _snaps;
    SteamWeb _steamWeb;
    bool _profReady, _profUi, _profBusy, _profDemo;
    readonly DispatcherTimer _profTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly DispatcherTimer _tplSave = new() { Interval = TimeSpan.FromMilliseconds(700) };
    CancellationTokenSource _profCts;
    List<string> _profKnown;
    // The picture of the template on screen: bytes for Steam, image for the preview.
    string _tplAvatarFor;
    byte[] _tplAvatarBytes;
    ImageSource _tplAvatarImg;
    HashSet<string> _demoSigned = new();
    HashSet<string> _starterWords;
    readonly HashSet<string> _profExpired = new();   // Steam refused the saved sign-in this session (its date says it's still good)
    byte[] _demoAvatar;

    ProfileTemplate CurTemplate => (TplCombo.SelectedItem as TemplateItem)?.T;
    static ProfileRow PRow(object sender) => (sender as FrameworkElement)?.DataContext as ProfileRow;

    // ------------------------------------------------------------ Setup

    void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_profReady) return;
        _profReady = true;
        _sessions = new SessionStore();
        _snaps = new SnapshotStore();
        _steamWeb = new SteamWeb();
        ProfList.ItemsSource = _profRows;
        TplCombo.ItemsSource = _templates;
        TplCombo.DisplayMemberPath = nameof(TemplateItem.Title);
        IdentSource.ItemsSource = _profRows;
        IdentSource.DisplayMemberPath = nameof(ProfileRow.Name);

        TplCountry.Items.Add(new ComboBoxItem { Content = Loc.T("None shown"), Tag = "" });
        foreach (var (code, name) in Countries.All) TplCountry.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        foreach (var c in new[] { PrivProfile, PrivGames, PrivInventory, PrivFriends, PrivComments })
            foreach (var (text, level) in new (string, int?)[] { (Loc.T("Keep as it is"), null), (Loc.T("Public"), PrivacyPreset.Public), (Loc.T("Friends only"), PrivacyPreset.Friends), (Loc.T("Private"), PrivacyPreset.Private) })
                c.Items.Add(new ComboBoxItem { Content = text, Tag = level });

        _tplSave.Tick += (_, _) => { _tplSave.Stop(); SaveTemplates(); };
        PageProfiles.IsVisibleChanged += (_, _) => { if (PageProfiles.IsVisible) ShowProfiles(); };
        _profTimer.Tick += (_, _) => WatchNewAccounts();
        if (!_demo && !Rec.On) _profTimer.Start();
        LoadTemplates();
    }

    void ShowProfiles()
    {
        if (!_profReady) return;
        if (_demo && !_profDemo) ProfilesDemo();
        var ids = _accounts.Select(a => a.SteamId).ToList();
        var rebuild = _profRows.Count != ids.Count || _profRows.Any(r => !ids.Contains(r.SteamId));
        if (rebuild) RebuildProfileRows();
        else foreach (var r in _profRows) { r.RefreshAccount(); RefreshSession(r); }
        UpdatePreview(keepResults: !rebuild);   // coming back to the page keeps the results of the last run
    }

    void RebuildProfileRows()
    {
        var picked = _profRows.Where(r => r.Selected).Select(r => r.SteamId).ToHashSet();
        var source = (IdentSource.SelectedItem as ProfileRow)?.SteamId;
        _profRows.Clear();
        foreach (var a in _accounts.OrderByDescending(a => a.Favorite).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var r = new ProfileRow(a) { Selected = picked.Contains(a.SteamId) };
            RefreshSession(r);
            _profRows.Add(r);
        }
        IdentSource.SelectedItem = _profRows.FirstOrDefault(r => r.SteamId == source) ?? _profRows.FirstOrDefault(r => r.Account.Active) ?? _profRows.FirstOrDefault();
        ProfEmpty.Visibility = _profRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // No column titles over an empty list, like Servers (Hidden keeps the row's height).
        ProfHeader.Visibility = _profRows.Count == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    // Narrow window: "Make all look like" moves under the picks instead of being cut.
    void ProfBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The room is the column's, minus the group's padding and edge: ProfBar itself takes the width of what it holds when that
        // doesn't fit (it is then cut off on the right, as Preview was at the smallest window size)
        var room = PageProfiles.ColumnDefinitions[2].ActualWidth - 30;
        var narrow = ProfBarLeft.DesiredSize.Width + ProfBarRight.DesiredSize.Width > room;
        Grid.SetRow(ProfBarRight, narrow ? 1 : 0);
        Grid.SetColumn(ProfBarRight, narrow ? 0 : 2);
        Grid.SetColumnSpan(ProfBarRight, narrow ? 3 : 1);
        ProfBarRight.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ProfBarRight.Margin = new Thickness(0, narrow ? 6 : 0, 0, 0);
    }

    // The column titles keep the rows' width, with or without the list's scrollbar.
    void ProfScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer v) ProfHeader.Margin = new Thickness(9, 0, v.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 15 : 9, 4);
    }

    void RefreshSession(ProfileRow r)
    {
        if (_demo)
        {
            r.SignedIn = _demoSigned.Contains(r.SteamId);
            r.DaysLeft = r.SignedIn ? (r.Name == "drift" ? 9 : 187) : null;   // one about to run out, to see the amber
            r.Expired = false;
            r.Session = r.SignedIn ? Loc.F("Signed in · {0} d left", r.DaysLeft) : Loc.T("Not signed in");
            return;
        }
        var info = _sessions?.Info(r.SteamId);
        var dead = _profExpired.Contains(r.SteamId);
        r.SignedIn = info?.Valid == true && !dead;
        r.DaysLeft = r.SignedIn ? (int)Math.Ceiling((info.Expires - DateTime.Now).TotalDays) : null;
        r.Expired = info != null && (!info.Valid || dead);
        r.Session = info == null ? Loc.T("Not signed in") : r.Expired ? Loc.T("Sign-in expired") : Loc.F("Signed in · {0} d left", r.DaysLeft);
    }

    // ------------------------------------------------------------ Templates

    static List<ProfileTemplate> StarterTemplates() => new()
    {
        new ProfileTemplate { Title = Loc.T("Main look"), Name = "{name}", Fields = ProfileFields.Name | ProfileFields.Avatar },
        new ProfileTemplate { Title = Loc.T("Clean (no details)"), Name = "{name}", Fields = ProfileFields.RealName | ProfileFields.Summary | ProfileFields.Country },
        new ProfileTemplate
        {
            Title = Loc.T("Anonymous"), Name = Loc.T("player {n}"),
            Privacy = new PrivacyPreset { Profile = PrivacyPreset.Public, GameDetails = PrivacyPreset.Private, Inventory = PrivacyPreset.Private, FriendsList = PrivacyPreset.Private, Comments = PrivacyPreset.Private },
            Fields = ProfileFields.Name | ProfileFields.RealName | ProfileFields.Summary | ProfileFields.Country | ProfileFields.Privacy,
        },
    };

    void LoadTemplates()
    {
        var list = _demo ? new List<ProfileTemplate>() : Store.Load(ProfilePaths.Templates, () => new List<ProfileTemplate>());
        list = list.Where(t => t != null).ToList();
        if (list.Count == 0) list = StarterTemplates();
        _templates.Clear();
        foreach (var t in list) { t.Privacy ??= new(); _templates.Add(new TemplateItem(t)); }
        TplCombo.SelectedItem = _templates.FirstOrDefault(x => x.T.Id == _s.ProfileTemplateId) ?? _templates.FirstOrDefault();
    }

    void SaveTemplates()
    {
        if (_demo) return;
        try
        {
            if (ProfilePaths.Redirected(ProfilePaths.Dir)) throw new IOException("the data folder goes through a link or junction");
            Directory.CreateDirectory(ProfilePaths.Dir);
            Store.Save(ProfilePaths.Templates, _templates.Select(t => t.T).ToList());
        }
        catch (Exception ex) { Log.Warn("Profiles: couldn't save the templates: " + Secrets.Clean(ex.Message)); }
    }

    void SaveTemplatesSoon() { _tplSave.Stop(); _tplSave.Start(); }

    void TplCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CurTemplate is not ProfileTemplate t) return;
        if (_s.ProfileTemplateId != t.Id) { _s.ProfileTemplateId = t.Id; SaveSettings(); }
        ShowTemplate(t);
    }

    void ShowTemplate(ProfileTemplate t)
    {
        _profUi = true;
        try
        {
            TplTitle.Text = t.Title ?? "";
            TplName.Text = t.Name ?? "";
            TplReal.Text = t.RealName ?? "";
            TplSummary.Text = t.Summary ?? "";
            TplUrl.Text = t.CustomUrl ?? "";
            TplCountry.SelectedItem = TplCountry.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (t.Country ?? "")) ?? TplCountry.Items[0];
            void P(ComboBox c, int? v) => c.SelectedItem = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int?)i.Tag == v) ?? c.Items[0];
            P(PrivProfile, t.Privacy.Profile); P(PrivGames, t.Privacy.GameDetails); P(PrivInventory, t.Privacy.Inventory); P(PrivFriends, t.Privacy.FriendsList); P(PrivComments, t.Privacy.Comments);
            FName.IsChecked = t.Fields.HasFlag(ProfileFields.Name);
            FAvatar.IsChecked = t.Fields.HasFlag(ProfileFields.Avatar);
            FReal.IsChecked = t.Fields.HasFlag(ProfileFields.RealName);
            FSummary.IsChecked = t.Fields.HasFlag(ProfileFields.Summary);
            FCountry.IsChecked = t.Fields.HasFlag(ProfileFields.Country);
            FPrivacy.IsChecked = t.Fields.HasFlag(ProfileFields.Privacy);
            FUrl.IsChecked = t.Fields.HasFlag(ProfileFields.CustomUrl);
            ProfAuto.IsChecked = _s.ProfileAutoTemplateId == t.Id;
        }
        finally { _profUi = false; }
        LoadTemplateAvatar(t);
        UpdatePreview();
    }

    void TplField_Changed(object sender, RoutedEventArgs e)
    {
        if (_profUi || !_profReady || CurTemplate is not ProfileTemplate t) return;
        t.Title = TplTitle.Text.Trim();
        t.Name = TplName.Text;
        t.RealName = TplReal.Text;
        t.Summary = TplSummary.Text;
        t.CustomUrl = TplUrl.Text.Trim();
        t.Country = (TplCountry.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        int? P(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as int?;
        t.Privacy = new PrivacyPreset { Profile = P(PrivProfile), GameDetails = P(PrivGames), Inventory = P(PrivInventory), FriendsList = P(PrivFriends), Comments = P(PrivComments) };
        (TplCombo.SelectedItem as TemplateItem)?.RefreshTitle();
        SaveTemplatesSoon();
        UpdatePreview();
    }

    void ProfField_Click(object sender, RoutedEventArgs e)
    {
        if (CurTemplate is not ProfileTemplate t) return;
        var f = ProfileFields.None;
        if (FName.IsChecked == true) f |= ProfileFields.Name;
        if (FAvatar.IsChecked == true) f |= ProfileFields.Avatar;
        if (FReal.IsChecked == true) f |= ProfileFields.RealName;
        if (FSummary.IsChecked == true) f |= ProfileFields.Summary;
        if (FCountry.IsChecked == true) f |= ProfileFields.Country;
        if (FPrivacy.IsChecked == true) f |= ProfileFields.Privacy;
        if (FUrl.IsChecked == true) f |= ProfileFields.CustomUrl;
        t.Fields = f;
        SaveTemplatesSoon();
        UpdatePreview();
    }

    void TplNew_Click(object sender, RoutedEventArgs e)
    {
        var t = new ProfileTemplate { Title = Loc.F("New template {0}", _templates.Count + 1) };
        _templates.Add(new TemplateItem(t));
        TplCombo.SelectedIndex = _templates.Count - 1;
        SaveTemplates();
        TplTitle.Focus();
        TplTitle.SelectAll();
    }

    async void TplDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TplCombo.SelectedItem is not TemplateItem it) return;
        if (!await Ask(Loc.T("Delete template"), Loc.F("Delete \"{0}\"? Profiles already changed with it stay as they are.", it.Title), Loc.T("Delete"), danger: true)) return;
        DeleteTemplateAvatar(it.T, it.T.Avatar);
        _templates.Remove(it);
        if (_s.ProfileAutoTemplateId == it.T.Id) { _s.ProfileAutoTemplateId = null; SaveSettings(); }
        if (_templates.Count == 0) _templates.Add(new TemplateItem(new ProfileTemplate()));
        TplCombo.SelectedIndex = 0;
        SaveTemplates();
    }

    void TplCopyFrom_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TplCopyBtn };
        foreach (var r in _profRows)
        {
            var mi = new MenuItem { Header = r.Name + (r.SignedIn ? "" : "  " + Loc.T("(public profile)")), Tag = "" };
            mi.Click += async (_, _) => { if (CurTemplate is ProfileTemplate t && await CopyLookInto(t, r)) { ShowTemplate(t); SaveTemplates(); Toast(Loc.F("Copied {0}'s look into \"{1}\".", r.Name, t.Title), true); } };
            menu.Items.Add(mi);
        }
        // Any profile, also one that isn't saved on this PC.
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var link = new MenuItem { Header = Loc.T("From a Steam profile link…"), Tag = "" };
        link.Click += async (_, _) => { if (CurTemplate is ProfileTemplate t && await CopyLookFromLink(t)) { ShowTemplate(t); SaveTemplates(); Toast(Loc.F("Copied the profile's look into \"{0}\".", t.Title), true); } };
        menu.Items.Add(link);
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------ Template picture

    async void TplPickAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (CurTemplate is not ProfileTemplate t) return;
        if (Rec.On) { Toast(Loc.T("Off in the recording build: Windows' file window isn't filtered. Use the normal app to pick a picture."), false); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Pictures") + "|*.jpg;*.jpeg;*.png;*.gif;*.bmp", Title = Loc.T("Picture for the template") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var fi = new FileInfo(dlg.FileName);
            if (fi.Length > AvatarImage.MaxInput) { Toast(Loc.T("That picture is over 20 MB."), false); return; }
            var raw = await File.ReadAllBytesAsync(fi.FullName);
            var jpg = AvatarImage.Prepare(raw, out var problem);
            if (jpg == null) { Toast(problem, false); return; }
            SetTemplateAvatar(t, jpg);
            t.Fields |= ProfileFields.Avatar;
            ShowTemplate(t);
            SaveTemplates();
        }
        catch (Exception ex) { Toast(Loc.T("Couldn't use that picture: ") + ex.Message, false); }
    }

    void TplClearAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (CurTemplate is not ProfileTemplate t || t.Avatar == null) return;
        DeleteTemplateAvatar(t, t.Avatar);
        t.Avatar = null;
        t.Fields &= ~ProfileFields.Avatar;
        ShowTemplate(t);
        SaveTemplates();
    }

    void SetTemplateAvatar(ProfileTemplate t, byte[] jpg)
    {
        var old = t.Avatar;
        if (_demo) { _demoAvatar = jpg; t.Avatar = "demo.jpg"; }
        else
        {
            if (ProfilePaths.Redirected(ProfilePaths.Avatars)) throw new IOException("the data folder goes through a link or junction");
            Directory.CreateDirectory(ProfilePaths.Avatars);
            var name = $"{t.Id}-{DateTime.Now:yyyyMMddHHmmss}.jpg";
            File.WriteAllBytes(Path.Combine(ProfilePaths.Avatars, name), jpg);
            t.Avatar = name;
        }
        if (old != null && old != t.Avatar) DeleteTemplateAvatar(t, old);
        _tplAvatarFor = null;
    }

    void DeleteTemplateAvatar(ProfileTemplate t, string name)
    {
        if (_demo || name == null || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ProfilePaths.Redirected(ProfilePaths.Avatars)) return;
        if (_templates.Any(x => x.T != t && x.T.Avatar == name)) return;
        try { var p = Path.Combine(ProfilePaths.Avatars, name); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    /// <summary>The template's picture as Steam will get it (checked again, it's a file on disk).</summary>
    byte[] TemplateAvatar(ProfileTemplate t)
    {
        if (t?.Avatar == null) return null;
        if (_tplAvatarFor == t.Avatar) return _tplAvatarBytes;
        _tplAvatarFor = t.Avatar;
        _tplAvatarBytes = null;
        _tplAvatarImg = null;
        try
        {
            byte[] raw = _demo ? _demoAvatar : null;
            if (!_demo && t.Avatar.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                var p = Path.Combine(ProfilePaths.Avatars, t.Avatar);
                if (File.Exists(p) && new FileInfo(p).Length <= AvatarImage.MaxInput) raw = File.ReadAllBytes(p);
            }
            _tplAvatarBytes = raw == null ? null : AvatarImage.Prepare(raw, out _);
            if (_tplAvatarBytes != null && !Rec.On) _tplAvatarImg = ToImage(_tplAvatarBytes);   // recording build: no pictures
        }
        catch (Exception ex)
        {
            // Once per picture (the result is kept for this name): otherwise the template just goes out without its picture.
            _tplAvatarBytes = null;
            Log.Warn("Profiles: a template's picture couldn't be read (" + (ex is IOException or UnauthorizedAccessException ? "the file can't be opened" : ex.Message) + "), so it is left out.");
        }
        return _tplAvatarBytes;
    }

    static ImageSource ToImage(byte[] bytes)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.DecodePixelWidth = 128;
        b.StreamSource = new MemoryStream(bytes);
        b.EndInit();
        b.Freeze();
        return b;
    }

    void LoadTemplateAvatar(ProfileTemplate t)
    {
        TemplateAvatar(t);
        TplAvatar.ImageSource = t.Avatar != null ? _tplAvatarImg : null;
        TplAvatarBox.Visibility = TplAvatar.ImageSource != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ Preview

    /// <summary>clearAliases null = the switch on screen (the preview and Apply warn about it); firstN = what {n} starts at.</summary>
    List<ProfileJob> Plan(ProfileTemplate t, IEnumerable<ProfileRow> rows, bool? clearAliases = null, int firstN = 1) =>
        ProfilePlan.Make(t, t.Fields, rows.Select(r => new ProfileTarget(r.SteamId, r.Account.Login, r.Account.Meta.Profile?.Persona ?? r.Account.Local.PersonaName, r.Name)).ToList(),
            t.Fields.HasFlag(ProfileFields.Avatar) ? TemplateAvatar(t) : null, clearAliases ?? ProfClearNames.IsChecked == true, _accounts.Select(a => a.Login), firstN);

    void UpdatePreview(bool keepResults = false)
    {
        if (!_profReady) return;
        // Nothing to pick, nothing to change: same as Configs with no accounts. "Sign out of all" stays on: it also deletes sign-ins of accounts Steam no longer lists.
        var any = _profRows.Count > 0;
        IdentSource.IsEnabled = ProfIdentBtn.IsEnabled = ProfRestoreBtn.IsEnabled = any;
        if (!_profBusy) ProfApplyBtn.IsEnabled = any;   // while a run goes the button is Stop and is handled by its own click
        var picked = _profRows.Where(r => r.Selected).ToList();
        ProfCount.Text = _profRows.Count == 0 ? "" : Loc.F("{0} of {1} picked", picked.Count, _profRows.Count);
        var t = CurTemplate;
        var jobs = t == null ? new() : Plan(t, picked).ToDictionary(j => j.SteamId);
        foreach (var r in _profRows)
        {
            if (_profBusy) break;
            if (!keepResults) { r.Result = null; r.Ok = null; r.Step = null; }
            if (!r.Selected || !jobs.TryGetValue(r.SteamId, out var j))
            {
                r.After = ""; r.AfterDetail = ""; r.AfterAvatar = null; r.Problem = null;
                continue;
            }
            r.After = j.PersonaName ?? r.Name;
            r.AfterAvatar = j.Avatar != null ? _tplAvatarImg : r.Avatar;
            r.AfterDetail = Describe(j);
            r.Problem = j.Problem ?? (r.SignedIn ? null : Loc.T("Sign in first."));
        }
    }

    static string Describe(ProfileJob j)
    {
        var parts = new List<string>();
        if (j.Problem != null) return "";
        if (j.Fields.HasFlag(ProfileFields.Country)) parts.Add(string.IsNullOrEmpty(j.Country) ? Loc.T("no country") : Countries.NameOf(j.Country));
        if (j.Fields.HasFlag(ProfileFields.RealName)) parts.Add(string.IsNullOrEmpty(j.RealName) ? Loc.T("no real name") : Loc.T("real name"));
        if (j.Fields.HasFlag(ProfileFields.Summary)) parts.Add(string.IsNullOrEmpty(j.Summary) ? Loc.T("no bio") : Loc.T("bio"));
        // Empty = the custom URL is removed. Recording build: the URL leads to the profile, so it isn't shown.
        if (j.Fields.HasFlag(ProfileFields.CustomUrl)) parts.Add(string.IsNullOrEmpty(j.CustomUrl) ? Loc.T("no custom URL") : Rec.On ? Loc.T("custom URL") : "/id/" + j.CustomUrl);
        if (j.Fields.HasFlag(ProfileFields.Privacy)) parts.Add(Loc.T("privacy"));
        if (j.ClearAliases) parts.Add(Loc.T("old names cleared"));
        return parts.Count == 0 ? (j.Fields.HasFlag(ProfileFields.Avatar) ? Loc.T("new picture") : Loc.T("same details")) : string.Join(" · ", parts);
    }

    void ProfRowCheck_Click(object sender, RoutedEventArgs e) => UpdatePreview();
    void ProfSelAll_Click(object sender, RoutedEventArgs e) { foreach (var r in _profRows) r.Selected = true; UpdatePreview(); }
    void ProfSelNone_Click(object sender, RoutedEventArgs e) { foreach (var r in _profRows) r.Selected = false; UpdatePreview(); }
    void ProfSelSigned_Click(object sender, RoutedEventArgs e) { foreach (var r in _profRows) r.Selected = r.SignedIn; UpdatePreview(); }

    // ------------------------------------------------------------ Sign-in

    async void ProfSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (PRow(sender) is not ProfileRow r) return;
        if (r.SignedIn) await ForgetSession(r);
        else await SignIn(r);
    }

    /// <summary>Opens the sign-in window; the token goes straight to the encrypted file and is dropped.</summary>
    async Task<bool> SignIn(ProfileRow r)
    {
        if (Rec.On) { Toast(Loc.T("Signing in is off in the recording build: use the normal app."), false); return false; }
        if (_demo) { Toast(Loc.T("Demo: the sign-in window would open here."), true); return false; }
        await Task.Yield();
        var w = new SteamLoginWindow(_steamWeb, r.Name, r.Account.Login, HideLogins) { Owner = this };
        var ok = w.ShowDialog() == true;
        var token = w.Token;
        var id = w.TokenSteamId;
        w.Token = null;
        if (!ok || token == null) return false;
        try
        {
            var target = _profRows.FirstOrDefault(x => x.SteamId == id);
            if (target == null)
            {
                Log.Warn("Profiles: a sign-in for an account that isn't saved in Steam on this PC was not kept.");
                Toast(Loc.T("That sign-in is for an account that isn't saved in Steam on this PC, so the app didn't keep it."), false);
                return false;
            }
            _sessions.Save(id, token);
            _profExpired.Remove(id);
            RefreshSession(target);
            Log.Ok($"Profiles: signed in to {target.Name}.");
            Toast(target == r ? Loc.F("Signed in to {0}.", r.Name) : Loc.F("That sign-in was {0}, not {1}: it was kept for {0}.", target.Name, r.Name), target == r);
            UpdatePreview();
            return target == r;
        }
        catch (Exception ex)
        {
            Log.Error("Profiles: couldn't save the sign-in: " + Secrets.Clean(ex.Message));
            Toast(Loc.T("Couldn't save the sign-in: ") + Secrets.Clean(ex.Message), Tone.Error);
            return false;
        }
        finally { token = null; }
    }

    async Task ForgetSession(ProfileRow r)
    {
        if (!await Ask(Loc.F("Sign out of {0}", r.Name), Loc.F("Delete the saved sign-in for {0}? The app can't change this profile again until you sign in.", r.Name) + "\n\n" + Loc.F("To end it on Steam's side too: Steam > Settings > Security > Authorized devices (named {0}).", SteamAuth.DeviceName), Loc.T("Sign out"))) return;
        if (!_demo) _sessions.Forget(r.SteamId); else _demoSigned.Remove(r.SteamId);
        _profExpired.Remove(r.SteamId);
        RefreshSession(r);
        Log.Info($"Profiles: signed out of {r.Name} (sign-in deleted).");
        UpdatePreview();
    }

    async void ProfForgetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask(Loc.T("Sign out of all"), Loc.T("Delete every sign-in the app saved for the Profiles page?") + "\n\n" + Loc.F("To end them on Steam's side too: Steam > Settings > Security > Authorized devices (named {0}).", SteamAuth.DeviceName), Loc.T("Sign out of all"), danger: true)) return;
        var n = _demo ? _demoSigned.Count : _sessions.ForgetAll();
        _demoSigned.Clear();
        _profExpired.Clear();
        foreach (var r in _profRows) RefreshSession(r);
        Log.Info($"Profiles: signed out, {Fmt.Count(n, "sign-in")} deleted.");
        Toast(Loc.F("Signed out: {0} deleted.", Fmt.Count(n, "sign-in")), true);
        UpdatePreview();
    }

    // ------------------------------------------------------------ Apply, undo

    async void ProfApply_Click(object sender, RoutedEventArgs e)
    {
        // While a run goes, the button is Stop: the account being changed finishes its current step, the rest are not tried.
        if (_profBusy) { _profCts?.Cancel(); ProfApplyBtn.IsEnabled = false; return; }
        if (CurTemplate is not ProfileTemplate t) return;
        var picked = _profRows.Where(r => r.Selected).ToList();
        if (picked.Count == 0) { Toast(Loc.T("Pick at least one account."), false); return; }
        if (Rec.On) { Toast(Loc.T("Off in the recording build: use the normal app to change profiles."), false); return; }
        var jobs = Plan(t, picked);
        var ready = jobs.Count(j => j.Problem == null && SignedIn(j.SteamId));
        if (ready == 0) { Toast(Loc.T("Nothing can be changed yet: the reason is next to each account."), false); return; }
        var clear = ProfClearNames.IsChecked == true;
        var text = Loc.F("Change {0} with \"{1}\"? They go one after another, about 10 s each.", Fmt.Count(ready, "Steam profile"), t.Title) + "\n\n" + Loc.T("A copy of each profile is kept first: right-click an account > Restore original puts it back.")
                   + (ready < picked.Count ? "\n" + (picked.Count - ready == 1 ? Loc.F("{0} is skipped (the reason is in the list).", Fmt.Count(picked.Count - ready, "picked account")) : Loc.F("{0} are skipped (the reason is in the list).", Fmt.Count(picked.Count - ready, "picked account"))) : "")
                   + (clear ? "\n\n" + Loc.T("Name history will be cleared on each one. That can't be undone, not even by Restore.") : "");
        if (!await Ask(Loc.T("Change profiles"), text, Loc.T("Apply"), danger: clear)) return;
        try { await RunProfileJobs(jobs, picked); }
        finally
        {
            // Can't be undone, so it never carries over to the next Apply by accident.
            if (ProfClearNames.IsChecked == true) { ProfClearNames.IsChecked = false; UpdatePreview(keepResults: true); }
        }
    }

    bool SignedIn(string steamId) => _profRows.FirstOrDefault(r => r.SteamId == steamId)?.SignedIn == true;

    async Task RunProfileJobs(List<ProfileJob> jobs, List<ProfileRow> rows)
    {
        _profBusy = true;
        ProfApplyBtn.Content = Loc.T("Stop");
        _profCts = new CancellationTokenSource();
        foreach (var r in rows) { r.Result = null; r.Ok = null; r.Step = null; }
        ProfileRow Of(string id) => _profRows.FirstOrDefault(r => r.SteamId == id);
        void Done(ProfileResult res)
        {
            var row = Of(res.SteamId);
            if (row != null) { row.Step = null; row.Result = res.Message; row.Ok = res.Ok ? true : res.Skipped ? null : false; }
            // Steam refused the saved sign-in: the row says so and asks for a new one (the file stays until Sign out).
            if (res.Expired && row != null) { _profExpired.Add(res.SteamId); RefreshSession(row); }
            var line = $"Profiles: {res.Shown}: {Secrets.Clean(res.Message)}";
            if (res.Ok) Log.Ok(line); else Log.Warn(line);
        }
        try
        {
            List<ProfileResult> results;
            if (_demo)
            {
                results = new();
                foreach (var j in jobs)
                {
                    var res = j.Problem != null ? new ProfileResult { SteamId = j.SteamId, Shown = j.Shown, Skipped = true, Message = j.Problem }
                        : !SignedIn(j.SteamId) ? new ProfileResult { SteamId = j.SteamId, Shown = j.Shown, Skipped = true, Message = Loc.T("Not signed in: sign in to this account first.") }
                        : new ProfileResult { SteamId = j.SteamId, Shown = j.Shown, Ok = true, Message = Loc.T("Changed: ") + Loc.T("name") + ", " + Loc.T("picture") + "." };
                    results.Add(res); Done(res);
                }
            }
            else
            {
                var applier = new ProfileApplier(_steamWeb, id => _profExpired.Contains(id) ? null : _sessions.Token(id), _snaps);   // a refused sign-in is "Not signed in", as the confirmation said
                results = await applier.Apply(jobs, (j, step) => { if (Of(j.SteamId) is ProfileRow r) r.Step = char.ToUpperInvariant(step[0]) + step[1..] + "…"; }, Done, _profCts.Token);
            }
            var ok = results.Count(r => r.Ok);
            var failed = results.Count(r => !r.Ok && !r.Skipped);
            var changed = rows.Where(r => results.Any(x => (x.Ok || x.Partial) && x.SteamId == r.SteamId)).Select(r => r.Account).ToList();
            if (changed.Count > 0 && !_demo)
            {
                // Steam's public page can take a moment to show the new name and picture.
                await Task.Delay(3000);
                await RefreshAccounts(changed);
                foreach (var r in _profRows) r.RefreshAccount();
            }
            Toast(failed == 0 && ok == results.Count ? Loc.F("{0} changed.", Fmt.Count(ok, "profile")) : Loc.F("{0} changed, {1} not: the reason is next to each account.", ok, results.Count - ok), failed == 0 && ok > 0);
        }
        finally
        {
            _profBusy = false;
            ProfApplyBtn.Content = Loc.T("Apply");
            ProfApplyBtn.IsEnabled = _profRows.Count > 0;
            _profCts.Dispose();
            _profCts = null;
        }
    }

    async void ProfRestoreSelected_Click(object sender, RoutedEventArgs e) => await Restore(_profRows.Where(r => r.Selected).ToList(), true);
    async void ProfRestoreOriginal_Click(object sender, RoutedEventArgs e) { if (PRow(sender) is ProfileRow r) await Restore(new() { r }, true); }
    async void ProfUndoLast_Click(object sender, RoutedEventArgs e) { if (PRow(sender) is ProfileRow r) await Restore(new() { r }, false); }

    /// <summary>Puts the copies back: the first one the app ever took (original) or the one from before the last change.</summary>
    async Task Restore(List<ProfileRow> rows, bool original)
    {
        if (_profBusy) return;
        if (rows.Count == 0) { Toast(Loc.T("Pick the accounts to restore first."), false); return; }
        if (Rec.On) { Toast(Loc.T("Off in the recording build: use the normal app to change profiles."), false); return; }
        var jobs = rows.Select(r =>
        {
            var s = _demo ? null : original ? _snaps.Original(r.SteamId) : _snaps.Latest(r.SteamId);
            return s == null ? new ProfileJob { SteamId = r.SteamId, Shown = r.Name, Problem = Loc.T("No copy saved: the app never changed this account.") } : _snaps.RestoreJob(s, r.Name);
        }).ToList();
        var have = jobs.Count(j => j.Problem == null);
        if (have == 0) { Toast(rows.Count == 1 ? Loc.T("There's no copy of this profile: the app never changed it.") : Loc.T("There's no copy of these profiles: the app never changed them."), false); return; }
        var what = have == 1
            ? original ? Loc.T("how it was before the app first changed it") : Loc.T("how it was before the last change")
            : original ? Loc.T("how they were before the app first changed them") : Loc.T("how they were before the last change");
        if (!await Ask(Loc.T("Restore profiles"), Loc.F("Put {0} back to {1}? Name, picture, details and privacy. Old names cleared before can't come back.", Fmt.Count(have, "profile"), what), Loc.T("Restore"))) return;
        await RunProfileJobs(jobs, rows);
    }

    // ------------------------------------------------------------ Copy a look, make identical

    /// <summary>Reads an account's look into a template: from the edit page when signed in, else from the public profile.</summary>
    async Task<bool> CopyLookInto(ProfileTemplate t, ProfileRow src)
    {
        if (_demo)
        {
            t.Name = src.Name; t.RealName = ""; t.Summary = "Retakes and DM every night."; t.Country = "DE";
            t.Fields = ProfileFields.Look; t.Avatar ??= "demo.jpg"; _tplAvatarFor = null;
            return true;
        }
        if (Rec.On) { Toast(Loc.T("Off in the recording build."), false); return false; }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Toast(Loc.F("Reading {0}'s profile…", src.Name), Tone.Info);
        try
        {
            LookCopy copy = null;
            PrivacyPreset privacy = null;
            var token = _sessions.Token(src.SteamId);
            if (token != null)
            {
                try
                {
                    var s = await CommunitySession.Open(_steamWeb, token, src.SteamId, cts.Token);
                    token = null;
                    var info = await s.ReadInfo(cts.Token);
                    privacy = PrivacyPreset.From(await s.ReadPrivacy(cts.Token));
                    var look = await s.ReadLook(cts.Token);
                    copy = new LookCopy(info.PersonaName, info.RealName, info.Summary, info.Country, look.AvatarUrl, 0);
                }
                // The saved sign-in no longer works: the row says so, and the public profile still gives the look.
                catch (SteamWebException ex) when (ex.Expired) { token = null; privacy = null; _profExpired.Add(src.SteamId); RefreshSession(src); }
            }
            // Without a sign-in: the public profile. What it keeps private stays as it is on each account.
            copy ??= LookCopy.FromPublic(await CommunitySession.ReadPublicLook(_steamWeb, src.SteamId, cts.Token));
            await FillTemplate(t, copy, privacy, cts.Token);
            Log.Info($"Profiles: copied {src.Name}'s look" + (privacy == null ? " (public profile)." : ".")
                     + (copy.Unknown != 0 ? " The public profile doesn't show everything; the rest stays as it is on each account." : ""));
            return true;
        }
        catch (SteamWebException ex) { Toast(Loc.F("Couldn't read {0}'s profile: {1}", src.Name, ex.Message), false); return false; }
        catch (OperationCanceledException) { Toast(Loc.T("Steam took too long to answer."), false); return false; }
        catch (Exception ex) { Log.Error("Profiles: copy look: " + Secrets.Clean(ex.GetBaseException().Message)); Toast(Loc.F("Couldn't read {0}'s profile.", src.Name), false); return false; }
    }

    /// <summary>The picture first (through the same checks as a picture you pick), then the rest: if the file can't be written, the template stays as it was.</summary>
    async Task FillTemplate(ProfileTemplate t, LookCopy copy, PrivacyPreset privacy, CancellationToken ct)
    {
        // Steam's default picture isn't copied: uploaded, it would be a custom picture that only looks like the default.
        var raw = CommunitySession.IsDefaultAvatar(copy.AvatarUrl) ? null : await CommunitySession.DownloadAvatar(_steamWeb, copy.AvatarUrl, ct);
        var jpg = raw == null ? null : AvatarImage.Prepare(raw, out _);
        if (jpg != null) SetTemplateAvatar(t, jpg);
        copy.Into(t, avatar: jpg != null, privacy);
    }

    /// <summary>
    /// Any Steam profile by its link (an account on another PC, a friend's look): only its public profile is read, through the
    /// same Steam-only client. Nothing is sent to that profile and no sign-in is used.
    /// </summary>
    async Task<bool> CopyLookFromLink(ProfileTemplate t)
    {
        DialogInput.Text = "";
        DialogInput.Tag = Loc.T("steamcommunity.com/id/… or SteamID64");
        if (!await Ask(Loc.T("Copy look from a profile link"),
                Loc.T("Paste a Steam profile link (steamcommunity.com/id/… or /profiles/…) or a SteamID64. The app reads its public profile: name, picture, real name, bio and country.") + " " +
                Loc.T("What the profile keeps private stays as it is on each account."), Loc.T("Copy"), Loc.T("Cancel"), inputs: 1, label1: Loc.T("Steam profile"))) return false;
        var path = ProfileLink.Path(DialogInput.Text);
        if (path == null) { Toast(Loc.T("That isn't a Steam profile link. Use steamcommunity.com/id/…, steamcommunity.com/profiles/… or a SteamID64."), false); return false; }
        if (_demo)
        {
            t.Name = "ghost.pt"; t.RealName = ""; t.Summary = "Entry fragger. Mid or nothing."; t.Country = "PT";
            t.Fields = ProfileFields.Look & ~ProfileFields.RealName; t.Avatar ??= "demo.jpg"; _tplAvatarFor = null;
            return true;
        }
        if (Rec.On) { Toast(Loc.T("Off in the recording build."), false); return false; }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Toast(Loc.T("Reading the profile…"), Tone.Info);
        try
        {
            var look = await CommunitySession.ReadPublicLookAt(_steamWeb, path, cts.Token);
            var copy = LookCopy.FromPublic(look, onlyFilled: true);   // what came back empty stays unticked: Apply won't wipe it on your accounts
            await FillTemplate(t, copy, null, cts.Token);
            Log.Info("Profiles: copied the look of a profile from a link (public profile)."
                     + (copy.Unknown != 0 ? " It doesn't show everything; the rest stays as it is on each account." : ""));
            return true;
        }
        catch (SteamWebException ex) { Toast(Loc.T("Couldn't read that profile: ") + ex.Message, false); return false; }
        catch (OperationCanceledException) { Toast(Loc.T("Steam took too long to answer."), false); return false; }
        catch (Exception ex) { Log.Error("Profiles: copy look from a link: " + Secrets.Clean(ex.GetBaseException().Message)); Toast(Loc.T("Couldn't read that profile."), false); return false; }
    }

    async void ProfCopyInto_Click(object sender, RoutedEventArgs e)
    {
        if (PRow(sender) is ProfileRow r && CurTemplate is ProfileTemplate t && await CopyLookInto(t, r))
        {
            ShowTemplate(t); SaveTemplates();
            Toast(Loc.F("Copied {0}'s look into \"{1}\".", r.Name, t.Title), true);
        }
    }

    async void ProfMakeLikeThis_Click(object sender, RoutedEventArgs e) { if (PRow(sender) is ProfileRow r) await MakeIdentical(r); }

    async void ProfIdentical_Click(object sender, RoutedEventArgs e)
    {
        if (IdentSource.SelectedItem is ProfileRow src) await MakeIdentical(src);
        else Toast(Loc.T("Pick the account to copy first."), false);
    }

    /// <summary>One click: the source's look into a new template, every other account picked, the preview shown. Apply does the rest.</summary>
    async Task MakeIdentical(ProfileRow src)
    {
        var t = new ProfileTemplate { Title = Loc.F("Look of {0}", src.Name), Name = NamePattern.Escape(src.Name) };
        if (!await CopyLookInto(t, src)) return;
        var it = new TemplateItem(t);
        _templates.Add(it);
        TplCombo.SelectedItem = it;
        IdentSource.SelectedItem = src;
        foreach (var r in _profRows) r.Selected = r != src;
        SaveTemplates();
        UpdatePreview();
        Toast(Loc.F("Preview ready: the picked accounts get {0}'s look. Check it, then click Apply.", src.Name), Tone.Info);
    }

    void ProfOpenEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Rec.On) { Toast(Loc.T("Off in the recording build: use the normal app to open Steam's edit page."), false); return; }
        if (PRow(sender) is not ProfileRow r || _demo) return;
        try { Steam.OpenUrl($"https://steamcommunity.com/profiles/{r.SteamId}/edit/info"); }
        catch (Exception ex) { Toast(ex.Message, false); }
    }

    // ------------------------------------------------------------ From the Accounts page

    void AccChangeProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        NavProfiles.IsChecked = true;
        ShowProfiles();
        foreach (var r in _profRows) r.Selected = r.SteamId == a.SteamId;
        UpdatePreview();
    }

    async void AccCopyLook_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        NavProfiles.IsChecked = true;
        ShowProfiles();
        if (_profRows.FirstOrDefault(r => r.SteamId == a.SteamId) is ProfileRow src) await MakeIdentical(src);
    }

    // ------------------------------------------------------------ New accounts

    void ProfAuto_Click(object sender, RoutedEventArgs e)
    {
        if (CurTemplate is not ProfileTemplate t) return;
        _s.ProfileAutoTemplateId = ProfAuto.IsChecked == true ? t.Id : null;
        SaveSettings();
        Toast(ProfAuto.IsChecked == true ? Loc.F("New accounts will get \"{0}\" (the app asks you to sign in to each once).", t.Title) : Loc.T("New accounts keep their own look."), true);
    }

    /// <summary>Every 10 s: an account that appears in Steam after the first run is offered the auto template, once.</summary>
    async void WatchNewAccounts()
    {
        if (_demo || Rec.On || _profBusy || _accounts.Count == 0 || Dialog.Visibility == Visibility.Visible || OwnedWindows.Count > 0) return;
        // No file yet (or an empty one) is the first run: the ones there now are not "new"
        if (_profKnown == null && Store.Load(ProfilePaths.Known, () => new List<string>()) is { Count: > 0 } saved) _profKnown = saved;
        var first = _profKnown == null;
        var ids = NewAccounts(ref _profKnown, _steamIds);
        if (first) { SaveKnown(); return; }
        if (ids.Count == 0) return;
        SaveKnown();
        var fresh = _accounts.Where(a => ids.Contains(a.SteamId)).ToList();   // a hidden account is remembered, never offered
        var t = _templates.FirstOrDefault(x => x.T.Id == _s.ProfileAutoTemplateId)?.T;
        if (t == null) return;
        ShowProfiles();
        var nextN = _profRows.Count;   // past the numbers a manual Apply to the others gave out
        foreach (var a in fresh)
        {
            if (_profRows.FirstOrDefault(r => r.SteamId == a.SteamId) is not ProfileRow r) continue;
            if (!r.SignedIn)
            {
                if (!await Ask(Loc.T("New account"), Loc.F("{0} is new on this PC. Give it the \"{1}\" look? The app needs to sign in to it once.", a.Name, t.Title), Loc.T("Sign in"), Loc.T("Not now"))) { Log.Info($"Profiles: {a.Name} is new; the auto template was skipped."); continue; }
                if (!await SignIn(r)) continue;
            }
            // Automatic runs never clear old names (no warning on this path), and {n} is one no other account uses.
            var n = ProfilePlan.FreeN(t, nextN, r.Account.Meta.Profile?.Persona ?? r.Account.Local.PersonaName, _accounts.Where(x => x.SteamId != a.SteamId).Select(x => x.Name));
            nextN = n + 1;
            await RunProfileJobs(Plan(t, new[] { r }, clearAliases: false, firstN: n), new() { r });
        }
    }

    void SaveKnown()
    {
        try
        {
            if (ProfilePaths.Redirected(ProfilePaths.Dir)) return;
            Directory.CreateDirectory(ProfilePaths.Dir);
            Store.Save(ProfilePaths.Known, _profKnown);
        }
        catch { }
    }

    /// <summary>The starter templates' words: the app's own, never personal.</summary>
    HashSet<string> StarterWords => _starterWords ??= StarterTemplates().SelectMany(TemplateItem.Words).Where(w => !string.IsNullOrWhiteSpace(w)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>For the recording build's screen filter: what templates and the preview would put on profiles.</summary>
    IEnumerable<string> ProfileWords()
    {
        // Only what the user typed: the starter templates' words ("Anonymous", "player") are the app's, and would blur plain UI text.
        foreach (var it in _templates)
            foreach (var w in TemplateItem.Words(it.T)) if (!string.IsNullOrWhiteSpace(w) && !StarterWords.Contains(w)) yield return w;
        foreach (var r in _profRows) yield return r.After;
        // The NOW column shows each account's public location (free text, often the city).
        foreach (var a in _accounts) foreach (var w in ProfileRow.LocationWords(a.Meta.Profile?.Country)) yield return w;
    }

    // ------------------------------------------------------------ Demo (screenshots)

    void ProfilesDemo()
    {
        _profDemo = true;
        _demoAvatar = DemoAvatar();
        foreach (var a in _accounts.Where(a => a.Name != "kestrel" && a.Name != "vanta")) _demoSigned.Add(a.SteamId);
        _templates.Clear();
        var team = new ProfileTemplate
        {
            Title = "Team look", Name = "{name} | NV", Avatar = "demo.jpg", Country = "DE", Summary = "Retakes and DM every night.",
            Privacy = new PrivacyPreset { Profile = PrivacyPreset.Public, Inventory = PrivacyPreset.Private, FriendsList = PrivacyPreset.Private },
            Fields = ProfileFields.Name | ProfileFields.Avatar | ProfileFields.Country | ProfileFields.Summary | ProfileFields.Privacy,
        };
        _templates.Add(new TemplateItem(team));
        foreach (var t in StarterTemplates()) _templates.Add(new TemplateItem(t));
        RebuildProfileRows();
        foreach (var r in _profRows) r.Selected = r.Name is "nightfall" or "orbit" or "drift" or "pulse" or "kestrel" or "atlas";
        TplCombo.SelectedIndex = 0;
    }

    static byte[] DemoAvatar()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x1E, 0x3A, 0x5F), Color.FromRgb(0x5B, 0xA8, 0xF0), 45);
            dc.DrawRectangle(bg, null, new Rect(0, 0, 256, 256));
            var pen = new Pen(Brushes.White, 10);
            dc.DrawEllipse(null, pen, new Point(128, 128), 70, 70);
            dc.DrawLine(pen, new Point(128, 30), new Point(128, 96));
            dc.DrawLine(pen, new Point(128, 160), new Point(128, 226));
            dc.DrawLine(pen, new Point(30, 128), new Point(96, 128));
            dc.DrawLine(pen, new Point(160, 128), new Point(226, 128));
            dc.DrawEllipse(Brushes.White, null, new Point(128, 128), 9, 9);
        }
        var rtb = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return AvatarImage.Prepare(ms.ToArray(), out _);
    }
}
