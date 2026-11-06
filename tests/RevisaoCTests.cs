using System.IO;
using System.Net.Http;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área C: servidores e regiões): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoCTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão C: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        var xaml = Src("MainWindow.xaml");
        var servers = Src("Views", "MainWindow.Servers.cs");
        var regions = Src("Views", "MainWindow.Regions.cs");

        // C1: adicionar diz o que acontece no estado em que se está
        check("Revisão C1: com a proteção desligada adicionar não diz protected", !MainWindow.AddedText(2, 0, protOn: false, noPhone: false, egress: false).Contains("protected"), "");
        check("Revisão C1: proteção desligada diz que veem o IP de casa", MainWindow.AddedText(2, 0, false, false, false) == "2 servers added. Protection is off: they see your home IP until you turn it on.", MainWindow.AddedText(2, 0, false, false, false));
        check("Revisão C1: No phone diz que veem o IP de casa", MainWindow.AddedText(1, 0, true, true, false) == "1 server added. No phone mode: it sees your home IP.", MainWindow.AddedText(1, 0, true, true, false));
        check("Revisão C1: com o telemóvel ligado vão pelo telemóvel", MainWindow.AddedText(2, 1, true, false, true) == "2 servers added. They go through the phone. 1 ignored.", MainWindow.AddedText(2, 1, true, false, true));
        check("Revisão C1: proteção ligada sem telemóvel ficam bloqueados", MainWindow.AddedText(2, 0, true, false, false).Contains("blocked until the phone"), MainWindow.AddedText(2, 0, true, false, false));
        check("Revisão C1: o Add não promete o telemóvel", !xaml.Contains("routes them through the phone right away"), "");
        check("Revisão C1: No phone não oferece jogar pelo telemóvel", !MainWindow.AddJoinedText("1.2.3.4:27015", noPhone: true).Contains("through the phone"), "");
        check("Revisão C1: o toast de servidor novo sabe se é No phone", Src("Views", "MainWindow.Kick.cs").Contains("AddJoinedText(server, _s.NoPhone)"), "");

        // C2: telemóvel ligado e por verificar não é "desligado"
        var unchecked_ = new GuardReport { Candidate = new Adapter { Index = 5, LocalIp = "192.168.42.10" }, Distrust = Distrust.Unchecked, EgressProblem = DistrustText.Of(Distrust.Unchecked) };
        check("Revisão C2: telemóvel ligado e por verificar não diz disconnected", !MainWindow.NoLinkWhy(unchecked_).Contains("disconnected") && MainWindow.NoLinkWhy(unchecked_) == DistrustText.Of(Distrust.Unchecked), MainWindow.NoLinkWhy(unchecked_));
        check("Revisão C2: sem telemóvel diz que não está ligado", MainWindow.NoLinkWhy(new GuardReport()).Contains("isn't connected") && MainWindow.NoLinkWhy(null).Contains("isn't connected"), "");
        check("Revisão C2: o Join não mostra a palavra Blocked como motivo", !servers.Contains("protection is not confirmed ({s.RouteText})"), "");
        check("Revisão C2: o Join sem ligação diz o motivo real", MainWindow.JoinBlockedText("1.2.3.4:27015", unchecked_, "Blocked") == "Not joining 1.2.3.4:27015. " + DistrustText.Of(Distrust.Unchecked), MainWindow.JoinBlockedText("1.2.3.4:27015", unchecked_, "Blocked"));
        var linked = new GuardReport { Egress = new Adapter { Index = 5, LocalIp = "192.168.42.10" } };
        check("Revisão C2: o Join com ligação e rota por confirmar diz a rota", MainWindow.JoinBlockedText("X", linked, "Route failed") == "Not joining X: its route through the phone isn't confirmed (Route failed).", MainWindow.JoinBlockedText("X", linked, "Route failed"));
        check("Revisão C2: espera por vaga com a proteção desligada diz isso", MainWindow.SlotNoLinkText("X", onHome: false, protOn: false, null) == "Waiting for a slot on X: protection is off.", "");
        check("Revisão C2: espera por vaga com o telemóvel por verificar diz o motivo", MainWindow.SlotNoLinkText("X", false, true, unchecked_) == "Waiting for a slot on X. " + DistrustText.Of(Distrust.Unchecked), "");
        check("Revisão C2: Refresh e Regions usam o mesmo motivo", servers.Contains("NoLinkWhy(_last)") && regions.Contains("NoLinkWhy(_last)") && !servers.Contains("Phone disconnected: not querying") && !regions.Contains("the phone is disconnected"), "");
        check("Revisão C2: a linha de Regions com o telemóvel por verificar não manda ligá-lo", !MainWindow.NoPingSuffix(unchecked_).Contains("connect the phone") && MainWindow.NoPingSuffix(null).Contains("connect the phone"), "");

        // C9: duplo clique num botão da linha não entra no servidor
        var dbl = servers.IndexOf("void ServerList_DoubleClick");
        check("Revisão C9: duplo clique num botão da linha não entra no servidor",
            dbl > 0 && servers.IndexOf("OnRowControl(e.OriginalSource as DependencyObject, ServerList, out _)", dbl) is var i9 && i9 > 0 && i9 < servers.IndexOf("await Join(s);", dbl), "");
        check("Revisão C9: o clique simples usa a mesma regra", servers.Contains("if (OnRowControl(e.OriginalSource as DependencyObject, ServerList, out var row)) return;"), "");

        // C10: a vaga não entra sozinha com outro comando ou uma pergunta aberta
        check("Revisão C10: com outro comando a correr não entra sozinho", !MainWindow.SlotAutoJoins(autoOn: true, busy: true, dialogOpen: false), "");
        check("Revisão C10: com uma pergunta aberta não entra sozinho", !MainWindow.SlotAutoJoins(true, false, true), "");
        check("Revisão C10: livre e com a opção, entra", MainWindow.SlotAutoJoins(true, false, false) && !MainWindow.SlotAutoJoins(false, false, false), "");
        check("Revisão C10: a vaga e o servidor cheio usam a regra", servers.Contains("if (SlotAutoJoins(auto, CommandBusy, Dialog.Visibility == Visibility.Visible))")
            && Src("Views", "MainWindow.Kick.cs").Contains("SlotAutoJoins(true, CommandBusy, Dialog.Visibility == Visibility.Visible)"), "");

    }
}
