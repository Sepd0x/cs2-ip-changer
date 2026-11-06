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
    }
}
