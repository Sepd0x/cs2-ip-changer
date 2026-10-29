using SRT.Core;

namespace SRT;

// Recording build: Hide logins is always on and the screen filter (Core/Recording.cs) knows what is personal
public partial class MainWindow
{
    /// <summary>Hide logins from Accounts, or the recording build (always on there, the switch is locked).</summary>
    bool HideLogins => Rec.On || _s?.StreamerMode == true;

    /// <summary>
    /// Valve's regions and the country list are public: the home country or city stays readable there (see Rec.Public).
    /// The Servers paste box's placeholder is a made-up example IP (see Rec.ExampleHint).
    /// </summary>
    void MarkPublicLists()
    {
        Rec.SetPublic(RegionList, true);
        Rec.SetPublic(TplCountry, true);
        Rec.SetExampleHint(BulkInput, true);
    }

    /// <summary>Everything the window knows that points to you or to other players. Read before each frame in the recording build.</summary>
    IEnumerable<string> PersonalWords()
    {
        // Tags are your own labels ("main", "new"), not who you are: as words they would blur the same words all over the UI.
        foreach (var a in _accounts) { yield return a.Login; yield return a.Name; yield return a.Local.PersonaName; if (!CommonNote(a.Note)) yield return a.Note; }
        foreach (var s in _servers) if (s.Players != null) foreach (var p in s.Players) yield return p.Name;
        foreach (var f in _flags) { yield return f.Name; yield return f.Persona; }
        foreach (var i in new[] { _homeInfo, _egressInfo }) if (i != null) { yield return i.Isp; yield return i.City; yield return i.Country; }
        foreach (var h in _history) if (h.Isp != null) foreach (var part in h.Isp.Split(" · ")) yield return part;
        if (_phone != null) { yield return _phone.Model; yield return _phone.Carrier; yield return _phone.WifiSsid; yield return _phone.Serial; }
        if (_last?.Adapters != null) foreach (var ad in _last.Adapters) yield return ad.Ssid;
        if (_s != null) { yield return _s.EgressSsid; yield return _s.SteamApiKey; }
        foreach (var w in ProfileWords()) yield return w;
        yield return Environment.UserName;
        yield return Environment.MachineName;
    }

    // A note that is one plain label, like a tag: the app's own text uses these words too.
    static readonly HashSet<string> NoteLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "main", "alt", "new", "old", "smurf", "spare", "backup", "test", "banned", "vac", "limited", "private", "prime", "premier",
        "faceit", "competitive", "casual", "wingman", "trade", "trading", "farm", "unused", "friend", "friends",
    };

    bool CommonNote(string note)
    {
        var n = note?.Trim();
        if (string.IsNullOrEmpty(n) || n.Any(char.IsWhiteSpace)) return false;
        return NoteLabels.Contains(n) || StarterWords.Contains(n);
    }
}
