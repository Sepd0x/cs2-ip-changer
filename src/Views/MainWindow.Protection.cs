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
    /// <summary>A cs2.exe found somewhere new goes into the settings, with one Log line. True when the settings changed (the caller saves).</summary>
    public static bool NoteCs2(Settings s, string found)
    {
        if (found == null || found == s.Cs2Path) return false;
        s.Cs2Path = found;
        Log.Info("CS2 found at " + found);
        return true;
    }

    /// <summary>What a sync found that belongs in the settings (the Guard only reads them). True when they changed (the caller saves).</summary>
    public static bool FromSync(Settings s, GuardReport rep)
    {
        var changed = NoteCs2(s, rep.Cs2Found);
        if (rep.LegacyCleaned && !s.LegacyCleaned) { s.LegacyCleaned = true; changed = true; }
        return changed;
    }

    /// <summary>cs2.exe (Steam.FindCs2), on the window thread: a new place is saved at once.</summary>
    string Cs2Exe()
    {
        var found = Steam.FindCs2(_s.Cs2Path);
        if (NoteCs2(_s, found)) SaveSettings();
        return found;
    }

    /// <summary>CS2's console.log: next to the saved cs2.exe even if it is gone (CS2 may be updating), else next to the one found.</summary>
    string Cs2ConsoleLog() => Cs2Launch.ConsoleLog(_s.Cs2Path ?? Cs2Exe());

    /// <summary>Why the phone's connection isn't trusted now, from what the window knows (the Guard adds Unchecked itself).</summary>
    public static Distrust DistrustNow(bool sameAsHome, bool phoneWifiOn, bool noHome) =>
        sameAsHome ? Distrust.SameIp : phoneWifiOn ? Distrust.PhoneWifi : noHome ? Distrust.NoHome : Distrust.None;

    /// <summary>The phone's IP was measured, but there is a home network whose IP was never read: nothing to compare with yet.</summary>
    bool _noHome;

    /// <summary>The adapter Windows uses for the rest of the PC (the home connection in the no phone mode).</summary>
    Adapter HomeAdapter => _last?.Adapters.FirstOrDefault(a => a.Index == _last.PcDefaultIndex);

    /// <summary>The USB phone is on a Wi-Fi network, so it isn't trusted (the rule: PhoneLink.WifiOn).</summary>
    bool PhoneWifiOn => PhoneLink.WifiOn(_phone, PhoneLink.Exit(_last)?.IsWifi);

    TaskCompletionSource _syncRerun;
    bool _syncAgainForce;

    /// <summary>
    /// Applies protection now. If a sync is already running, this one runs right after it and the caller waits for it:
    /// a join checks the result, so it must not read the state from before (that showed "protection is not confirmed").
    /// </summary>
    async Task SyncNow(bool force = false)
    {
        if (_demo || _restored) return;
        if (_syncing)
        {
            _syncAgain = true;
            _syncAgainForce |= force;
            _syncRerun ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _syncRerun.Task;
            return;
        }
        _syncing = true;
        try
        {
            var ips = _servers.Select(x => x.Ip).Distinct().ToList();
            var cs2Running = Steam.Cs2Running;
            if (cs2Running) WatchCs2Exit();
            if (cs2Running != _cs2WasRunning)
            {
                _cs2WasRunning = cs2Running;
                if (_s.GameMode && !_s.NoPhone && _protOn) Log.Info(cs2Running ? "CS2 open: the whole PC goes through the phone." : "CS2 closed: the rest of the PC is back on the home network.");
                if (!cs2Running) QualityOnCs2Closed();
                force = true;
            }
            var rep = await _guard.Sync(_s, new SyncRequest(ips)
            {
                Force = force, Distrust = DistrustNow(_distrust, PhoneWifiOn, _noHome), Cs2Running = cs2Running, VerifiedKey = _verifiedKey, On = _protOn,
            });
            // Closing or Windows signing out: the Guard no longer writes, and its answer isn't shown
            if (!_restored && !_sessionEnding)
            {
                if (FromSync(_s, rep)) SaveSettings();
                ApplyReport(rep);
            }
        }
        finally { _syncing = false; }
        if (_syncAgain)
        {
            _syncAgain = false;
            var again = _syncAgainForce;
            _syncAgainForce = false;
            var waiting = _syncRerun;
            _syncRerun = null;
            try { await SyncNow(again); }
            finally { waiting?.TrySetResult(); }
        }
    }

    // ------------------------------------------------------------ Turn on, turn off (Guard.OnAtStart has the rules)

    // A command (4 nov.): the top bar says what it is doing while it connects the phone, and Cancel stops the wait for tethering
    async void TurnOn_Click(object sender, RoutedEventArgs e) => await RunCommand("Turn on protection", "turning on…", () => TurnProtectionOn(ask: true));

    /// <summary>
    /// Protection on: the kill switch, the routes and the CS2 lock (and Whole PC, by the mode). With no phone, your servers and CS2
    /// are blocked until it is connected; a phone already checked while protection was off is trusted at once.
    /// </summary>
    async Task<bool> TurnProtectionOn(bool ask)
    {
        if (_protOn) return true;
        if (_turningOff) { Toast("Protection is still being turned off. Try again in a moment.", false); return false; }
        if (_s.NoPhone) { Toast("No phone mode is on: pick Game only or Whole PC under What goes through the phone first.", false); return false; }
        // CS2 open on the home network: the CS2 lock (or Whole PC) moves it to the phone, or blocks it with no phone. A match drops.
        if (ask && !_demo && Steam.Cs2Running && !await Ask("CS2 is open", TurnOnCs2Open(_s.LockCs2 && PhoneKnown), "Turn on protection"))
            return false;
        _protOn = true;
        Log.Ok("Protection turned on.");
        if (_demo) { DemoProtection(true); return true; }
        await SyncNow(force: true);
        _ = RefreshPublicIps();
        await ConnectPhoneAfterTurnOn(openSetup: ask);
        return true;
    }

    /// <summary>
    /// The question of Turn on protection with CS2 open. "Or blocks it" only when the CS2 lock is armed with no phone: that needs the
    /// option on and a phone checked once on this PC (Guard), or CS2 keeps going out from home.
    /// </summary>
    public static string TurnOnCs2Open(bool cs2Blocks) =>
        "Turning protection on moves CS2 to the phone" + (cs2Blocks ? ", or blocks it until the phone is connected" : "") + ". If you are in a match, you will drop.";

    public enum TurnOnStep { None, Tether, WifiThenTether, Setup }

    /// <summary>
    /// What Turn on protection does next when the phone isn't connected yet (4 nov.: "I clicked Turn on protection and nothing
    /// happened", tethering was off). A phone ADB sees gets USB tethering turned on by the app, like Phone setup does (Wi-Fi off
    /// first, or tethering would share the home Wi-Fi). Anything else opens Phone setup, which says what is missing.
    /// </summary>
    public static TurnOnStep NextAfterTurnOn(bool connected, bool hotspotSaved, bool phoneReady, bool phoneWifiOn) =>
        connected ? TurnOnStep.None
        : hotspotSaved ? TurnOnStep.Setup
        : phoneReady ? (phoneWifiOn ? TurnOnStep.WifiThenTether : TurnOnStep.Tether)
        : TurnOnStep.Setup;

    async Task ConnectPhoneAfterTurnOn(bool openSetup)
    {
        try
        {
            if (_last?.Candidate != null) return;
            await RefreshPhone();
            var next = NextAfterTurnOn(_last?.Candidate != null, PhoneLink.IsHotspot(_s.EgressSsid, PhoneLink.Exit(_last)?.IsWifi), _phone?.Ready == true, PhoneWifiOn);
            if (next == TurnOnStep.None) return;
            if (next is TurnOnStep.Tether or TurnOnStep.WifiThenTether)
            {
                Toast("Protection is on. Connecting the phone: turning on USB tethering…", Tone.Info);
                ShowProgress("Connecting the phone: turning on USB tethering…");
                Step("turning on USB tethering on the phone…");
                using var wait = _command != null ? CommandWait() : null;
                try
                {
                    if (next == TurnOnStep.WifiThenTether) { await TurnPhoneWifiOff(); await Task.Delay(2000); }
                    var (adapter, why) = await TurnOnTethering(t => ProgressText.Text = t);
                    if (CommandCancelled) return;   // Cancel at the top: protection stays on, the phone part stops here
                    if (adapter != null) { Toast($"Phone connected ({adapter.Name}). The app checks its IP, then servers see it.", true); return; }
                    Log.Warn("Turn on protection: the phone didn't connect by itself. " + why);
                }
                finally { HideProgress(); }
            }
            // Protection is on and blocks servers until the phone is there: say it, and show the steps instead of leaving it silent.
            Toast($"Protection is on, but the phone isn't connected yet: {Blocked(_last, lower: true)} stay blocked. Phone setup shows what's missing.", false);
            if (openSetup && SetupPane.Visibility != Visibility.Visible) await RunPhoneSetup();
        }
        catch (Exception ex) { Log.Warn("Connecting the phone after Turn on protection: " + ex.Message); }
    }

    /// <summary>The Turn on / Turn off / leak test buttons follow the state; the main button is picked in UpdateRejoinBtn (StatusPrimary).</summary>
    void UpdateProtectionButtons()
    {
        var canOn = !_protOn && !_s.NoPhone;
        TurnOnBtn.Visibility = canOn ? Visibility.Visible : Visibility.Collapsed;
        TurnOffBtn.Visibility = _protOn && !_s.NoPhone ? Visibility.Visible : Visibility.Collapsed;
        LeakBtn.Visibility = _protOn && !_s.NoPhone ? Visibility.Visible : Visibility.Collapsed;
        UpdateRejoinBtn();
        UpdateQuickInfo();     // New IP + next account says whether the IP changes with protection as it is now
        RefreshChecksView();   // the Checks summary says when protection is off
    }

    /// <summary>
    /// An IP change runs (Android or the iPhone's guided one) until its new IP was checked and synced: the connection drops on
    /// purpose meanwhile, so it is no drop for the alarm or the cable hint. Unlike _changingIp, trust is given during it.
    /// </summary>
    bool _ipChangeQuiet;

    /// <summary>The phone's protection went away: the alarm (toast, and the sound with CS2 open). Never for an IP change.</summary>
    public static bool DropAlarm(bool wasProtected, bool protectedNow, bool changingIp) => wasProtected && !protectedNow && !changingIp;

    /// <summary>The trusted connection stays trusted only while the report still sees that same connection.</summary>
    public static string TrustAfter(string verifiedKey, string candidateKey) => candidateKey != null && candidateKey == verifiedKey ? verifiedKey : null;

    void ApplyReport(GuardReport rep)
    {
        if (rep.Error != null && rep.Adapters.Count == 0) { SetHero("bad", "Error applying protection", rep.Error); return; }
        var egressKey = Guard.KeyOf(rep.Egress);
        var egressChanged = egressKey != _lastEgressKey;
        var home = rep.Unprotected;   // No phone, or protection off: nothing is blocked, so a drop is no drop
        // Change IP (Android or the iPhone's guided one) drops the connection on purpose: no "dropped", no alarm, no "Protection is back"
        var quiet = _changingIp || _ipChangeQuiet;
        if (egressChanged && _lastEgressKey != null && !home && !quiet)
        {
            if (rep.Egress == null) Log.Warn("The phone connection dropped. " + (rep.KillSwitchOk ? $"Kill switch on: {Blocked(rep, lower: true)} blocked." : "The block isn't confirmed."));
            else Log.Ok($"Phone connection: {rep.Egress.Name} ({rep.Egress.LocalIp}).");
        }
        _lastEgressKey = egressKey;
        _last = rep;
        UpdateVpnCheck(rep);
        // A connection that went away (or changed) loses its trust: back with the same adapter and DHCP lease, it is measured again
        // before CS2 traffic is let through it.
        _verifiedKey = TrustAfter(_verifiedKey, rep.CandidateKey);

        // Alert when protection drops (with a sound if CS2 is open).
        var protectedNow = rep.Egress != null && rep.FirewallProblem == null && rep.KillSwitchOk;
        // No phone mode is your choice, not a drop: no alarm, and none when you go back to the phone either.
        if (home) { _wasProtected = false; _everProtected = false; }
        // Mid-change _wasProtected keeps what it was before: back with the new IP is no news, and not back after it, the alarm comes then
        else if (quiet) { }
        else if (DropAlarm(_wasProtected, protectedNow, quiet))
        {
            if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
            var safe = rep.KillSwitchOk && rep.FirewallProblem == null;
            Toast(DropToast(rep.EgressProblem, safe, Cs2Locked(rep)), safe ? Tone.Warn : Tone.Error);
        }
        else if (!_wasProtected && protectedNow && _lastEgressKey != null && _everProtected)
            Toast("Protection is back.", true);
        if (protectedNow) _everProtected = true;
        if (!quiet) _wasProtected = protectedNow;

        FillEgressCombo(rep.Adapters);

        foreach (var srv in _servers)
        {
            if (home) { srv.Protected = null; srv.RouteText = rep.EffectiveMode == "off" ? "Protection off" : "Home IP"; continue; }
            var ok = rep.RouteOk.TryGetValue(srv.Ip, out var r) && r && rep.KillSwitchOk;
            srv.Protected = rep.Egress == null ? (rep.KillSwitchOk ? null : false) : ok;
            srv.RouteText = rep.Egress == null ? (rep.KillSwitchOk ? "Blocked" : "Unprotected") : ok ? "Protected" : !rep.KillSwitchOk ? "No kill switch" : "Route failed";
        }

        TrackSteam(rep);
        DiagReport(rep);
        UpdateStatusUi();
        if (egressChanged || rep.CandidateKey != _measuredKey) _ = RefreshPublicIps();
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
        TrackPhoneDrop(rep);
        var n = _servers.Select(s => s.Ip).Distinct().Count();
        var routesOk = rep.RouteOk.Values.Count(v => v);
        if (rep.Unprotected) { HomeStatusUi(rep, n); return; }

        _cFirewall.Ok = rep.FirewallProblem == null;
        _cFirewall.Detail = rep.FirewallProblem ?? "On";
        _cKill.Ok = n == 0 ? null : rep.KillSwitchOk;
        _cKill.Detail = n == 0 ? "No servers added yet" : rep.KillSwitchOk ? (n == 1 ? "1 IP blocked on the home network" : $"{n} IPs blocked on the home network") : "Rule not confirmed";
        var waiting = rep.Egress == null && rep.KillSwitchOk && rep.FirewallProblem == null;
        _cEgress.Ok = rep.Egress != null ? true : waiting || n == 0 ? null : false;
        _cEgress.Waiting = waiting;
        _cEgress.Detail = rep.Egress != null
            ? $"{(rep.Egress.IsPhone || rep.Egress.IsUsbNet || rep.Egress.IsWifi ? rep.Egress.Kind : rep.Egress.Name)} · {rep.Egress.LocalIp}" + (rep.Egress.IsWifi && rep.Egress.Ssid != null ? $" · {rep.Egress.Ssid}" : "")
            : rep.Distrust == Distrust.Unchecked ? $"{rep.Candidate?.Name ?? "Phone"}: checking its public IP…"
            : rep.Distrust == Distrust.PhoneWifi ? "Blocked while the phone's Wi-Fi is on"
            : rep.Distrust == Distrust.SameIp ? "Blocked: same IP as your home IP"
            : rep.Distrust == Distrust.NoHome ? "Blocked: your home IP hasn't been measured yet"
            : rep.EgressProblem ?? "Waiting for the phone";
        // The square comes from the same branch as the text: never green next to "Not locked yet". Amber, not red: on a first run
        // that is the normal state, and red would fail the leak test before the phone was ever connected.
        var notLockedYet = _s.LockCs2 && !PhoneKnown && rep.Egress == null;
        _cCs2.Ok = !_s.LockCs2 || notLockedYet ? null : rep.Cs2LockOk;
        _cCs2.Waiting = notLockedYet;
        _cCs2.Detail = !_s.LockCs2 ? "Turned off in Settings"
            : notLockedYet ? Cs2NotLockedYet
            : rep.Cs2LockOk == null ? "Couldn't find cs2.exe (open CS2 once)"
            : rep.Cs2LockOk == true ? (rep.Egress != null ? "CS2 only goes out through the phone" : "CS2 blocked until you connect the phone") : "Rule not confirmed";
        _cRoutes.Ok = n == 0 || waiting ? null : rep.Egress != null && routesOk == n;
        _cRoutes.Waiting = n > 0 && waiting;
        _cRoutes.Detail = n == 0 ? "No servers added yet" : waiting ? "Waiting for the phone (safe)" : $"{routesOk} of {n}";
        var same = _egressPublic != null && _homePublic != null && _egressPublic == _homePublic;
        _cDiff.Ok = _egressPublic == null || _homePublic == null ? null : !same;
        _cDiff.Detail = _egressPublic == null ? "Waiting for the phone's IP" : _homePublic == null ? "No home network to compare" : same ? "Same IP: the phone is sharing your Wi-Fi" : "Yes";
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
            : !rep.Steam.Connected ? (Steam.IsRunning ? "Steam not connected to Valve" : "Steam closed")
            : rep.SteamOnEgress == true ? (rep.SteamRoutesOk == false ? "Connected through the phone (Valve routes not confirmed yet)" : "Connected through the phone, like CS2")
            : "Connected through the home network: restart Steam (or switch account) before playing";

        if (rep.EffectiveMode == "all")
        {
            _cPc.Ok = rep.Egress == null ? null : rep.FullModeOk;
            _cPc.Detail = rep.FullModeOk ? (_s.Mode == "all" ? "Whole PC, DNS and IPv6 through the phone (IPv6 blocked)" : "CS2 open: whole PC, DNS and IPv6 through the phone (IPv6 blocked)")
                : rep.Egress == null ? "Waiting for the phone" : FullModeProblem(rep);
        }
        else (_cPc.Ok, _cPc.Detail) = RestOfPc(rep);

        // Under the dropdown: only what the Status text, the Phone box and Checks don't already say.
        // A specific adapter problem, and the note that explains why a hotspot lock blocks other networks.
        var egressNote = rep.Egress == null && rep.Candidate == null ? rep.EgressProblem : null;
        var wifiLock = !string.IsNullOrEmpty(_s.EgressSsid)
            ? $"{(egressNote == null ? "Only" : "only")} valid on the hotspot \"{_s.EgressSsid}\""
            : null;
        var egressLine = string.Join(" · ", new[] { egressNote, wifiLock }.Where(s => !string.IsNullOrEmpty(s)));
        EgressDetail.Text = egressLine;
        EgressDetail.Visibility = egressLine.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PcRouteText.Text = rep.EffectiveMode == "all"
            ? (_s.Mode == "all" ? "Everything, including Steam, goes out through the phone." : "While CS2 is open, the whole PC goes out through the phone.")
            : RestOfPc(rep).Detail;

        // Hero
        var held = n == 0 || rep.KillSwitchOk ? "warn" : "bad";
        var cs2Off = _s.LockCs2 && rep.Cs2LockOk == false;
        if (rep.FirewallProblem != null)
            SetHero("bad", "Windows Firewall is off", rep.FirewallProblem + ". " + FirewallHint("there is no kill switch"));
        else if (rep.Distrust == Distrust.Unchecked)
            SetHero(held, "Checking the phone's IP…", $"{Blocked(rep)} stay blocked until the app confirms the phone's public IP is not your home IP.");
        else if (rep.Distrust == Distrust.NoHome)
            SetHero(held, "Home IP not known yet", NoHomeSub(Blocked(rep), VpnIsDefault(rep)));
        else if (rep.Distrust == Distrust.PhoneWifi && !same)
            SetHero(held, "Phone Wi-Fi is on",
                $"{Blocked(rep)} are blocked. With Wi-Fi on, the phone may share your home network instead of mobile data: turn it off under Phone.");
        else if (rep.Distrusted || same)
            SetHero(held, "Your IP is the same as your home IP",
                $"{Blocked(rep)} are blocked until the IP is different. The phone is sharing your home Wi-Fi, or the PC went back to the router. Turn off the phone's Wi-Fi under Phone.");
        else if (rep.Egress == null && !PhoneKnown)
            // With protection on, the title says what it waits for (beside Turn off protection, "Not protected yet" read as a contradiction)
            SetHero("warn", _protOn ? "Waiting for the phone" : "Not protected yet", WaitingSub(_protOn, _s.LockCs2));
        else if (rep.Egress == null && rep.EgressProblem != null)
            SetHero(held, "You left the phone's network", rep.EgressProblem + $" {Blocked(rep)} are blocked until you are back on the hotspot.");
        else if (rep.Egress == null)
            SetHero(n == 0 ? "idle" : held, "Phone disconnected",
                n == 0 ? (string.IsNullOrEmpty(_s.EgressSsid)
                    ? "Plug in your phone with USB and turn on USB tethering. First time? Press Phone setup."
                    : "Connect the phone to its Wi-Fi hotspot.")
                : rep.KillSwitchOk ? DisconnectedSafe(Cs2Locked(rep))
                    + (rep.EffectiveMode == "servers" ? " The rest of the PC works as usual." : "")
                : "The block on your servers isn't confirmed, so they could see your home IP. Don't join until Checks are green.");
        else if (rep.EffectiveMode == "all" && rep.FullModeOk && (n == 0 || rep.KillSwitchOk) && !cs2Off)
            SetHero("good", _s.Mode == "all" ? "Protected · whole PC" : "Protected · whole PC (CS2 open)", "The PC's traffic and DNS go out through the phone, and IPv6 is blocked. CS2 and your servers only see the phone's IP.");
        else if (rep.EffectiveMode == "all" && !rep.FullModeOk)
            SetHero(held, "Whole PC hasn't switched over", FullModeProblem(rep) + "." + (n > 0 && rep.KillSwitchOk ? " Servers on your Servers page are still protected." : "") + " Check the Log.");
        else if (n == 0)
            SetHero("idle", "Ready", _s.GameMode
                ? "The phone is connected. While CS2 is open, the whole PC goes through the phone and every server sees its IP."
                : "The phone is connected. Add servers on the Servers page, or pick \"Whole PC\" under What goes through the phone.");
        else if (rep.KillSwitchOk && routesOk == n && !cs2Off)
            SetHero("good", "Protected", ProtectedText(rep));
        else if (!rep.KillSwitchOk)
            SetHero("bad", "Protection incomplete", "The firewall block was not confirmed, so servers on your Servers page could see your home IP. Check the Log.");
        else
            SetHero("warn", "Protection incomplete", cs2Off ? "Your servers are protected, but the CS2 rule was not confirmed. Check the Log." : $"Only {routesOk} of {n} routes confirmed. Another VPN may be interfering.");

        ShowIps();
        UpdatePhoneCheck();
        PhoneNotInUse();
        UpdateProtectionButtons();
        ServerCount.Text = _servers.Count == 0 ? "" : $"{_servers.Count} {(_servers.Count == 1 ? "server" : "servers")} on {n} {(n == 1 ? "IP" : "IPs")}";
        ServersEmpty.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryBtn.IsEnabled = CopyBtn.IsEnabled = ServerSearch.IsEnabled = _servers.Count > 0;
        ServerHeader.Visibility = _servers.Count == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>
    /// Something the app put in place would stay after it closes: the block on your servers or the CS2 lock. With protection off
    /// and in No phone mode there is nothing to turn off.
    /// </summary>
    bool ProtectionArmed => !_restored && _protOn && _last != null && !_last.Unprotected
        && ((_servers.Count > 0 && _last.KillSwitchOk) || (_s.LockCs2 && _last.Cs2LockOk == true));

    /// <summary>No phone mode with the phone plugged in: the Phone box says it isn't used instead of "Ready to change IP".</summary>
    void PhoneNotInUse()
    {
        if (_s.NoPhone && _phone?.Ready == true) SetPhone(Ui.Faint, "Phone not in use", _phone.Summary + " · No phone mode is on");
    }

    /// <summary>
    /// Where the rest of the PC goes (game only and no phone), named for what the adapter is: the phone's adapter (protected, still
    /// being checked, or any phone adapter Windows has) is never called the home network.
    /// </summary>
    public static (bool? Ok, string Detail) RestOfPc(GuardReport rep)
    {
        var pc = rep.Adapters.FirstOrDefault(a => a.Index == rep.PcDefaultIndex);
        if (pc == null && rep.PcDefaultName is null or "—") return (null, "No internet connection right now");
        var name = pc?.Name ?? rep.PcDefaultName;
        if (PcOnPhone(rep)) return (rep.Unprotected ? null : false, $"The whole PC is using the phone ({name}): mobile data");
        return (rep.Unprotected || rep.Egress == null ? null : true, $"Home network: {name}");
    }

    /// <summary>
    /// The adapter Windows uses for the rest of the PC is the phone's (HomeIsPhone), and there is an internet connection at all:
    /// RestOfPc, Status with protection off, the IP box and Change IP read it.
    /// </summary>
    public static bool PcOnPhone(GuardReport rep) =>
        !(rep.PcDefaultName is null or "—" && !rep.Adapters.Any(a => a.Index == rep.PcDefaultIndex)) && HomeIsPhone(rep);

    /// <summary>Protection off or No phone: whether anything is blocked. The Regions block stays in every state.</summary>
    public static string NothingBlocked(int blockedRegions) => blockedRegions > 0 ? "only the regions you turned off are blocked" : "nothing is blocked";

    /// <summary>
    /// Protection off or No phone: what servers see. Usually the home IP; with the whole PC on the phone (no other connection, or
    /// Windows picked it) they see the phone's, but nothing holds the home IP back if it drops.
    /// </summary>
    public static string OffSeen(bool pcOnPhone, int blockedRegions) => pcOnPhone
        ? $"The whole PC is using the phone, so CS2 and every server see its IP, but if the phone drops they see your home IP: {NothingBlocked(blockedRegions)}."
        : $"CS2 and every server see your home IP, and {NothingBlocked(blockedRegions)}.";

    /// <summary>
    /// The Phone connection row with protection off (always grey): not in use, and whether the phone is ready for when it is on.
    /// <paramref name="kind"/> null = no phone connection.
    /// </summary>
    public static string OffPhoneRow(string kind, string localIp, bool ready, Distrust distrust) =>
        kind == null ? "Not in use: protection is off"
        : ready ? $"Ready ({kind} · {localIp}): not in use while protection is off"
        : distrust == Distrust.Unchecked ? $"{kind} · {localIp} · checking its IP…"
        : distrust != Distrust.None ? $"Not in use: protection is off. {DistrustText.Of(distrust)}"
        : $"{kind} · {localIp} · not in use: protection is off";

    /// <summary>The Routes row with protection off: servers, not IPs (two servers can share one IP).</summary>
    public static string RoutesOffText(int servers) => servers == 1 ? "Off: 1 server sees your home IP" : $"Off: {servers} servers see your home IP";

    /// <summary>
    /// Protection off, or No phone mode: nothing is protected, and the page says so plainly. With protection off the phone is still
    /// checked, so Status says whether it is ready for when you turn protection on.
    /// </summary>
    void HomeStatusUi(GuardReport rep, int n)
    {
        var off = rep.EffectiveMode == "off";
        var Off = off ? "Off: protection is off" : "Off: no phone mode";
        _cFirewall.Ok = rep.FirewallProblem == null;
        _cFirewall.Detail = rep.FirewallProblem ?? "On (it enforces the Regions block)";
        foreach (var c in new[] { _cKill, _cEgress, _cRoutes, _cDiff, _cQuality, _cCs2, _cSteam })
        {
            c.Ok = null;
            c.Waiting = false;
            c.Detail = Off;
        }
        _cRoutes.Detail = n == 0 ? Off : RoutesOffText(_servers.Count);
        var phoneReady = PhoneReadyOff(rep);
        if (off)
        {
            // The phone, as it would be used: checked here so Turn on protection trusts it at once. Grey with protection off (6 nov.,
            // red there didn't make sense): red is only for a problem while protection is on. The text says how it stands.
            _cEgress.Ok = null;
            _cEgress.Waiting = false;
            _cEgress.Detail = OffPhoneRow(rep.Candidate?.Kind, rep.Candidate?.LocalIp, phoneReady, rep.Distrust);
            var same = _egressPublic != null && _homePublic != null && _egressPublic == _homePublic;
            _cDiff.Ok = _egressPublic == null || _homePublic == null || same ? null : true;
            _cDiff.Detail = _egressPublic == null ? "Waiting for the phone's IP" : _homePublic == null ? "No home network to compare" : same ? "Same IP: the phone is sharing your Wi-Fi" : "Yes";
        }
        _cPc.Ok = null;
        _cPc.Detail = RestOfPc(rep).Detail;
        EgressDetail.Text = "";
        EgressDetail.Visibility = Visibility.Collapsed;
        var onPhone = PcOnPhone(rep);
        PcRouteText.Text = onPhone ? RestOfPc(rep).Detail : $"Everything stays on: {rep.PcDefaultName}";
        var regions = _s.BlockedRegions?.Count ?? 0;
        if (rep.FirewallProblem != null)
            SetHero("bad", "Windows Firewall is off", rep.FirewallProblem + ". " + FirewallHint("the Regions block does nothing"));
        else if (off)
            SetHero(OffHeroKind(_s.UseWithProtectionOff), OffHeroTitle(_s.UseWithProtectionOff),
                (_s.UseWithProtectionOff ? "Use the app with protection off is on: Servers, Join and Log in use your home connection. " : "")
                + OffSeen(onPhone, regions) + " Accounts, Configs, Profiles and Regions work as usual. "
                + (phoneReady ? $"The phone is ready ({_egressPublic}): turn protection on to play through it."
                    : rep.Candidate != null ? "The phone is connected: its IP is being checked."
                    : "To play through the phone, connect it, then turn protection on."));
        else
            SetHero("warn", "Not protected · no phone",
                "You picked No phone. " + OffSeen(onPhone, regions) + " Accounts, Configs, Profiles and Regions work as usual. To protect again, pick another option under What goes through the phone, then turn protection on.");
        ShowIps();
        UpdatePhoneCheck();
        PhoneNotInUse();
        UpdateProtectionButtons();
        ServerCount.Text = _servers.Count == 0 ? "" : $"{_servers.Count} {(_servers.Count == 1 ? "server" : "servers")} on {n} {(n == 1 ? "IP" : "IPs")}";
        ServersEmpty.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryBtn.IsEnabled = CopyBtn.IsEnabled = ServerSearch.IsEnabled = _servers.Count > 0;
        ServerHeader.Visibility = _servers.Count == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>
    /// Protection off. With "Use the app with protection off" on, the app joins servers on the home connection, so the top bar and
    /// the Status box say it in red and keep saying it: every server sees the home IP. Without it, a quiet grey "Protection off".
    /// </summary>
    public static string OffHeroKind(bool useWithOff) => useWithOff ? "bad" : "idle";
    public static string OffHeroTitle(bool useWithOff) => useWithOff ? "Home IP: servers see it" : "Protection off";

    /// <summary>Whole PC mode: what isn't confirmed yet (routes, DNS or IPv6).</summary>
    static string FullModeProblem(GuardReport rep)
    {
        var left = new List<string>();
        if (!rep.FullRoutesOk) left.Add($"the rest of the PC is still on {rep.PcDefaultName}");
        if (rep.DnsOk != true) left.Add("DNS isn't confirmed through the phone");
        if (rep.Ipv6Ok != true) left.Add("the IPv6 block isn't confirmed");
        var text = left.Count == 0 ? "Not confirmed yet" : string.Join(", ", left);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// A phone connection was checked on this PC. Until then the CS2 lock isn't armed (Guard), so nothing is protected yet.
    /// A phone connected now counts as seen too: the same rule as the Guard. An IP history from before beta.6 already set PhoneSeen
    /// at start (the settings migration); an IP measured this session without trust (home IP not known yet) doesn't arm the lock, so
    /// it doesn't count here either. The demo's made-up history still counts, as before.
    /// </summary>
    bool PhoneKnown => _s.PhoneSeen || _last?.Egress != null || (_demo && _history.Count > 0);

    /// <summary>The "Protected" line, for the mode you picked: Whole PC while CS2 is open covers every server, Game only your list.</summary>
    string ProtectedText(GuardReport rep)
    {
        var cs2Later = _s.LockCs2 && rep.Cs2LockOk == null;
        if (_s.GameMode)
            return "While CS2 is open, every server sees the phone's IP, not only your list. "
                + (cs2Later ? "CS2 itself gets locked once you open it." : $"If the phone drops, {Blocked(rep, lower: true)} are blocked.");
        return cs2Later
            ? "Servers on your Servers page only see the phone's IP. CS2 itself gets locked once you open it."
            : "Servers on your Servers page only see the phone's IP. If the phone disconnects, they are blocked.";
    }

    /// <summary>What the kill switch holds back right now: CS2 is only named when its own rule is on.</summary>
    string Blocked(GuardReport rep, bool lower = false) => BlockedText(Cs2Locked(rep), lower);

    /// <summary>The CS2 lock is on and confirmed: CS2 can't go out from home.</summary>
    bool Cs2Locked(GuardReport rep) => _s.LockCs2 && rep?.Cs2LockOk == true;

    public static string BlockedText(bool cs2Locked, bool lower = false) => cs2Locked ? (lower ? "servers" : "Servers") + " and CS2" : lower ? "your servers" : "Your servers";

    /// <summary>
    /// The Status line while no phone was ever checked on this PC. Until then the CS2 lock isn't armed (Guard), so with protection on
    /// only your servers wait for the phone: CS2 itself still reaches any other server from home.
    /// </summary>
    public static string WaitingSub(bool protOn, bool lockCs2) => protOn
        ? "Your servers wait for the phone, so your home IP can't reach them." + (lockCs2 ? " CS2 isn't locked until the app has checked a phone once: don't open it yet." : "") + " Press Phone setup to walk through it."
        : "If you open CS2 now, servers see your home IP. Set up the phone first: press Phone setup to walk through it.";

    /// <summary>The CS2 lock row of Checks before a phone was ever checked (amber: nothing is wrong, it just isn't armed yet).</summary>
    public const string Cs2NotLockedYet = "Not locked until a phone is checked once";

    /// <summary>The phone dropped with the block confirmed. "Your IP can't leak" only when CS2 is locked too: without it CS2 goes out from home.</summary>
    public static string DropToast(string problem, bool safe, bool cs2Locked) =>
        (problem ?? "The phone connection dropped.")
        + (!safe ? " The block isn't confirmed: don't join a server, check the Log."
            : $" {BlockedText(cs2Locked)} are now blocked" + (cs2Locked ? ": your IP can't leak." : "."));

    /// <summary>Status with the phone gone and the block confirmed: "You're safe" only when CS2 is locked too.</summary>
    public static string DisconnectedSafe(bool cs2Locked) =>
        (cs2Locked ? "You're safe: " + BlockedText(true, lower: true) : BlockedText(false)) + " wait for the phone (USB cable or Wi-Fi hotspot).";

    /// <summary>The leak test passed. With no phone it names only what is really blocked, and says when CS2 isn't locked yet.</summary>
    public static string LeakOkText(string phoneIp, string blockedLower, bool cs2NotLockedYet) =>
        phoneIp != null ? $"No leaks. Servers see {phoneIp}."
        : $"No leaks: {blockedLower} are blocked until you connect the phone." + (cs2NotLockedYet ? " CS2 isn't locked until the app has checked a phone once." : "");

    /// <summary>The Status box as the protection state wants it; an IP change in progress is drawn over it (ShowProgress).</summary>
    (string Kind, string Title, string Sub) _heroWanted = ("idle", "Checking the connection…", "One moment.");
    string _changingTitle;

    /// <summary>
    /// A new phone IP drops you from the server only when CS2 goes out through the phone now: protection on, or the whole PC on the
    /// phone with protection off (no home network, or Save mobile data off). Otherwise CS2 stays on the home connection.
    /// </summary>
    public static bool ChangeDrops(bool protectedNow, bool pcOnPhone) => protectedNow || pcOnPhone;

    bool IpChangeDrops => ChangeDrops(_protOn && _last?.Unprotected != true, _last != null && PcOnPhone(_last));

    /// <summary>The Status line while the IP changes.</summary>
    public static string ChangingSub(bool drops) => "Mobile data goes off and on until the carrier gives a new IP. "
        + (drops ? "If you are on a server, you will drop." : "Servers see it once protection is on.");

    void SetHero(string kind, string title, string sub)
    {
        _heroWanted = (kind, title, sub);
        if (kind == "good") Telemetry.ProtectedNow();
        if (_changingTitle != null)
            (kind, title, sub) = ("warn", _changingTitle, ChangingSub(IpChangeDrops));
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
        if (_tray != null) _tray.Tip = TrayTip(SideState.Text);
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
                        ? $"{a.Description}\nConnected now to: {a.Ssid ?? "no network"}.\nWhen you pick it, the app remembers this network: if the PC jumps to another one (e.g. the home router), your servers and CS2 are blocked while protection is on."
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
                Toast(a.SsidUnknown ? Net.SsidLocationText : "First connect the PC's Wi-Fi to the phone's hotspot, then pick it again.", false);
                FillEgressCombo(_last.Adapters);
                return;
            }
            if (!await Ask("Phone hotspot",
                    $"The PC's Wi-Fi is connected to \"{a.Ssid}\". Confirm this is the phone's network (hotspot).\n\n" +
                    "The app remembers this name: if the PC's Wi-Fi changes to another network, like the home router, servers and CS2 get blocked while protection is on.",
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
        var mode = ModeAll.IsChecked == true ? "all" : ModeHome.IsChecked == true ? "home" : "servers";
        var game = ModeGame.IsChecked == true;
        if (mode == _s.Mode && (mode != "servers" || game == _s.GameMode)) return;
        if (mode == "home" && !_demo && !await Ask("No phone", NoPhoneAsk(Steam.Cs2Running && _protOn), "Use my home connection"))
        {
            ApplySettingsToUi();
            return;
        }
        var wasHome = _s.NoPhone;
        // No phone protects nothing (Guard), so protection is off there for real, never hidden: leaving it doesn't turn it on by itself
        var protWas = _protOn;
        _protOn = ProtOnAfterMode(_protOn, mode);
        if (protWas && !_protOn) Log.Info("Protection is off: No phone mode.");
        _s.Mode = mode;
        if (mode == "servers") _s.GameMode = game;   // the no phone mode keeps the Whole PC choice for when you come back
        _s.WholePcBeforeWeak = null;   // your choice wins over a mode the app dropped because of a weak connection
        SaveSettings();
        if (wasHome) { _verifiedKey = null; _distrust = false; }   // back on the phone: its IP is checked again before anything goes through
        Log.Info(mode == "all" ? "Whole PC, always: the whole PC goes out through the phone."
            : mode == "home" ? "No phone: CS2 and servers use your home connection. Not protected."
            : game ? "Whole PC while CS2 is open: the whole PC goes out through the phone while CS2 is open."
            : "Game only: the rest of the PC stays on the home network.");
        UpdatePhoneCheck();
        await SyncNow(force: true);
        _ = RefreshPublicIps();
        _ = RefreshPhone();   // the Phone box says again whether the phone is in use
        ShowRegionsStatus();
        // Back from No phone: protection is off and shown as off, and one question offers to turn it on (with CS2 open, the one
        // Turn on protection asks, since a match drops)
        if (wasHome && !_s.NoPhone && !_protOn && !_demo)
        {
            if (await Ask("Turn on protection now?", LeaveNoPhoneAsk(Steam.Cs2Running, _s.LockCs2 && PhoneKnown), "Turn on", "Not now"))
                await RunCommand("Turn on protection", "turning on…", () => TurnProtectionOn(ask: false));
            else Log.Info("Protection is off. Turn it on in Status when you want to play through the phone.");
        }
    }

    /// <summary>The question before No phone. Protection goes off with it, and coming back doesn't turn it on by itself.</summary>
    public static string NoPhoneAsk(bool cs2OnPhoneNow) =>
        "CS2 and every server will see your home IP: no kill switch, no CS2 lock and no Change IP. Accounts, Configs, Profiles and Regions keep working.\n\n"
        + (cs2OnPhoneNow ? Cs2OpenHomeNow + "\n\n" : "")
        + "Protection turns off. To protect again, pick another option here, then turn protection on.";

    /// <summary>Leaving No phone: protection is off. With CS2 open the question is the one of Turn on protection (a match drops).</summary>
    public static string LeaveNoPhoneAsk(bool cs2Open, bool cs2Blocks) => cs2Open
        ? TurnOnCs2Open(cs2Blocks)
        : "Protection is off, so servers still see your home IP. Turn it on to play through the phone.";

    /// <summary>Protection after a mode is picked: No phone turns it off; any other mode leaves it as it is.</summary>
    public static bool ProtOnAfterMode(bool protOn, string mode) => mode != "home" && protOn;

    /// <summary>Checks running now. The 15 s tick waits for the last one: a slow check (weak phone, home endpoints timing out)
    /// was always overtaken by the next and never landed, so the IP stayed on "Checking" (4 nov.).</summary>
    int _ipRunning;
    /// <summary>The last check through the phone failed since then (null = the IP shown was confirmed by the last check).</summary>
    DateTime? _egressFailSince;
    int _egressFails;
    string _egressKey;
    bool _egressFailLogged;

    async Task RefreshPublicIps()
    {
        _ipRunning++;
        try { await RefreshPublicIpsCore(); }
        finally { _ipRunning--; }
    }

    /// <summary>
    /// What the box shows after a check. A failed check keeps the last IP of the same phone connection (faint, with since when
    /// it couldn't be confirmed) instead of going back to "—": a weak connection is worth saying, and the IP didn't change because
    /// one request failed. Another connection, or none, clears it. Trust never comes from this: only from a check that answered.
    /// </summary>
    public static (string Ip, bool Failed) EgressAfterCheck(string measured, string shown, string shownKey, string key) =>
        measured != null ? (measured, false)
        : key == null || shown == null || shownKey != key ? (null, false)
        : (shown, true);

    /// <summary>
    /// The adapter the home IP is measured through: the one Windows uses for the rest of the PC, unless that is the phone's, or a VPN
    /// (its IP isn't your home IP, so a phone sharing your home Wi-Fi would pass the check). -1 = not measured now: the last home IP counts.
    /// </summary>
    public static int HomeIf(GuardReport rep, Adapter candidate) =>
        rep == null || rep.PcDefaultIsEgress || candidate?.Index == rep.PcDefaultIndex || VpnIsDefault(rep) ? -1 : rep.PcDefaultIndex;

    /// <summary>The rest of the PC goes out through a VPN app's adapter.</summary>
    public static bool VpnIsDefault(GuardReport rep) => rep?.Adapters.Any(a => a.Index == rep.PcDefaultIndex && a.IsVpn) == true;

    /// <summary>Status while the home IP isn't known. Through a VPN it can't be read until the VPN is off for a moment.</summary>
    public static string NoHomeSub(string blocked, bool vpnDefault) => vpnDefault
        ? $"{blocked} stay blocked. Your PC goes out through a VPN app, so the app can't read your home IP: turn the VPN off for a moment so it can compare it with the phone's."
        : $"{blocked} stay blocked. Close CS2 (or pick Game only) for a moment so the app can read your home IP and compare it with the phone's.";

    async Task RefreshPublicIpsCore()
    {
        if (_demo) return;
        _lastIpCheck = DateTime.Now;
        var seq = ++_ipSeq;
        var rep = _last;
        // Measured through the candidate (even when it's no longer trusted) to know when it becomes safe again.
        var candidate = rep?.Candidate;
        // Home = the adapter Windows uses for the rest of the PC. If that adapter is the phone's (whole PC,
        // game mode, or only the hotspot exists), it can't be measured: the last confirmed home IP is shown.
        var homeIf = HomeIf(rep, candidate);

        var key = Guard.KeyOf(candidate);
        _measuredKey = key;
        var tE = Net.EgressPublicIp(candidate);
        var tH = Net.HomePublicIp(homeIf);
        string e = null, h = null;
        try { e = await tE; } catch { }
        try { h = await tH; } catch { }

        // A newer check started meanwhile (or the connection changed): this result is stale.
        if (seq != _ipSeq || key != Guard.KeyOf(_last?.Candidate)) return;

        // 3rd barrier: phone connection IP equal to the home IP = it isn't using mobile data. Block everything.
        // A failed measurement (e == null) changes nothing: it never lifts a block and never grants trust.
        var homeRef = h ?? _s.LastHomeIp;
        var same = e != null && homeRef != null && e == homeRef;
        // Protection off (or No phone): the phone is still checked for when protection is on, but nothing is blocked now
        var unprotected = rep?.Unprotected == true;
        if (e != null)
        {
            var changed = false;
            if (same != _distrust)
            {
                _distrust = same;
                changed = true;
                if (same && unprotected) Log.Warn(SameIpText(e, unprotected: true));
                else if (same)
                {
                    Log.Error(SameIpText(e, unprotected: false));
                    if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
                    _guard.Problems.SameIpWritten();   // the Guard's own "Servers blocked." line would say it again
                    Toast("The phone's IP is the same as your home IP. Servers and CS2 are now blocked.", false);
                }
                else Log.Ok("The phone connection IP is different from your home IP again.");
            }
            // With a home network whose IP was never read (CS2 already open, whole PC mode), there is nothing to compare
            // with: no trust until it is measured. Without a home network at all (only the hotspot), there is no home IP to leak.
            var noHome = !same && homeRef == null && rep?.HasHome == true;
            if (noHome != _noHome)
            {
                _noHome = noHome;
                changed = true;
                if (noHome) Log.Warn(NoHomeText(unprotected));
            }
            // Mid-change the phone may still show the old IP: trust only comes from the check after the change.
            var verified = same || noHome || _changingIp ? null : key;
            if (verified != _verifiedKey)
            {
                if (verified != null && !changed) Log.Ok(CheckedText(e, unprotected));
                _verifiedKey = verified;
                changed = true;
                if (verified != null && !_s.PhoneSeen) { _s.PhoneSeen = true; SaveSettings(); }
            }
            if (changed) _ = SyncNow(force: true);
        }

        if (e != _egressPublic && e != null && !same)
        {
            _egressInfo = await Net.Lookup(e, candidate?.Index ?? -1, candidate?.LocalIp);
            AddHistory(e, _egressInfo, "detetado");
        }
        if (h != null)
        {
            if (LookUpHome(h, _homePublic, cached: false)) _homeInfo = await Net.Lookup(h, homeIf);
            _homeCached = false;
            if (_s.LastHomeIp != h) { _s.LastHomeIp = h; SaveSettings(); }
        }
        else if (_s.LastHomeIp != null)
        {
            h = _s.LastHomeIp;
            _homeCached = true;
            if (!LookUpHome(h, _homePublic, cached: true) && h != _homePublic) _homeInfo = null;
        }
        var (shownIp, failed) = EgressAfterCheck(e, _egressPublic, _egressKey, key);
        _egressPublic = shownIp;
        _egressKey = shownIp == null ? null : key;
        if (!failed)
        {
            if (_egressFailLogged && e != null) Log.Ok($"The phone's IP answered again: {e}.");
            _egressFailSince = null; _egressFails = 0; _egressFailLogged = false;
        }
        else
        {
            _egressFails++;
            _egressFailSince ??= DateTime.Now;
            if (!_egressFailLogged && DateTime.Now - _egressFailSince.Value > TimeSpan.FromSeconds(45))
            {
                _egressFailLogged = true;
                Log.Warn($"The phone's IP couldn't be checked since {_egressFailSince:HH:mm:ss} ({_egressFails} tries: {Net.LastEgressError ?? "no answer"}). "
                    + "The phone connection may be weak. Showing the last IP that answered.");
            }
        }
        if (h != _homePublic && HideLogins) { _homePublic = h; RemaskLog(); }
        _homePublic = h;
        DiagIps(e, h, same);
        ShowIps();
        UpdateStatusUi();
    }

    /// <summary>The phone's IP is the home IP. With protection off nothing is blocked: the Log says what to do before turning it on.</summary>
    public static string SameIpText(string ip, bool unprotected) => unprotected
        ? $"The phone's IP ({ip}) is the same as your home IP: it is sharing your Wi-Fi. Turn off the phone's Wi-Fi before you turn on protection."
        : $"The phone connection IP ({ip}) is the same as your home IP. Servers and CS2 blocked.";

    public static string NoHomeText(bool unprotected) => "The home IP hasn't been measured yet, so the phone's IP can't be compared with it."
        + (unprotected ? "" : " Servers and CS2 blocked until it is.");

    /// <summary>The phone's IP checked: servers see it only with protection on.</summary>
    public static string CheckedText(string ip, bool unprotected) => unprotected
        ? $"Phone connection checked: {ip}. Servers see it once protection is on."
        : $"Phone connection checked: servers see {ip}.";

    /// <summary>
    /// ipwho.is is only asked about a home IP measured just now, through the home connection. The last one saved (shown when the
    /// home IP can't be measured) may be from another network, a laptop elsewhere: asking about it from here would tell ipwho.is both.
    /// </summary>
    public static bool LookUpHome(string ip, string shown, bool cached) => !cached && ip != shown;

    List<string> _otherFirewalls;

    /// <summary>What to do with Windows Firewall off. The other firewalls are asked once (WMI, off the UI thread); until then the plain text.</summary>
    string FirewallHint(string without)
    {
        if (_otherFirewalls == null)
        {
            _otherFirewalls = new();
            _ = Task.Run(Firewall.OtherFirewalls).ContinueWith(t =>
            {
                if (t.Result.Count == 0) return;
                Log.Info("Firewall programs Windows Security knows about: " + string.Join(", ", t.Result) + ".");
                Dispatcher.BeginInvoke(() => { _otherFirewalls = t.Result; _ = SyncNow(); });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        return Firewall.OffHint(_otherFirewalls, without);
    }

    void ShowIps()
    {
        var inUse = _last?.Egress != null;
        var isHome = _egressPublic != null && _egressPublic == _homePublic;
        EgressIp.Text = _egressPublic == null ? "—" : isHome && !_reveal ? Fmt.MaskIp(_egressPublic) : _egressPublic;
        // Mid-change the IP shown is the old one (faint); a new one is green for two seconds, next to the toast that announces it.
        EgressIp.Foreground = !inUse || _changingTitle != null || _egressFailSince != null ? Ui.Faint : DateTime.Now < _ipFlashUntil ? Ui.Good : Ui.Text;
        EgressIsp.Text = _egressPublic == null ? (_last?.Candidate != null ? "Checking the IP through the phone…" : "No phone connection")
            : !inUse ? "Not in use: servers are blocked"
            : _egressFailSince != null ? (_egressFails < 2 ? "Checking again through the phone…" : $"Not confirmed since {_egressFailSince:HH:mm}: weak connection?")
            : _egressInfo?.Short ?? "";
        HomeIp.Text = _homePublic == null ? "—" : _reveal ? _homePublic : Fmt.MaskIp(_homePublic);
        HomeIsp.Text = _homePublic == null ? "No home network detected"
            : (_homeInfo?.Short ?? "") + (_homeCached ? "  ·  last confirmed" : "");
        SideIp.Text = inUse && _changingTitle == null ? _egressPublic ?? "" : "";
        if (_s.NoPhone && _last?.EffectiveMode == "home")
        {
            // No phone: servers see the home IP, so that is what the first box shows (hidden like the home box).
            EgressIp.Text = _homePublic == null ? "—" : _reveal ? _homePublic : Fmt.MaskIp(_homePublic);
            EgressIp.Foreground = Ui.Warn;
            EgressIsp.Text = "No phone: your home connection";
        }
        else if (_last?.EffectiveMode == "off")
        {
            // Protection off: servers see the home IP. The phone's, when it is checked, goes underneath (it is what they see once on);
            // a measured IP that isn't trusted may be the home one, so it isn't written there. With the whole PC on the phone (no other
            // connection, or Windows picked it) servers see the phone's IP, so the box shows that.
            var onPhone = _last != null && PcOnPhone(_last) && _egressPublic != null && _egressPublic != _homePublic;
            EgressIp.Text = onPhone ? _egressPublic : _homePublic == null ? "—" : _reveal ? _homePublic : Fmt.MaskIp(_homePublic);
            EgressIp.Foreground = Ui.Warn;
            EgressIsp.Text = onPhone ? "Protection off · the whole PC is using the phone" : OffEgressLine(_egressPublic, PhoneReadyOff(_last));
        }
        CopyIpBtn.Visibility = _egressPublic == null || !inUse ? Visibility.Collapsed : Visibility.Visible;
        RevealBtn.Visibility = _homePublic == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The line under the IP box with protection off: "phone ready" and its IP only once that IP is checked (never the home IP).</summary>
    public static string OffEgressLine(string phoneIp, bool ready) =>
        ready && phoneIp != null ? $"Protection off · phone ready: {phoneIp}" : "Protection off: your home connection";

    /// <summary>Protection off and the phone checked: Turn on protection trusts it at once.</summary>
    bool PhoneReadyOff(GuardReport rep) => rep?.EffectiveMode == "off" && rep.Candidate != null && rep.CandidateKey == _verifiedKey && _egressPublic != null;

    DateTime _ipFlashUntil;
    DispatcherTimer _ipFlashTimer;

    /// <summary>The new IP in Status turns green for two seconds after a change.</summary>
    void FlashNewIp()
    {
        _ipFlashUntil = DateTime.Now.AddSeconds(2);
        if (_ipFlashTimer == null)
        {
            _ipFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.1) };
            _ipFlashTimer.Tick += (_, _) => { _ipFlashTimer.Stop(); ShowIps(); };
        }
        _ipFlashTimer.Stop();
        _ipFlashTimer.Start();
        ShowIps();
    }

    /// <summary>
    /// The check through the phone saw the new IP before the change itself added it (the iPhone always, Android when a check lands
    /// mid-change): that line, added since the change started, becomes the change, so it counts once and turns green.
    /// </summary>
    public static bool SeenSinceChange(IList<IpHistoryEntry> history, string ip, string reason, DateTime? since) =>
        reason == "trocado" && since != null && history.Count > 0 && history[0].Ip == ip && history[0].Reason == "detetado" && history[0].Time >= since;

    void AddHistory(string ip, Net.IpInfo info, string reason, DateTime? changeSince = null)
    {
        if (SeenSinceChange(_history, ip, reason, changeSince))
        {
            var seen = _history[0];
            _history[0] = new IpHistoryEntry { Time = seen.Time, Ip = ip, Isp = info?.Short ?? seen.Isp, Reason = reason };
            Telemetry.Count("ip_changed");
            FlashNewIp();
            _s.IpHistory = _history.ToList();
            SaveSettings();
            return;
        }
        if (_history.Count > 0 && _history[0].Ip == ip) return;
        if (reason == "trocado") { Telemetry.Count("ip_changed"); FlashNewIp(); }
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

    async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask("Clear previous IPs", "The app uses them to tell you when a new IP is in the same range as an earlier one. Clear them?", "Clear")) return;
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

    async void LeakTest_Click(object sender, RoutedEventArgs e) => await RunCommand("Leak test", "testing…", LeakTest);

    async Task<bool> LeakTest()
    {
        if (_demo) { Toast("Test finished: everything protected.", true); return true; }
        if (!_protOn) { Toast("Protection is off: turn it on first, then test it.", false); return false; }
        LeakBtn.IsEnabled = false;
        ShowProgress("Testing: Firewall, kill switch, routes and public IP…");
        try
        {
            await SyncNow(force: true);
            KeepRegionRule();   // the Regions rule checked now, not at the next 30 s tick
            await RefreshPublicIps();
            if (_s.NoPhone)
            {
                Toast("No phone mode: there is no protection to test. CS2 and servers see your home IP.", false);
                return false;
            }
            var bad = LeakFailures(_checks, _last?.EffectiveMode == "all");
            var (ok, text) = LeakResult(bad, _s.LockCs2, _cQuality.Ok == false, LeakOkText(_egressPublic, Blocked(_last, lower: true), _s.LockCs2 && !PhoneKnown));
            Telemetry.LeakTest(ok);
            if (ok) Log.Ok("Leak test: everything confirmed.");
            else Log.Warn(bad.Count > 0 ? "Leak test failed on: " + string.Join(", ", bad) : "Leak test: CS2 only through the phone is off, so servers not in the list can see the home IP.");
            Toast(text, ok);
            return ok;
        }
        finally { HideProgress(); LeakBtn.IsEnabled = true; }
    }

    /// <summary>
    /// The Checks rows that fail the leak test: only leaks. A weak connection and the regions you turned off aren't, and Other apps
    /// only is in Whole PC (in Game only red there means the whole PC is on mobile data, which costs data but leaks nothing).
    /// </summary>
    public static List<string> LeakFailures(IEnumerable<CheckItem> checks, bool wholePc) =>
        checks.Where(c => c.Ok == false && c.Title is not ("Connection quality" or "Matchmaking regions") && (wholePc || c.Title != "Other apps"))
            .Select(c => c.Title).ToList();

    /// <summary>The leak test's toast: what failed, or with CS2 not locked a warning instead of "No leaks", or the OK text (a weak link said apart).</summary>
    public static (bool Ok, string Text) LeakResult(IReadOnlyList<string> bad, bool lockCs2, bool weak, string okText) =>
        bad.Count > 0 ? (false, "Warning: " + string.Join(", ", bad) + ". See Checks.")
        : !lockCs2 ? (false, "Your servers are protected, but CS2 only through the phone is off: servers not in your list can see your home IP.")
        : (true, okText + (weak ? " The connection is weak: see Checks." : ""));
}
