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

// Protection
public partial class MainWindow
{
    async Task SyncNow(bool force = false)
    {
        if (_demo) return;
        if (_syncing) { _syncAgain = true; return; }
        _syncing = true;
        try
        {
            var ips = _servers.Select(x => x.Ip).Distinct().ToList();
            var legacyBefore = _s.LegacyCleaned;
            var cs2Before = _s.Cs2Path;
            var cs2Running = Steam.Cs2Running;
            if (cs2Running) WatchCs2Exit();
            if (cs2Running != _cs2WasRunning)
            {
                _cs2WasRunning = cs2Running;
                if (_s.GameMode) Log.Info(cs2Running ? "CS2 open: the whole PC goes through the phone." : "CS2 closed: the rest of the PC is back on the home network.");
                force = true;
            }
            var rep = await _guard.Sync(_s, ips, force, _distrust, cs2Running);
            if (legacyBefore != _s.LegacyCleaned || cs2Before != _s.Cs2Path) SaveSettings();
            ApplyReport(rep);
        }
        finally { _syncing = false; }
        if (_syncAgain) { _syncAgain = false; await SyncNow(); }
    }

    void ApplyReport(GuardReport rep)
    {
        if (rep.Error != null && rep.Adapters.Count == 0) { SetHero("bad", "Error applying protection", rep.Error); return; }
        var egressKey = rep.Egress == null ? null : $"{rep.Egress.Index}|{rep.Egress.LocalIp}";
        var egressChanged = egressKey != _lastEgressKey;
        if (egressChanged && _lastEgressKey != null)
        {
            if (rep.Egress == null) Log.Warn("The phone connection dropped. Kill switch on: servers and CS2 blocked.");
            else Log.Ok($"Phone connection: {rep.Egress.Name} ({rep.Egress.LocalIp}).");
        }
        _lastEgressKey = egressKey;
        _last = rep;

        // Alert when protection drops (with a sound if CS2 is open).
        var protectedNow = rep.Egress != null && rep.FirewallProblem == null && rep.KillSwitchOk;
        if (_wasProtected && !protectedNow)
        {
            if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
            Toast((rep.EgressProblem ?? "The phone connection dropped.") + " Servers and CS2 are now blocked: your IP can't leak.", false);
        }
        else if (!_wasProtected && protectedNow && _lastEgressKey != null && _everProtected)
            Toast("Protection is back.", true);
        if (protectedNow) _everProtected = true;
        _wasProtected = protectedNow;

        FillEgressCombo(rep.Adapters);

        foreach (var srv in _servers)
        {
            var ok = rep.RouteOk.TryGetValue(srv.Ip, out var r) && r && rep.KillSwitchOk;
            srv.Protected = rep.Egress == null ? (rep.KillSwitchOk ? null : false) : ok;
            srv.RouteText = rep.Egress == null ? (rep.KillSwitchOk ? "Blocked" : "Unprotected") : ok ? "Protected" : !rep.KillSwitchOk ? "No kill switch" : "Route failed";
        }

        TrackSteam(rep);
        DiagReport(rep);
        UpdateStatusUi();
        if (egressChanged) _ = RefreshPublicIps();
    }

    /// <summary>
    /// Logs how Steam reaches Valve and when it reconnects. A "No Steam logon" mid-game
    /// lines up with one of these lines in the Log (that's the proof of the cause).
    /// </summary>
    void TrackSteam(GuardReport rep)
    {
        var link = rep.Steam;
        var path = !link.Connected ? "off" : rep.SteamOnEgress == true ? "egress" : rep.SteamOnEgress == false ? "home" : "?";
        if (path != _steamPath)
        {
            if (path == "egress") Log.Ok($"Steam is connected to Valve through the phone ({rep.Egress?.LocalIp}).");
            else if (path == "home")
            {
                Log.Warn($"Steam is connected to Valve through another network ({string.Join(", ", link.LocalIps)}), not the phone connection. Restart Steam before playing.");
                if (_s.SteamOnEgress) Toast("Steam is on another network: restart it (or switch account) before playing, or you'll get kicked with \"No Steam logon\".", false);
            }
        }
        _steamPath = path;
    }

