#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 26 nov.: os achados da auditoria do Codex (ServerRouteToolCodex/CODEX-RELATORIO.md) confirmados no main, um teste por correção.
// A3 e A4 são os testes que o Codex escreveu (tests/CodexAuditTests.cs da pasta dele), com a API de agora.
static class CodexAuditTests
{
    public static void Run(Action<string, bool, string> check)
    {
        // A2: Whole PC só confirmava a metade de baixo do IPv4 (0/1) depois de escrever as duas rotas
        check("Codex A2: Whole PC só confirmado com as duas metades no telemóvel",
            Guard.BothHalvesOn(7, 7, 7) && !Guard.BothHalvesOn(7, 3, 7) && !Guard.BothHalvesOn(3, 7, 7), "");
        var guard = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Core", "Network", "Guard.cs"));
        check("Codex A2: depois das escritas relê as duas sondas",
            guard.Contains("fullOk = BothHalvesOn(Net.BestInterface(Net.PcProbeIp), Net.BestInterface(Net.PcProbeIpHigh), egress.Index);")
            && !guard.Contains("fullOk = Net.BestInterface(Net.PcProbeIp) == egress.Index;"), "");

        // A3: uma regra com o nome temporário de um Apply interrompido é um bloqueio que ficou
        const string updating = " (updating 0123456789)";
        var remaining = new[] { Firewall.Cs2RuleName + updating };
        var stayedOn = Guard.OnAtStart(false, Array.Empty<string>(), false, Firewall.BlockLeft(remaining), cs2Running: true, noPhone: false);
        check("Codex A3: o arranque mantém um bloqueio temporário com o jogo aberto", stayedOn, $"proteção mantida = {stayedOn}");
        var dns = new[] { Firewall.DnsUdpRuleName + updating };
        check("Codex A3: o fecho reconhece a regra DNS temporária que tem de remover", Firewall.Has(dns, Firewall.DnsUdpRuleName), "");
        check("Codex A3: o nome base tira só a cauda do Apply interrompido",
            Firewall.BaseName(Firewall.RuleName + updating) == Firewall.RuleName && Firewall.BaseName(Firewall.RuleName) == Firewall.RuleName && Firewall.BaseName(null) == null, "");
        check("Codex A3: a regra do teste de IP, mesmo temporária, não conta como bloqueio",
            !Firewall.BlockLeft(new[] { Firewall.EchoRuleName + updating }), "");

        // A4: duas marcas de ban com hosts e portas diferentes bloqueavam também os pares cruzados
        var rules = Firewall.BannedIpRules(new[] { "192.0.2.10:27015", "192.0.2.20:27016" });
        var crossed = rules.Any(r => r.RemoteIps.Contains("192.0.2.10") && r.RemotePorts!.Split(',').Contains("27016"))
            || rules.Any(r => r.RemoteIps.Contains("192.0.2.20") && r.RemotePorts!.Split(',').Contains("27015"));
        check("Codex A4: duas marcas de ban preservam os pares de endereço e porta", !crossed && rules.Count == 2, $"outro servidor bloqueado = {crossed}");
        var same = Firewall.BannedIpRules(new[] { "192.0.2.10:27015", "192.0.2.20:27015" });
        check("Codex A4: a mesma porta continua numa regra só", same.Count == 1 && same[0].Name == Firewall.BannedIpRuleName && same[0].RemoteIps.Count == 2, "");
        check("Codex A4: as regras de cada porta são da família da regra de ban (sentido, fecho, restore)",
            Firewall.IsBannedIpRule(Firewall.BannedIpPortName(27016)) && Firewall.IsBannedIpRule(Firewall.BannedIpPortName(27016) + updating)
            && Firewall.IsBannedIpRule(Firewall.BannedIpRuleName) && !Firewall.IsBannedIpRule(Firewall.RuleName)
            && Firewall.Meaning(Firewall.BannedIpPortName(27016)) == Firewall.Meaning(Firewall.BannedIpRuleName)
            && Firewall.IsAppRule(Firewall.BannedIpPortName(27016)) && !Firewall.BlockLeft(new[] { Firewall.BannedIpPortName(27016) }), "");
        var restore = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tools", "restore.cmd"));
        check("Codex A4: o restore.cmd tira as regras de cada porta (nome com Pickroute - *)", restore.Contains("'Pickroute - *'"), "");

        // A5: sem a lista de regiões, o Allow all ficava desligado e a regra guardada não saía pela página
        check("Codex A5: Allow all ligado sem a lista quando há regiões bloqueadas",
            MainWindow.AllowAllEnabled(false, false, 3, false) && MainWindow.AllowAllEnabled(false, false, 0, true) && !MainWindow.AllowAllEnabled(false, false, 0, false)
            && MainWindow.AllowAllEnabled(true, false, 0, false), "");
        var regions = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Regions.cs"));
        check("Codex A5: sem a lista tira a regra pelo nome do jogo e relê antes de dizer que saiu",
            regions.Contains("AllowAllWithoutList(_regionGame)") && regions.Contains("Firewall.Remove(game.RuleName);") && regions.Contains("Firewall.IsRegionRule(n, game)"), "");

        // A1 (decisão de 26 nov.: aceitar a proposta do Codex): Android por USB, sem ADB, sem IP de casa e o telemóvel no Wi-Fi de casa.
        // A app não consegue ver o Wi-Fi do telemóvel nem comparar IPs: fica bloqueado e não confirmado, nunca Protected
        var android = new Adapter { Index = 7, Name = "Ethernet 3", Description = "Remote NDIS based Internet Sharing Device", IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var unconfirmed = MainWindow.MobileUnconfirmed(hasHome: false, android, iphone: false, adbReady: false, homeIpFromHome: false);
        var distrust = MainWindow.DistrustNow(sameAsHome: false, phoneWifiOn: false, noHome: false, unconfirmed);
        check("Codex A1: USB sem ADB, sem IP de casa e o telemóvel no Wi-Fi de casa fica bloqueado e não confirmado",
            unconfirmed && distrust == Distrust.Unconfirmed && Guard.EgressFor(distrust, android) == null, $"{unconfirmed} {distrust}");
        check("Codex A1: com a depuração USB autorizada ou um IP de casa medido em casa, volta a confiar",
            MainWindow.DistrustNow(false, false, false, MainWindow.MobileUnconfirmed(false, android, false, adbReady: true, false)) == Distrust.None
            && MainWindow.DistrustNow(false, false, false, MainWindow.MobileUnconfirmed(false, android, false, false, homeIpFromHome: true)) == Distrust.None
            && Guard.EgressFor(Distrust.None, android) == android, "");
        check("Codex A1: as outras razões continuam primeiro (o mesmo IP, o Wi-Fi do telemóvel visto pelo ADB)",
            MainWindow.DistrustNow(true, false, false, true) == Distrust.SameIp && MainWindow.DistrustNow(false, true, false, true) == Distrust.PhoneWifi, "");
        check("Codex A1: a frase diz o que fazer e o Phone setup é o botão cheio",
            MainWindow.UnconfirmedBlockedSub("Servers and CS2").Contains("Phone setup") && MainWindow.UnconfirmedBlockedSub("x").Contains("home network")
            && !string.IsNullOrEmpty(DistrustText.Of(Distrust.Unconfirmed))
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Phone.cs")).Contains("_last.Distrust == Distrust.Unconfirmed")
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Protection.cs")).Contains("DistrustNow(_distrust, PhoneWifiOn, _noHome, MobileUnconfirmedNow(_last))"), "");
    }
}
