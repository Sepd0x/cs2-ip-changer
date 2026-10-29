using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SRT.Core;

namespace SRT;

// Profiles page. The templates, sign-ins and undo copies live in SRT_Dados/profiles (see Core/Steam/ProfileSwitcher.cs),
// not here: settings.json goes into Save bug report.
public sealed partial class Settings
{
    public string ProfileTemplateId { get; set; }                          // the template picked last
    public string ProfileAutoTemplateId { get; set; }                      // apply this template to accounts that appear later (null = off)
}

/// <summary>One account on the Profiles page: picked or not, how it looks now and how it would look after.</summary>
public sealed class ProfileRow : Observable
{
    public AccountItem Account { get; }
    bool _selected, _busy, _signedIn;
    string _after, _afterDetail, _problem, _result, _session, _step;
    bool? _ok;
    ImageSource _afterAvatar;

    public ProfileRow(AccountItem a) { Account = a; }

    public string SteamId => Account.SteamId;
    public string Name => Account.Name;
    public string Initial => Account.Initial;
    public ImageSource Avatar => Account.Avatar;
    public bool HasAvatar => Account.HasAvatar;
    public string NowDetail => Account.Meta.Profile?.Country is string c && c.Length > 0 ? c : "";
    public bool HasNowDetail => NowDetail.Length > 0;

    /// <summary>For the recording build's screen filter: the public location (free text, often the city) and each of its parts.</summary>
    public static IEnumerable<string> LocationWords(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) yield break;
        yield return location.Trim();
        foreach (var part in location.Split(',')) yield return part.Trim();
    }

    public bool Selected { get => _selected; set { if (Set(ref _selected, value)) Raise(nameof(NotSelected)); } }
    public bool NotSelected => !Selected;
    public bool Busy { get => _busy; set => Set(ref _busy, value); }
    public bool SignedIn { get => _signedIn; set { if (Set(ref _signedIn, value)) { Raise(nameof(SessionBrush)); Raise(nameof(SignInText)); } } }
    public string Session { get => _session; set => Set(ref _session, value); }
    public Brush SessionBrush => SignedIn ? Ui.Good : Ui.Faint;
    public string SignInText => SignedIn ? "Forget" : "Sign in";

    public string After { get => _after; set { if (Set(ref _after, value)) Raise(nameof(AfterInitial)); } }
    public string AfterDetail { get => _afterDetail; set => Set(ref _afterDetail, value); }
    public ImageSource AfterAvatar { get => _afterAvatar; set { if (Set(ref _afterAvatar, value)) Raise(nameof(HasAfterAvatar)); } }
    public bool HasAfterAvatar => AfterAvatar != null;
    public string AfterInitial => Rec.On || string.IsNullOrEmpty(After) ? "?" : After.Substring(0, 1).ToUpperInvariant();

    /// <summary>Why this account would not be changed (shown in red instead of the preview).</summary>
    public string Problem { get => _problem; set { if (Set(ref _problem, value)) { Raise(nameof(HasProblem)); Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } } }
    public bool HasProblem => Problem != null;

    public string Step { get => _step; set { if (Set(ref _step, value)) { Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } } }
    public string Result { get => _result; set { if (Set(ref _result, value)) { Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } } }
    public bool? Ok { get => _ok; set { if (Set(ref _ok, value)) { Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); } } }

    public string StatusText => Step ?? Result ?? Problem ?? "";
    public Brush StatusBrush => Step != null ? Ui.Accent : Result != null ? (Ok == true ? Ui.Good : Ok == false ? Ui.Bad : Ui.Warn) : Problem != null ? Ui.Bad : Ui.Faint;

    public override string ToString() => Name;   // the combo box shows the account name

    public void RefreshAccount()
    {
        foreach (var n in new[] { nameof(Name), nameof(Initial), nameof(Avatar), nameof(HasAvatar), nameof(NowDetail), nameof(HasNowDetail), nameof(AfterInitial), nameof(SignInText), nameof(SessionBrush) }) Raise(n);
    }
}

/// <summary>A template in the list (the title shown; the template itself is edited in place).</summary>
public sealed class TemplateItem : Observable
{
    public ProfileTemplate T { get; }
    public TemplateItem(ProfileTemplate t) { T = t; }
    public string Title => string.IsNullOrWhiteSpace(T.Title) ? "Untitled" : T.Title;
    public void RefreshTitle() => Raise(nameof(Title));

    /// <summary>For the recording build's screen filter: the template's own text, its name and URL split around the tags.</summary>
    public static IEnumerable<string> Words(ProfileTemplate t)
    {
        yield return t.Title; yield return t.RealName; yield return t.Summary;
        foreach (var part in NamePattern.Literals(t.Name)) yield return part;
        foreach (var part in NamePattern.Literals(t.CustomUrl)) yield return part;
    }
    public override string ToString() => Title;
}