    void UpdateStatusUi()
    {
        var rep = _last;
        if (rep == null) return;
        var n = _servers.Select(s => s.Ip).Distinct().Count();
        var routesOk = rep.RouteOk.Values.Count(v => v);

        _cFirewall.Ok = rep.FirewallProblem == null;
        _cFirewall.Detail = rep.FirewallProblem ?? "On";
        _cKill.Ok = n == 0 ? null : rep.KillSwitchOk;
        _cKill.Detail = n == 0 ? "No servers in the list" : rep.KillSwitchOk ? (n == 1 ? "1 IP blocked on the home network" : $"{n} IPs blocked on the home network") : "Rule not confirmed";
        _cEgress.Ok = rep.Egress != null;
        _cEgress.Detail = rep.Egress != null
            ? $"{rep.Egress.Name} · {rep.Egress.LocalIp}" + (rep.Egress.IsWifi && rep.Egress.Ssid != null ? $" · {rep.Egress.Ssid}" : "")
            : rep.EgressProblem ?? "Connect the phone (USB cable or hotspot)";
        _cCs2.Ok = !_s.LockCs2 ? null : rep.Cs2LockOk;
        _cCs2.Detail = !_s.LockCs2 ? "Turned off in Settings"
            : rep.Cs2LockOk == null ? "Couldn't find cs2.exe (open CS2 once)"
            : rep.Cs2LockOk == true ? (rep.Egress != null ? "CS2 only goes out through the phone" : "CS2 blocked until you connect the phone") : "Rule not confirmed";
        _cRoutes.Ok = n == 0 ? null : rep.Egress != null && routesOk == n;
        _cRoutes.Detail = n == 0 ? "No servers in the list" : $"{routesOk} of {n} {(n == 1 ? "IP" : "IPs")} through the phone";
        var same = _egressPublic != null && _homePublic != null && _egressPublic == _homePublic;
        _cDiff.Ok = _egressPublic == null || _homePublic == null ? null : !same;
        _cDiff.Detail = _egressPublic == null ? "Waiting for the phone's IP" : _homePublic == null ? "No home network to compare" : same ? "Same IP: the phone is sharing your Wi-Fi" : "Different";
        _cSteam.Ok = Steam.IsRunning && _steamLog.Online == false ? false
            : !SteamReady ? null
            : !_s.SteamOnEgress || rep.Egress == null ? null
            // Over UDP the TCP table doesn't show the connection: the Steam log plus the Valve routes count instead.
            : !rep.Steam.Connected && Steam.IsRunning && _steamLog.Online == true ? rep.SteamRoutesOk
            : rep.SteamOnEgress;
        _cSteam.Detail = Steam.IsRunning && _steamLog.Online == false ? $"No Valve session since {_steamLog.LastDrop:HH:mm:ss}: joining now gets you kicked"
            : !SteamReady ? "Reconnecting after the IP change"
            : !_s.SteamOnEgress ? "Turned off in Settings"
            : rep.Egress == null ? "Waiting for the phone"
            : !rep.Steam.Connected && Steam.IsRunning && _steamLog.Online == true ? "Connected over UDP (the Valve route goes through the phone)"
            : !rep.Steam.Connected ? (Steam.IsRunning ? "Steam is not connected to Valve right now" : "Steam closed")
            : rep.SteamOnEgress == true ? (rep.SteamRoutesOk == false ? "Connected through the phone (Valve routes to be confirmed)" : "Connected through the phone, like CS2")
            : "Connected through the home network: restart Steam (or switch account) before playing";

        if (rep.EffectiveMode == "all")
        {
            _cPc.Ok = rep.Egress == null ? null : rep.FullModeOk;
            _cPc.Detail = rep.FullModeOk ? (_s.Mode == "all" ? "The whole PC goes out through the phone" : "CS2 open: the whole PC goes out through the phone") : "The whole PC hasn't switched over yet";
        }
        else
        {
            _cPc.Ok = rep.Egress == null ? null : !rep.PcDefaultIsEgress;
            _cPc.Detail = rep.PcDefaultIsEgress ? $"The whole PC is using {rep.PcDefaultName} (uses data)" : $"Home network: {rep.PcDefaultName}";
        }

        var wifiLock = !string.IsNullOrEmpty(_s.EgressSsid) ? $" · only valid on the hotspot \"{_s.EgressSsid}\"" : "";
        EgressDetail.Text = rep.Egress == null
            ? (rep.EgressProblem ?? "No phone connection. Plug it in with a USB cable or connect to the Wi-Fi hotspot.") + wifiLock
            : $"{rep.Egress.Kind} · local IP {rep.Egress.LocalIp} · gateway {rep.Egress.Gateway ?? "direct"}{wifiLock}";
        PcRouteText.Text = rep.EffectiveMode == "all"
            ? (_s.Mode == "all" ? "Everything, including Steam, goes out through the phone." : "While CS2 is open, the whole PC goes out through the phone.")
            : $"The rest of the PC stays on: {rep.PcDefaultName}";

        // Hero
        if (rep.FirewallProblem != null)
            SetHero("bad", "Windows Firewall is off", rep.FirewallProblem + ". Without the Firewall there is no kill switch; turn it on in Windows Security > Firewall.");
        else if (rep.Distrusted || same)
            SetHero("bad", "Your IP is the same as your home IP",
                "Servers and CS2 are blocked until the IP is different again. The phone is sharing your home Wi-Fi, or the PC went back to the router. Turn off the phone's Wi-Fi (Phone card).");
        else if (rep.Egress == null && rep.EgressProblem != null)
            SetHero("bad", "You left the phone's network", rep.EgressProblem + " Servers and CS2 are blocked until you are back on the hotspot: your IP can't leak.");
        else if (rep.Egress == null)
            SetHero(n > 0 && rep.KillSwitchOk ? "warn" : "idle", "Phone disconnected",
                n > 0 && rep.KillSwitchOk
                    ? "You're safe: the listed servers and CS2 are blocked until you connect the phone (USB cable or hotspot)."
                    : "Connect the phone with a USB cable or to the Wi-Fi hotspot.");
        else if (rep.EffectiveMode == "all" && rep.FullModeOk && (n == 0 || rep.KillSwitchOk))
            SetHero("good", _s.Mode == "all" ? "Protected · whole PC" : "Protected · whole PC with CS2", $"All traffic goes out through {rep.Egress.Name}. Any server sees only the phone's IP.");
        else if (n == 0)
            SetHero("idle", "Ready", "The phone is connected. Add servers on the Servers page, or pick \"Whole PC\" under What goes through the phone.");
        else if (rep.KillSwitchOk && routesOk == n)
            SetHero("good", "Protected", "Servers on your list only see the phone's IP. On your home network they are blocked.");
        else
            SetHero("warn", "Protection incomplete", !rep.KillSwitchOk ? "The Firewall block was not confirmed. Check the Log." : $"Only {routesOk} of {n} routes confirmed. Another VPN may be interfering.");

        SideIp.Text = _egressPublic ?? "";
        UpdatePhoneCheck();
        ServerCount.Text = _servers.Count == 0 ? "" : $"{_servers.Count} {(_servers.Count == 1 ? "server" : "servers")} on {n} {(n == 1 ? "IP" : "IPs")}";
        ServersEmpty.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryBtn.IsEnabled = CopyBtn.IsEnabled = ServerSearch.IsEnabled = _servers.Count > 0;
        ServerHeader.Visibility = _servers.Count == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    void SetHero(string kind, string title, string sub)
    {
        var (fg, bg, glyph, side) = kind switch
        {
            "good" => (Ui.Good, Ui.GoodSoft, "", "Protected"),
            "bad" => (Ui.Bad, Ui.BadSoft, "", "Attention"),
            "warn" => (Ui.Warn, Ui.WarnSoft, "", "Phone disconnected"),
            _ => (Ui.Muted, (Brush)FindResource("Surface3"), "", "Ready"),
        };
        HeroBadge.Background = bg;
        HeroIcon.Foreground = fg;
        HeroIcon.Text = glyph;
        HeroTitle.Text = title;
        HeroSub.Text = sub;
        SideDot.Fill = fg;
        // The top bar repeats the page state (only "Protected · …" gets shortened).
        SideState.Text = kind == "good" ? side : title;
    }

    void FillEgressCombo(List<Adapter> adapters)
    {
        var usable = adapters.Where(a => a.Up && a.LocalIp != null).ToList();
        var sig = string.Join(";", usable.Select(a => a.Id + a.LocalIp + a.Ssid)) + "|" + _s.EgressAdapterId + _s.EgressSsid;
        if ((string)EgressCombo.Tag == sig) return;
        _uiLock = true;
        try
        {
            EgressCombo.Tag = sig;
            EgressCombo.Items.Clear();
            EgressCombo.Items.Add(new ComboBoxItem { Content = "USB cable (automatic)", Tag = null });
            foreach (var a in usable.OrderByDescending(a => a.IsPhone).ThenByDescending(a => a.IsWifi))
            {
                var label = a.IsWifi
                    ? $"Wi-Fi hotspot · {(a.Id == _s.EgressAdapterId && _s.EgressSsid != null ? _s.EgressSsid : a.Ssid ?? a.Name)}"
                    : $"{a.Kind} · {a.Name}";
                EgressCombo.Items.Add(new ComboBoxItem
                {
                    Content = label, Tag = a.Id,
                    ToolTip = a.IsWifi
                        ? $"{a.Description}\nConnected now to: {a.Ssid ?? "no network"}.\nWhen you pick it, the app remembers this network: if the PC jumps to another one (e.g. the home router), everything is blocked."
                        : a.Description,
                });
            }
            if (_s.EgressAdapterId != null && !usable.Any(a => a.Id == _s.EgressAdapterId))
                EgressCombo.Items.Add(new ComboBoxItem { Content = _s.EgressSsid != null ? $"Hotspot · {_s.EgressSsid} (disconnected)" : "Chosen adapter (disconnected)", Tag = _s.EgressAdapterId });
            EgressCombo.SelectedItem = EgressCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.EgressAdapterId) ?? EgressCombo.Items[0];
        }
        finally { _uiLock = false; }
    }

