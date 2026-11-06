using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área A: topo, proteção, telemóvel): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoATests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão A: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");

        var xaml = Src("MainWindow.xaml");
        var prot = Src("Views", "MainWindow.Protection.cs");
        var guard = Src("Core", "Network", "Guard.cs");

        // A1: sem telemóvel visto o CS2 não está trancado (o Guard só arma a regra depois), por isso nada promete o CS2 bloqueado
        check("Revisão A1: à espera do primeiro telemóvel, o Status não diz que o CS2 espera", !MainWindow.WaitingSub(true, lockCs2: true).Contains("CS2 wait")
              && MainWindow.WaitingSub(true, lockCs2: true).Contains("CS2 isn't locked") && !MainWindow.WaitingSub(true, lockCs2: false).Contains("CS2 isn't locked"), "");
        check("Revisão A1: o toast do Turn on sem telemóvel só nomeia o que está bloqueado", !prot.Contains("servers and CS2 stay blocked. Phone setup") && prot.Contains("{Blocked(_last, lower: true)} stay blocked. Phone setup"), "");
        check("Revisão A1: o Check do CS2 por armar diz que não está trancado (âmbar)", MainWindow.Cs2NotLockedYet.StartsWith("Not locked") && !prot.Contains("\"Waits for the phone\""), "");
        check("Revisão A1: a pergunta do Turn on com o CS2 aberto só diz 'or blocks it' com o lock armado",
              MainWindow.TurnOnCs2Open(true).Contains("or blocks it") && !MainWindow.TurnOnCs2Open(false).Contains("blocks") && prot.Contains("TurnOnCs2Open(_s.LockCs2 && PhoneKnown)"), "");
        check("Revisão A1: o leak test sem telemóvel diz que o CS2 ainda não está trancado",
              MainWindow.LeakOkText(null, "your servers", true).Contains("CS2 isn't locked") && !MainWindow.LeakOkText(null, "your servers", false).Contains("CS2")
              && MainWindow.LeakOkText("1.2.3.4", "your servers", true) == "No leaks. Servers see 1.2.3.4.", "");
        check("Revisão A1: o tooltip de Turn on protection when the app starts não promete o CS2 bloqueado", !xaml.Contains("your servers and CS2 stay blocked until it is.")
              && xaml.Contains("your servers stay blocked until it is, and CS2 too once the app has checked a phone"), "");
        check("Revisão A1: um IP medido sem confiança não faz a janela achar o telemóvel conhecido (o Guard não arma o CS2)",
              prot.Contains("bool PhoneKnown => _s.PhoneSeen || _last?.Egress != null || (_demo && _history.Count > 0);"), "");

        // A13: "can't leak" e "You're safe" só com o CS2 trancado; o Log da queda diz o que está bloqueado
        check("Revisão A13: o toast da queda só diz 'can't leak' com o CS2 trancado", !MainWindow.DropToast(null, true, cs2Locked: false).Contains("can't leak")
              && MainWindow.DropToast(null, true, cs2Locked: true).Contains("Servers and CS2 are now blocked: your IP can't leak.")
              && MainWindow.DropToast(null, false, true).Contains("isn't confirmed"), "");
        check("Revisão A13: sem o CS2 trancado o Status não diz 'You're safe'", !MainWindow.DisconnectedSafe(false).Contains("safe") && MainWindow.DisconnectedSafe(true).StartsWith("You're safe: servers and CS2"), "");
        check("Revisão A13: o Log da queda não diz sempre 'servers and CS2 blocked'", !prot.Contains("Kill switch on: servers and CS2 blocked.\")") && prot.Contains("Kill switch on: {Blocked(rep, lower: true)} blocked."), "");

        // A19: nomes, não lugares (DESIGN.md)
        check("Revisão A19: o Status não aponta o Phone setup por sítio", !prot.Contains("Phone setup below") && !prot.Contains("See Phone setup below"), "");

        // A24: "everything is blocked" e "(no connection)" exageravam
        check("Revisão A24: o tooltip do hotspot diz o que fica bloqueado", !prot.Contains("everything is blocked.") && prot.Contains("your servers and CS2 are blocked while protection is on"), "");
        check("Revisão A24: o Log do kill switch distingue sem ligação de ligação por confirmar", guard.Contains("(candidate != null ? \" blocked (the phone isn't trusted yet).\" : \" blocked (no connection).\")"), "");
        check("Revisão A6: a pergunta do hotspot diz que só bloqueia com a proteção ligada", prot.Contains("servers and CS2 get blocked while protection is on."), "");

        var phone = Src("Views", "MainWindow.Phone.cs");
        var iphoneSrc = Src("Views", "MainWindow.Iphone.cs");
        var settings = Src("Views", "MainWindow.Settings.cs");

        // A2: com a proteção desligada, o IP igual ao de casa não diz "blocked" nem toca o alarme
        check("Revisão A2: proteção desligada, IP igual ao de casa sem 'blocked'", !MainWindow.SameIpText("1.2.3.4", unprotected: true).Contains("blocked")
              && MainWindow.SameIpText("1.2.3.4", unprotected: false).Contains("blocked"), "");
        check("Revisão A2: proteção desligada, o som e o toast de bloqueio não saem",
              prot.Contains("if (same && unprotected) Log.Warn(SameIpText(e, unprotected: true));\n                else if (same)\n                {\n                    Log.Error(SameIpText(e, unprotected: false));\n                    if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();"), "");
        check("Revisão A2: proteção desligada, sem IP de casa não diz 'blocked'", !MainWindow.NoHomeText(true).Contains("blocked") && MainWindow.NoHomeText(false).Contains("blocked"), "");
        check("Revisão A2: proteção desligada, o IP verificado não diz que os servidores já o veem",
              MainWindow.CheckedText("1.2.3.4", true).Contains("once protection is on") && MainWindow.CheckedText("1.2.3.4", false) == "Phone connection checked: servers see 1.2.3.4.", "");

        // A3: Turn off com o CS2 aberto diz que o servidor onde se está vê o IP de casa
        check("Revisão A3: Turn off com o CS2 aberto avisa do servidor onde se está", MainWindow.TurnOffAsk(true).Contains("if you are on a server, it sees your home IP from now on")
              && !MainWindow.TurnOffAsk(false).Contains("CS2 is open") && settings.Contains("TurnOffAsk(!_demo && Steam.Cs2Running)"), "");

        // A4: proteção desligada, a caixa do IP não escreve o IP de casa nem "phone ready" sem confiança
        check("Revisão A4: proteção desligada, IP sem confiança não aparece nem diz 'ready'", !MainWindow.OffEgressLine("85.1.1.1", ready: false).Contains("85.1.1.1")
              && !MainWindow.OffEgressLine("85.1.1.1", ready: false).Contains("ready") && MainWindow.OffEgressLine("85.1.1.1", ready: true).Contains("phone ready: 85.1.1.1"), "");
        check("Revisão A4: a caixa usa a mesma confiança que o Status", prot.Contains("OffEgressLine(_egressPublic, PhoneReadyOff(_last))") && prot.Contains("var phoneReady = PhoneReadyOff(rep);"), "");

        // A6: frases que diziam "blocked" ou "servers see it" com a proteção desligada
        check("Revisão A6: Change IP de volta com o IP de casa, sem proteção, não diz 'blocked'", !MainWindow.HomeBackText(false).Contains("blocked") && MainWindow.HomeBackText(true).Contains("blocked"), "");
        check("Revisão A6: Phone setup pronto, sem proteção, diz para a ligar", MainWindow.SetupReadyText(false).Contains("Turn on protection") && !MainWindow.SetupReadyText(false).Contains("then servers see it"), "");
        // (o balão do ícone, A6.4, já ficou corrigido pela área D: não fala da proteção)
        check("Revisão A6: o balão do ícone não diz 'Protection carries on'", !MainWindow.TrayHintText.Contains("Protection carries on"), "");

        // A14: as regiões continuam bloqueadas com a proteção desligada e em No phone
        check("Revisão A14: 'nothing is blocked' só sem regiões bloqueadas", MainWindow.NothingBlocked(0) == "nothing is blocked" && MainWindow.NothingBlocked(2).Contains("regions")
              && !prot.Contains("and nothing is blocked. Accounts"), "");
        check("Revisão A14: o aviso de Use the app with protection off fala das regiões", MainWindow.UseOffWarning.Contains("regions") && MainWindow.UseOffWarning.Contains("sees your home IP"), "");

        // A15: com o PC todo pelo telemóvel e a proteção desligada, os servidores veem o IP do telemóvel
        var phoneAdapter = new Adapter { Index = 7, Name = "Ethernet 2", IsPhone = true, Up = true };
        var onPhoneRep = new GuardReport { EffectiveMode = "off", Adapters = new() { phoneAdapter }, PcDefaultIndex = 7, PcDefaultName = "Ethernet 2", PhoneIndex = 7 };
        var homeRep = new GuardReport { EffectiveMode = "off", Adapters = new() { new Adapter { Index = 3, Name = "Wi-Fi", Up = true }, phoneAdapter }, PcDefaultIndex = 3, PcDefaultName = "Wi-Fi", PhoneIndex = 7 };
        check("Revisão A15: o PC pelo telemóvel reconhece-se", MainWindow.PcOnPhone(onPhoneRep) && !MainWindow.PcOnPhone(homeRep), "");
        check("Revisão A15: com o PC pelo telemóvel o Status não diz que veem o IP de casa", !MainWindow.OffSeen(true, 0).Contains("every server see your home IP") && MainWindow.OffSeen(true, 0).Contains("see its IP")
              && MainWindow.OffSeen(false, 0) == "CS2 and every server see your home IP, and nothing is blocked.", "");

        // A20 (parte do Mobile data): com a proteção desligada o Wi-Fi do telemóvel não é vermelho
        check("Revisão A20: proteção desligada, Mobile data com o Wi-Fi ligado fica âmbar", MainWindow.DataRow(true, false, unprotected: true) == (null, true)
              && MainWindow.DataRow(true, false, false) == (false, false) && MainWindow.DataRow(true, true, false) == (null, true) && MainWindow.DataRow(false, false, true) == (true, false), "");

        // A21: conta servidores, não IPs
        check("Revisão A21: 'Off: N servers' conta servidores", MainWindow.RoutesOffText(1) == "Off: 1 server sees your home IP" && MainWindow.RoutesOffText(3) == "Off: 3 servers see your home IP"
              && prot.Contains("RoutesOffText(_servers.Count)"), "");

        // A22: a pergunta do fecho segue Turn on protection when the app starts
        check("Revisão A22: com Turn on at start a pergunta do fecho diz que começa ligada", !MainWindow.CloseKeepText("Your servers", true).Contains("starts with protection off")
              && MainWindow.CloseKeepText("Your servers", true).Contains("starts with protection on") && MainWindow.CloseKeepText("Your servers", false).Contains("starts with protection off"), "");

        // A23: a linha VPN app sem proteção não diz que vai pelo telemóvel, e muda com o estado
        check("Revisão A23: VPN app sem proteção não diz 'through the phone'", !ChecksView.VpnHelpFor(true).Contains("through the phone") && ChecksView.VpnHelpFor(false) == ChecksView.VpnHelp, "");
        check("Revisão A23: a ajuda da linha VPN atualiza-se em cada relatório", Src("Views", "MainWindow.Checks.cs").Contains("_cVpn.Help = ChecksView.VpnHelpFor(rep.Unprotected);"), "");

        // A25: com a proteção desligada (e o CS2 por casa) trocar o IP não deita ninguém abaixo
        check("Revisão A25: trocar o IP só derruba com a proteção ligada ou o PC pelo telemóvel", MainWindow.ChangeDrops(true, false) && MainWindow.ChangeDrops(false, true) && !MainWindow.ChangeDrops(false, false), "");
        check("Revisão A25: sem derrubar, o Status da troca não diz 'you will drop'", !MainWindow.ChangingSub(false).Contains("drop") && MainWindow.ChangingSub(true).Contains("you will drop"), "");
        check("Revisão A25: a pergunta 'CS2 is open' do Change IP só quando derruba", phone.Contains("if (confirm && Steam.Cs2Running && IpChangeDrops && !await Ask(\"CS2 is open\",")
              && phone.Contains("var drop = IpChangeDrops ?") && iphoneSrc.Contains("(IpChangeDrops ? \"If you are on a server, you will drop.\""), "");

        // A5: cada Change IP com a proteção ligada dizia "dropped", tocava o alarme e depois "Protection is back"
        check("Revisão A5: uma troca de IP não dá o alarme de queda", !MainWindow.DropAlarm(true, false, changingIp: true) && MainWindow.DropAlarm(true, false, changingIp: false) && !MainWindow.DropAlarm(true, true, false), "");
        check("Revisão A5: durante a troca o Log e o alarme calam-se e _wasProtected fica como estava",
              prot.Contains("var quiet = _changingIp || _ipChangeQuiet;") && prot.Contains("if (egressChanged && _lastEgressKey != null && !home && !quiet)")
              && prot.Contains("else if (quiet) { }") && prot.Contains("if (!quiet) _wasProtected = protectedNow;"), "");
        check("Revisão A5: o silêncio da troca acaba só depois do IP novo verificado e sincronizado (Android)",
              phone.IndexOf("_ipChangeQuiet = true;") > 0 && phone.IndexOf("_ipChangeQuiet = true;") < phone.IndexOf("_verifiedKey = null;\n        await SyncNow(force: true);")
              && phone.IndexOf("await RefreshPublicIps();\n                await SyncNow();") < phone.IndexOf("finally { _ipChangeQuiet = false; }"), "");
        check("Revisão A5: o Change IP guiado do iPhone também não dá o alarme", iphoneSrc.Contains("_ipChangeQuiet = true;") && iphoneSrc.Contains("finally { _ipChangeQuiet = false; }"), "");

        // A11: no iPhone o IP novo ficava "at startup", sem o verde nem a contagem
        var t0 = new DateTime(2026, 11, 6, 22, 0, 0);
        var seenNow = new List<IpHistoryEntry> { new() { Time = t0.AddSeconds(30), Ip = "5.5.5.5", Reason = "detetado" } };
        check("Revisão A11: o IP visto pela verificação durante a troca passa a ser a troca", MainWindow.SeenSinceChange(seenNow, "5.5.5.5", "trocado", t0), "");
        check("Revisão A11: um IP de antes da troca, outro IP ou sem troca não muda",
              !MainWindow.SeenSinceChange(seenNow, "5.5.5.5", "trocado", t0.AddMinutes(1)) && !MainWindow.SeenSinceChange(seenNow, "6.6.6.6", "trocado", t0)
              && !MainWindow.SeenSinceChange(seenNow, "5.5.5.5", "detetado", t0) && !MainWindow.SeenSinceChange(seenNow, "5.5.5.5", "trocado", null)
              && !MainWindow.SeenSinceChange(new List<IpHistoryEntry>(), "5.5.5.5", "trocado", t0), "");
        check("Revisão A11: as duas trocas dizem quando começaram", iphoneSrc.Contains("AddHistory(now, _egressInfo, \"trocado\", since);") && phone.Contains("AddHistory(got, _egressInfo, \"trocado\", _ipChangedAt);")
              && !phone.Contains("AddHistory(got, _egressInfo, \"trocado\");"), "");

        // A12: três trocas no iPhone davam o aviso do cabo; desconfiança não é queda
        check("Revisão A12: desconfiança (ligação ainda lá) não é queda", !MainWindow.IsDrop(true, false, exitNow: true, false), "");
        check("Revisão A12: uma troca de IP não é queda", !MainWindow.IsDrop(true, false, false, changingIp: true), "");
        check("Revisão A12: o adaptador a desaparecer é queda", MainWindow.IsDrop(true, false, false, false) && !MainWindow.IsDrop(false, false, false, false), "");
        check("Revisão A12: o contador de quedas conta a troca do iPhone como troca", Src("Views", "MainWindow.Telemetry.cs").Contains("IsDrop(_egressUp, up, PhoneLink.Exit(rep) != null, _changingIp || _ipChangeQuiet)"), "");

        // A8: o Change IP do iPhone com a proteção desligada (sem Egress) e o NullReference do IsWifi
        check("Revisão A8: o Change IP do iPhone mede pela ligação do telemóvel, não só pela protegida",
              iphoneSrc[iphoneSrc.IndexOf("async Task<bool> IphoneChangeIp()")..] is var ich && !ich.Contains("_last?.Egress") && !ich.Contains("_last.Egress") && iphoneSrc.Contains("PhoneLink.Exit(_last) is not { } exit") && iphoneSrc.Contains("var connected = PhoneLink.Exit(_last) != null;"), "");
        // A7 (decidido a 6 nov.): No phone desliga a proteção à vista; sair não a liga sozinha, pergunta
        check("Revisão A7: No phone desliga a proteção, os outros modos deixam-na como está", !MainWindow.ProtOnAfterMode(true, "home") && MainWindow.ProtOnAfterMode(true, "servers")
              && MainWindow.ProtOnAfterMode(true, "all") && !MainWindow.ProtOnAfterMode(false, "servers"), "");
        check("Revisão A7: escolher No phone põe _protOn a false antes de mudar o modo", prot.IndexOf("_protOn = ProtOnAfterMode(_protOn, mode);") is var p7 && p7 > 0 && p7 < prot.IndexOf("_s.Mode = mode;"), "");
        check("Revisão A7: a pergunta de No phone não promete que voltar protege", !MainWindow.NoPhoneAsk(false).Contains("to protect again.\"") && MainWindow.NoPhoneAsk(false).Contains("then turn protection on")
              && !prot.Contains("Pick another option here to protect again.") && !prot.Contains("under What goes through the phone to protect again."), "");
        check("Revisão A7: No phone com o CS2 pelo telemóvel diz que o servidor passa a ver o IP de casa (A3)", MainWindow.NoPhoneAsk(true).Contains(MainWindow.Cs2OpenHomeNow) && !MainWindow.NoPhoneAsk(false).Contains("CS2 is open"), "");
        check("Revisão A7: sair de No phone pergunta Turn on / Not now, e com o CS2 aberto é a pergunta do Turn on",
              prot.Contains("await Ask(\"Turn on protection now?\", LeaveNoPhoneAsk(Steam.Cs2Running, _s.LockCs2 && PhoneKnown), \"Turn on\", \"Not now\")")
              && MainWindow.LeaveNoPhoneAsk(true, true) == MainWindow.TurnOnCs2Open(true) && MainWindow.LeaveNoPhoneAsk(false, true).Contains("Protection is off"), "");
        check("Revisão A7: fechar em No phone com o CS2 aberto não pergunta pelo tráfego que já vai por casa",
              Src("Views", "MainWindow.Window.cs").Contains("if (Steam.Cs2Running && !_restored && _protOn && _last?.Unprotected != true)"), "");

        // A20 (decidido a 6 nov.): com a proteção desligada a linha Phone connection fica cinzenta e diz o estado
        check("Revisão A20: proteção desligada, Phone connection diz que não está em uso", MainWindow.OffPhoneRow(null, null, false, Distrust.None) == "Not in use: protection is off"
              && MainWindow.OffPhoneRow("USB", "192.0.2.5", true, Distrust.None).StartsWith("Ready (USB · 192.0.2.5)")
              && MainWindow.OffPhoneRow("USB", "192.0.2.5", false, Distrust.SameIp).StartsWith("Not in use: protection is off."), "");
        check("Revisão A20: proteção desligada, Phone connection e o IP igual nunca a vermelho",
              prot.Contains("_cEgress.Ok = null;\n            _cEgress.Waiting = false;\n            _cEgress.Detail = OffPhoneRow(")
              && prot.Contains("_cDiff.Ok = _egressPublic == null || _homePublic == null || same ? null : true;"), "");

        // A8 (botão), A9, A18: uma só regra para o telemóvel poder trocar o IP
        check("Revisão A8: o Change IP fica ativo com um iPhone ligado, também com a proteção desligada", MainWindow.CanChangeIp(noPhone: false, adbReady: false, iphone: true, iphoneUp: true)
              && !MainWindow.CanChangeIp(false, false, true, iphoneUp: false) && MainWindow.CanChangeIp(false, adbReady: true, false, false) && !MainWindow.CanChangeIp(noPhone: true, true, true, true), "");
        check("Revisão A8: o botão Change IP usa a regra, não o Egress", phone.Contains("ChangeIpBtn.IsEnabled = !_changingIp && ChangeIpReady;") && !phone.Contains("iphone && _last?.Egress != null"), "");
        check("Revisão A9: Change IP and rejoin ativo com o iPhone e em Use the app with protection off sem telemóvel",
              MainWindow.RejoinEnabled(true, false, canChangeIp: true, false) && MainWindow.RejoinEnabled(true, false, false, skipIp: true)
              && !MainWindow.RejoinEnabled(false, false, true, true) && !MainWindow.RejoinEnabled(true, noPhone: true, true, true) && !MainWindow.RejoinEnabled(true, false, false, false), "");
        check("Revisão A9: o botão do rejoin usa a regra", Src("Views", "MainWindow.Kick.cs").Contains("RejoinEnabled(_s.LastServer != null, _s.NoPhone, ChangeIpReady, IpStep(_protOn, _s.UseWithProtectionOff) == IpWhenOff.Skip)"), "");
        check("Revisão A18: o nome do switch só diz New IP quando o clique troca o IP", !MainWindow.SwitchChangesIp(account: true, canChangeIp: true, adbReady: false)
              && MainWindow.SwitchChangesIp(true, true, adbReady: true) && MainWindow.SwitchChangesIp(account: false, canChangeIp: true, false) && !MainWindow.SwitchChangesIp(false, false, false), "");
        check("Revisão A18: o nome usa a mesma regra (fora da demo)", Src("Views", "MainWindow.Accounts.cs").Contains("!_demo && !SwitchChangesIp(c.Account, ChangeIpReady, _adb != null && _phone?.Ready == true)"), "");

        // A10: No phone com iPhone
        check("Revisão A10: No phone com iPhone diz que não está em uso", phone.IndexOf("if (_s.NoPhone) SetPhone(Ui.Faint, \"iPhone not in use\", \"No phone mode is on\");") is var p10 && p10 > 0
              && p10 < phone.IndexOf("else if (tethered) SetPhone(Ui.Good, \"iPhone connected\""), "");

        // A16: no hotspot o botão Turn off phone Wi-Fi não aparece
        check("Revisão A16: o Turn off phone Wi-Fi segue a regra do bloqueio (escondido no hotspot)", phone.Contains("WifiOffBtn.Visibility = info?.Ready == true && PhoneWifiOn ?") && !phone.Contains("WifiOffBtn.Visibility = info?.Ready == true && info.WifiOn"), "");

        // A17: o tooltip do tethering diz o tempo que a app espera
        check("Revisão A17: o tooltip do Turn on USB tethering diz o tempo de espera certo", xaml.Contains($"waits up to {MainWindow.TetherWait.TotalSeconds:0} s for Windows to see it"), "");

        // A26: telemóvel perdido com a proteção ligada, o Phone setup é o botão azul
        check("Revisão A26: telemóvel perdido, Phone setup é o principal", MainWindow.SetupPrimary(false, phoneLost: true, phoneWifiOn: false) && !MainWindow.SetupPrimary(false, true, phoneWifiOn: true)
              && MainWindow.SetupPrimary(firstRun: true, false, false) && !MainWindow.SetupPrimary(false, false, false), "");
        check("Revisão A26: o estilo do Phone setup usa a regra", phone.Contains("PhoneSetupBtn.Style = (Style)FindResource(SetupPrimary(first, lost, PhoneWifiOn) ?"), "");

        check("Revisão A8: o IP novo do iPhone só conta verificado (nunca o de casa)", iphoneSrc.Contains("_verifiedKey != null && _verifiedKey == _last?.CandidateKey ? _egressPublic : null"), "");
    }
}
