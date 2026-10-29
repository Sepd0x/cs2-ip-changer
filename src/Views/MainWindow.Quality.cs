using SRT.Core;

namespace SRT;

// How good the phone connection is right now, and what the app does about it (warns, and drops Whole PC while it's bad)
public partial class MainWindow
{
    readonly List<LinkQuality> _quality = new();
    QualityLevel _qualityLevel;
    DateTime _qualityAt = DateTime.MinValue;
    bool _measuring;

    /// <summary>Every 30 s with a trusted phone connection (the pings take about 2 s and use almost no data).</summary>
    async Task WatchQuality()
    {
        if (_demo || _measuring || _changingIp || _last?.Egress == null || (DateTime.Now - _qualityAt).TotalSeconds < 30) return;
        _measuring = true;
        _qualityAt = DateTime.Now;
        try
        {
            var q = await Quality.Measure(Net.EchoIp, _phone?.Ready == true ? _phone.NetType : null);
            if (_changingIp || _last?.Egress == null) return;   // the IP changed meanwhile: that sample is about the old cell
            _quality.Add(q);
            if (_quality.Count > 3) _quality.RemoveAt(0);
            Diag.Write("quality", new { q.AvgMs, q.JitterMs, q.LossPct, q.NetType, level = q.Level.ToString() });
            var before = _qualityLevel;
            _qualityLevel = Quality.Settled(_quality, _qualityLevel);
            ShowQuality(q);
            if (_qualityLevel != before) OnQualityChanged(before, q);
        }
        finally { _measuring = false; }
    }

    /// <summary>After Change IP the phone may be on another cell: start over.</summary>
    void ResetQuality()
    {
        _quality.Clear();
        _qualityLevel = QualityLevel.Unknown;
        _qualityAt = DateTime.Now.AddSeconds(-20);   // first sample 10 s after the change, once the connection has settled
        ShowQuality(null);
    }

    void ShowQuality(LinkQuality q)
    {
        _cQuality.Waiting = _qualityLevel == QualityLevel.Fair;
        _cQuality.Ok = _qualityLevel switch { QualityLevel.Good => true, QualityLevel.Bad => false, _ => null };
        _cQuality.Detail = q == null ? "Measuring…" : _qualityLevel switch
        {
            QualityLevel.Good => "Good · " + q.Text,
            QualityLevel.Fair => "Fair · " + q.Text,
            QualityLevel.Bad => "Weak · " + q.Text,
            _ => q.Text,
        };
    }

    bool WholePcOn => _s.WholePc;

    void OnQualityChanged(QualityLevel before, LinkQuality q)
    {
        if (_qualityLevel == QualityLevel.Bad)
        {
            Log.Warn($"The phone connection is weak ({q.Text}). Expect lag in game. Change IP can land on another cell; moving the phone (near a window, off the PC case) also helps.");
            if (_s.AutoModeByQuality && WholePcOn && TryDropWholePc(q)) return;
            Toast($"Weak mobile connection: {q.Text}. Expect lag." + (WholePcOn ? " Game only would leave more of it for CS2." : ""), Tone.Error);
        }
        else if (before == QualityLevel.Bad)
        {
            Log.Ok($"The phone connection is fine again ({q.Text}).");
            if (_s.WholePcBeforeWeak != null) RestoreWholePc();
            else Toast($"Mobile connection is fine again: {q.Text}.", true);
        }
    }

    /// <summary>
    /// Bad connection with Whole PC on: the browser, Discord and downloads were sharing the weak link with CS2.
    /// Switches to Game only and remembers the mode to come back to. Not while CS2 is on a server outside your list:
    /// Game only would block that server.
    /// </summary>
    bool TryDropWholePc(LinkQuality q)
    {
        if (Steam.Cs2Running && _consoleServer != null && !_servers.Any(x => x.Key == _consoleServer))
        {
            Toast($"Weak mobile connection: {q.Text}. CS2 is on a server outside your list, so Whole PC stays on.", Tone.Error);
            return true;
        }
        _s.WholePcBeforeWeak = _s.Mode == "all" ? "all" : "game";
        _s.Mode = "servers";
        _s.GameMode = false;
        SaveSettings();
        ApplySettingsToUi();
        Log.Warn("Weak connection: switched to Game only, so CS2 doesn't share the phone with the rest of the PC. The app goes back to Whole PC when it's fine again (with CS2 closed). Turn this off in Settings > Protection.");
        Toast($"Weak mobile connection ({q.Text}): switched to Game only for now. The rest of the PC is back on your home network.", false);
        _ = SyncNow(force: true);
        return true;
    }

    /// <summary>Back to the Whole PC mode you had, only with CS2 closed: moving the PC's routes mid-match cuts connections.</summary>
    void RestoreWholePc()
    {
        if (Steam.Cs2Running) { Toast("Mobile connection is fine again. Whole PC comes back when you close CS2.", true); return; }
        var back = _s.WholePcBeforeWeak;
        _s.WholePcBeforeWeak = null;
        if (back == "all") _s.Mode = "all"; else _s.GameMode = true;
        SaveSettings();
        ApplySettingsToUi();
        Log.Info(back == "all" ? "Connection fine again: back to Whole PC, always." : "Connection fine again: back to Whole PC while CS2 is open.");
        Toast("Mobile connection is fine again: Whole PC is back on.", true);
        _ = SyncNow(force: true);
    }

    /// <summary>CS2 just closed: if Whole PC was waiting for that, it comes back now.</summary>
    void QualityOnCs2Closed()
    {
        if (_s.WholePcBeforeWeak != null && _qualityLevel != QualityLevel.Bad) RestoreWholePc();
    }
}