    async void EgressCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiLock || EgressCombo.SelectedItem is not ComboBoxItem it) return;
        var id = (string)it.Tag;
        if (id == _s.EgressAdapterId) return;
        var a = _last?.Adapters.FirstOrDefault(x => x.Id == id);
        if (a?.IsWifi == true)
        {
            if (a.Ssid == null)
            {
                Toast("First connect the PC's Wi-Fi to the phone's hotspot, then pick it again.", false);
                FillEgressCombo(_last.Adapters);
                return;
            }
            if (!await Ask("Phone hotspot",
                    $"The PC's Wi-Fi is connected to \"{a.Ssid}\". Confirm this is the phone's network (hotspot).\n\n" +
                    "The app remembers this name: if the PC's Wi-Fi jumps to another network, like the home router, servers and CS2 get blocked.",
                    "It's the phone"))
            {
                FillEgressCombo(_last.Adapters);
                return;
            }
            _s.EgressSsid = a.Ssid;
        }
        else _s.EgressSsid = null;
        _s.EgressAdapterId = id;
        _distrust = false;
        SaveSettings();
        EgressCombo.Tag = null;
        Log.Info(id == null ? "Phone connection: phone over USB (automatic)." : a?.IsWifi == true ? $"Phone connection: hotspot \"{_s.EgressSsid}\" on {a.Name}." : $"Phone connection pinned: {it.Content}.");
        await SyncNow(force: true);
        _ = RefreshPublicIps();
    }

    async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_uiLock) return;
        var mode = ModeAll.IsChecked == true ? "all" : "servers";
        var game = ModeGame.IsChecked == true;
        if (mode == _s.Mode && (mode == "all" || game == _s.GameMode)) return;
        _s.Mode = mode;
        if (mode != "all") _s.GameMode = game;
        SaveSettings();
        Log.Info(mode == "all" ? "Whole PC, always: the whole PC goes out through the phone."
            : game ? "Whole PC while CS2 is open: the whole PC goes out through the phone while CS2 is open."
            : "Game only: the rest of the PC stays on the home network.");
        await SyncNow(force: true);
        _ = RefreshPublicIps();
    }

    async Task RefreshPublicIps()
    {
        if (_demo) return;
        _lastIpCheck = DateTime.Now;
        var rep = _last;
        // Measured through the candidate (even when it's no longer trusted) to know when it becomes safe again.
        var candidate = rep?.Candidate;
        // Home = the adapter Windows uses for the rest of the PC. If that adapter is the phone's (whole PC,
        // game mode, or only the hotspot exists), it can't be measured: the last confirmed home IP is shown.
        var homeIf = rep == null || rep.PcDefaultIsEgress || candidate?.Index == rep.PcDefaultIndex ? -1 : rep.PcDefaultIndex;

        var tE = Net.EgressPublicIp(candidate);
        var tH = Net.HomePublicIp(homeIf);
        string e = null, h = null;
        try { e = await tE; } catch { }
        try { h = await tH; } catch { }

        // 3rd barrier: phone connection IP equal to the home IP = it isn't using mobile data. Block everything.
        var homeRef = h ?? _s.LastHomeIp;
        var same = e != null && homeRef != null && e == homeRef;
        if (same != _distrust)
        {
            _distrust = same;
            if (same)
            {
                Log.Error($"The phone connection IP ({e}) is the same as your home IP. Servers and CS2 blocked.");
                if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
                Toast("The phone's IP is the same as your home IP. Servers and CS2 are now blocked.", false);
            }
            else Log.Ok("The phone connection IP is different from your home IP again.");
            _ = SyncNow(force: true);
        }

        if (e != _egressPublic && e != null && !same)
        {
            _egressInfo = await Net.Lookup(e);
            AddHistory(e, _egressInfo, "detetado");
        }
        if (h != null)
        {
            if (h != _homePublic) _homeInfo = await Net.Lookup(h);
            _homeCached = false;
            if (_s.LastHomeIp != h) { _s.LastHomeIp = h; SaveSettings(); }
        }
        else if (_s.LastHomeIp != null)
        {
            h = _s.LastHomeIp;
            _homeCached = true;
            if (h != _homePublic) _homeInfo = await Net.Lookup(h);
        }
        _egressPublic = e;
        _homePublic = h;
        DiagIps(e, h, same);
        ShowIps();
        UpdateStatusUi();
    }

    void ShowIps()
    {
        EgressIp.Text = _egressPublic ?? "—";
        EgressIsp.Text = _egressPublic == null ? (_last?.Egress == null ? "No phone connection" : "Getting the IP through the phone…")
            : (_egressInfo?.Short ?? "") + (_egressInfo?.Proxy == true ? "  ·  flagged as VPN/proxy" : "");
        HomeIp.Text = _homePublic == null ? "—" : _reveal ? _homePublic : Fmt.MaskIp(_homePublic);
        HomeIsp.Text = _homePublic == null ? "No home network detected"
            : (_homeInfo?.Short ?? "") + (_homeCached ? "  ·  last confirmed" : "");
        SideIp.Text = _egressPublic ?? "";
        CopyIpBtn.Visibility = _egressPublic == null ? Visibility.Collapsed : Visibility.Visible;
        RevealBtn.Visibility = _homePublic == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void AddHistory(string ip, Net.IpInfo info, string reason)
    {
        if (_history.Count > 0 && _history[0].Ip == ip) return;
        var h = new IpHistoryEntry { Time = DateTime.Now, Ip = ip, Isp = info?.Short, Reason = reason };
        _history.Insert(0, h);
        while (_history.Count > 30) _history.RemoveAt(_history.Count - 1);
        _s.IpHistory = _history.ToList();
        SaveSettings();
        UpdateHistoryEmpty();
    }

    void UpdateHistoryEmpty()
    {
        HistoryEmpty.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryBtn.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _s.IpHistory = new();
        SaveSettings();
        UpdateHistoryEmpty();
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        _reveal = !_reveal;
        RevealBtn.Content = _reveal ? "" : "";
        ShowIps();
    }

    void CopyEgressIp_Click(object sender, RoutedEventArgs e)
    {
        if (_egressPublic == null) return;
        Clipboard.SetText(_egressPublic);
        Toast("IP copied.", true);
    }

    async void LeakTest_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Test finished: everything protected.", true); return; }
        LeakBtn.IsEnabled = false;
        if (!_tick.IsEnabled) _tick.Start();
        ShowProgress("Testing: Firewall, kill switch, routes and public IP…");
        try
        {
            await SyncNow(force: true);
            await RefreshPublicIps();
            var bad = _checks.Where(c => c.Ok == false).Select(c => c.Title).ToList();
            if (bad.Count == 0)
            {
                Log.Ok("Leak test: everything confirmed.");
                Toast(_egressPublic != null ? $"No leaks. Servers see {_egressPublic}." : "No leaks: servers are blocked until you connect the phone.", true);
            }
            else
            {
                Log.Warn("Leak test failed on: " + string.Join(", ", bad));
                Toast("Warning: " + string.Join(", ", bad) + ". See Checks.", false);

            }
        }
        finally { HideProgress(); LeakBtn.IsEnabled = true; }
    }
}
