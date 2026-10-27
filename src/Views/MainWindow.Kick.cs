using System.Diagnostics;
using System.Windows;
using SRT.Core;

namespace SRT;

// Kick or ban seen in CS2's console, and "Change IP and rejoin" (button and Ctrl+Alt+R)
public partial class MainWindow
{
    readonly ConsoleTail _cs2Console = new();
    DateTime _lastKickAt = DateTime.MinValue;
    DateTime? _cs2StartedAt, _cs2LogSeenAt;
    long _cs2LogLen = -1;
    bool _condebugHinted, _cs2LogOk, _rejoining;
    string _consoleServer;
    int _disconnectSamples;

    /// <summary>Every tick while CS2 runs: new console lines, looking for a kick or ban.</summary>
    void WatchCs2Console()
    {
        if (_demo) return;
        if (!Steam.Cs2Running)
        {
            _cs2Console.Reset();
            _cs2StartedAt = _cs2LogSeenAt = null;
            _cs2LogLen = -1;
            _cs2LogOk = false;
            return;
        }
        _cs2StartedAt ??= Cs2StartedAt();
        var path = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
        if (path == null) return;
        CheckCondebug(path);
        var kicked = false;
        foreach (var line in _cs2Console.ReadNew(path))
        {
            var joined = Cs2Console.ConnectedServer(line);
            if (joined != null) _consoleServer = joined;
            var kind = Cs2Console.Classify(line);
            if (kind != KickKind.None && !kicked) { kicked = true; OnKicked(kind, line); }
            else if (Cs2Console.IsDisconnect(line) && _disconnectSamples++ < 40)
                Log.Info("CS2 console: " + Cut(line));   // samples to learn the real format
        }
    }

    static string Cut(string line) => line.Length > 220 ? line[..220] + "…" : line;

    static DateTime? Cs2StartedAt()
    {
        DateTime? t = null;
        foreach (var p in Process.GetProcessesByName("cs2"))
        {
            try { t ??= p.StartTime; } catch { }
            finally { p.Dispose(); }
        }
        return t;
    }

    /// <summary>CS2 open for a while and console.log not written: it was started without -condebug.</summary>
    void CheckCondebug(string path)
    {
        if (_condebugHinted || _cs2LogOk || _cs2StartedAt == null) return;
        DateTime? written = null;
        long len = -1;
        try { var fi = new FileInfo(path); if (fi.Exists) { written = fi.LastWriteTime; len = fi.Length; } } catch { }
        // The write time of a file kept open can lag, so a size change while watching also counts.
        if (_cs2LogSeenAt == null) { _cs2LogSeenAt = DateTime.Now; _cs2LogLen = len; return; }
        if (len != _cs2LogLen) { _cs2LogOk = true; return; }
        if (written >= _cs2StartedAt) return;
        if (DateTime.Now - _cs2StartedAt < TimeSpan.FromSeconds(90) || DateTime.Now - _cs2LogSeenAt < TimeSpan.FromSeconds(60)) return;
        _condebugHinted = true;
        Log.Warn("CS2 is not writing console.log, so kicks and bans can't be detected. In Steam, add -condebug to CS2's launch options.");
        Toast("To detect kicks and bans, add -condebug to CS2's launch options in Steam (CS2 > Properties).", false);
    }

    void OnKicked(KickKind kind, string line)
    {
        var what = kind == KickKind.Ban ? "banned" : "kicked";
        // The server CS2 was really on (also when joined from the console), if it's in the list.
        if (_consoleServer != null && _servers.Any(x => x.Key == _consoleServer) && _consoleServer != _s.LastServer)
        {
            _s.LastServer = _consoleServer;
            _s.LastServerAt = DateTime.Now;
            SaveSettings();
        }
        // One kick can write several lines; the raw line always goes to the Log to collect real samples.
        if ((DateTime.Now - _lastKickAt).TotalSeconds < 20) { Log.Info($"CS2 console ({what}): " + Cut(line)); return; }
        _lastKickAt = DateTime.Now;
        System.Media.SystemSounds.Hand.Play();
        Log.Warn($"You were {what} from {_s.LastServer ?? "the server"}. Console: " + Cut(line));
        Diag.Write("kick", new { kind = what, line, server = _s.LastServer });
        Toast(_s.LastServer == null ? $"You were {what} from the server."
            : $"You were {what} from the server. Press Ctrl+Alt+R to change IP and rejoin.", false);
        UpdateRejoinBtn();
        if (_s.LastServer != null && Dialog.Visibility != Visibility.Visible && !_changingIp && !_rejoining) _ = OfferRejoin(what);
    }

    async Task OfferRejoin(string what)
    {
        if (await Ask($"You were {what}", $"CS2 says you were {what} from {_s.LastServer}. Change IP and rejoin?" + (what == "banned" ? "\n\nIf the ban is on your Steam account, a new IP won't help." : ""), "Change IP and rejoin", "Not now"))
            await ChangeIpAndRejoin("After the " + (what == "banned" ? "ban" : "kick"));
    }

    void UpdateRejoinBtn()
    {
        RejoinBtn.IsEnabled = !_changingIp && !_rejoining && _s.LastServer != null && _phone?.Ready == true;
        RejoinBtn.ToolTip = (_s.LastServer == null ? "Join a server from Servers first. " : $"Changes the IP, then joins {_s.LastServer} again. ")
            + "Only joins with protection confirmed. In game: Ctrl+Alt+R.";
    }

    async void Rejoin_Click(object sender, RoutedEventArgs e) => await ChangeIpAndRejoin("Button");

    /// <summary>The normal Change IP, then the normal join to the last server (with all its checks).</summary>
    async Task ChangeIpAndRejoin(string from)
    {
        if (_rejoining || _changingIp) return;
        if (_demo) { Toast("Demo: IP changed, rejoining.", true); return; }
        var s = _servers.FirstOrDefault(x => x.Key == _s.LastServer);
        if (s == null)
        {
            Toast(_s.LastServer == null ? "No last server yet. Join one from Servers first." : $"The last server ({_s.LastServer}) is no longer in your list.", false);
            return;
        }
        _rejoining = true;
        UpdateRejoinBtn();
        try
        {
            Log.Info($"{from}: change IP and rejoin {s.Key}.");
            if (!await ChangeIp(confirm: false)) return;
            await Connect(s);
        }
        finally { _rejoining = false; UpdateRejoinBtn(); }
    }
}
