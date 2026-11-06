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
        check("Revisão A6: o balão do ícone só diz 'Protection carries on' com a proteção ligada", !MainWindow.TrayHintText(false).Contains("Protection") && MainWindow.TrayHintText(true).StartsWith("Protection carries on."), "");

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
    }
}
