using SRT.Core;

namespace SRT;

// Recording build: Hide logins is always on and the screen filter (Core/Recording.cs) knows what is personal
public partial class MainWindow
{
    /// <summary>Hide logins from Accounts, or the recording build (always on there, the switch is locked).</summary>
    bool HideLogins => Rec.On || _s?.StreamerMode == true;

    /// <summary>Everything the window knows that points to you or to other players. Read before each frame in the recording build.</summary>
    IEnumerable<string> PersonalWords()
    {
        foreach (var a in _accounts) { yield return a.Login; yield return a.Name; yield return a.Local.PersonaName; yield return a.Note; yield return a.Tags; }
        foreach (var s in _servers) if (s.Players != null) foreach (var p in s.Players) yield return p.Name;
        foreach (var f in _flags) { yield return f.Name; yield return f.Persona; }
        foreach (var i in new[] { _homeInfo, _egressInfo }) if (i != null) { yield return i.Isp; yield return i.City; yield return i.Country; }
        foreach (var h in _history) if (h.Isp != null) foreach (var part in h.Isp.Split(" · ")) yield return part;
        if (_phone != null) { yield return _phone.Model; yield return _phone.Carrier; yield return _phone.WifiSsid; yield return _phone.Serial; }
        if (_last?.Adapters != null) foreach (var ad in _last.Adapters) yield return ad.Ssid;
        if (_s != null) { yield return _s.EgressSsid; yield return _s.SteamApiKey; }
        yield return Environment.UserName;
        yield return Environment.MachineName;
    }
}
