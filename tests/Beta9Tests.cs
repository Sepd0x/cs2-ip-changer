using System.IO;
using SRT.Core;

// beta.9: o mapa de calor dos cliques (grelha 24×16 à direita da barra lateral, como o ui-map.js do dashboard), os cliques de raiva
// e a idade da instalação em bandas
static class Beta9Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        // A captura do dashboard tem 1240×800 e a grelha começa a seguir à barra lateral de 80
        check("Calor: a barra lateral fica fora da grelha", Telemetry.Cell(40, 300, 1240, 800) == null, "");
        check("Calor: o canto de cima à esquerda da página é 0,0", Telemetry.Cell(80, 0, 1240, 800) == "0,0", Telemetry.Cell(80, 0, 1240, 800));
        check("Calor: o canto de baixo à direita é 23,15", Telemetry.Cell(1239.9, 799.9, 1240, 800) == "23,15", Telemetry.Cell(1239.9, 799.9, 1240, 800));
        check("Calor: o Change IP da captura cai na célula certa", Telemetry.Cell(163, 238, 1240, 800) == "1,4", Telemetry.Cell(163, 238, 1240, 800));
        check("Calor: a mesma posição relativa dá a mesma célula numa janela maior", Telemetry.Cell(80 + (163 - 80) * 1.5, 238 * 1.5, 80 + 1160 * 1.5, 1200) == "1,4", "");
        check("Calor: fora da janela ou janela sem tamanho dá nada",
            Telemetry.Cell(1240, 10, 1240, 800) == null && Telemetry.Cell(100, -1, 1240, 800) == null && Telemetry.Cell(100, 10, 60, 800) == null && Telemetry.Cell(100, 10, 1240, 0) == null, "");

        var t = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        var two = new List<(DateTime, string)> { (t.AddMilliseconds(-600), "3,5"), (t.AddMilliseconds(-300), "3,5") };
        check("Raiva: o 3.º clique na mesma célula em 1 s", Telemetry.IsRage(two, t, "3,5"), "");
        check("Raiva: noutra célula não conta", !Telemetry.IsRage(two, t, "4,5"), "");
        check("Raiva: 2 cliques não chegam", !Telemetry.IsRage(two.Take(1), t, "3,5"), "");
        check("Raiva: cliques com mais de 1 s não contam", !Telemetry.IsRage(new List<(DateTime, string)> { (t.AddSeconds(-2), "3,5"), (t.AddMilliseconds(-1100), "3,5") }, t, "3,5"), "");

        var bands = new[] { (0, "0"), (1, "1"), (2, "2-3"), (3, "2-3"), (4, "4-6"), (6, "4-6"), (7, "7-13"), (13, "7-13"), (14, "14-29"), (29, "14-29"), (30, "30+"), (400, "30+"), (-3, "0") };
        foreach (var (d, b) in bands) check($"Idade: {d} dias vai na banda {b}", Telemetry.AgeBand(d) == b, Telemetry.AgeBand(d));
        // ---------------- fase 2: partidas, sessões, scroll, definições, dias ativos e ambiente sem ID
        var lag = new NetReport { PingP50 = 62, JitterUp = 23, JitterDown = 4, UpLate = 58.2, Dropped = 3 };
        var mc = Telemetry.MatchCounts(lag, valve: false);
        check("Partida: comunitária com lag e cada número na sua banda",
            mc.SequenceEqual(new[] { "m_comm", "m_comm_lag", "ping_50_80", "jit_20_40", "jitd_lt5", "late_gt10", "loss_1_10" }), string.Join(",", mc));
        var ok = Telemetry.MatchCounts(new NetReport { PingP50 = 20, UpLate = 0 }, valve: true);
        check("Partida: Valve sem lag, sem jitter lido não manda banda de jitter", ok.SequenceEqual(new[] { "m_valve", "ping_lt30", "late_0", "loss_0" }), string.Join(",", ok));
        check("Partida: nomes que o servidor aceita (letras, números, _)", mc.Concat(ok).All(n => System.Text.RegularExpressions.Regex.IsMatch(n, "^[a-z0-9_]{1,48}$")), "");
        check("Bandas: ping, jitter, atrasadas, perdidos nas pontas",
            Telemetry.PingBand(29) == "lt30" && Telemetry.PingBand(30) == "30_50" && Telemetry.PingBand(500) == "gt120" && Telemetry.JitterBand(40) == "gt40"
            && Telemetry.LateBand(0.5) == "lt1" && Telemetry.LateBand(3) == "3_10" && Telemetry.LossBand(10) == "1_10" && Telemetry.LossBand(101) == "gt100", "");
        check("Bandas: sessão, arranque e até Protected",
            Telemetry.SessionBand(TimeSpan.FromMinutes(4)) == "lt5" && Telemetry.SessionBand(TimeSpan.FromMinutes(90)) == "1_2h" && Telemetry.SessionBand(TimeSpan.FromHours(9)) == "gt2h"
            && Telemetry.StartupBand(TimeSpan.FromSeconds(1.5)) == "1_2" && Telemetry.ProtectedBand(TimeSpan.FromSeconds(45)) == "30_60" && Telemetry.ProtectedBand(TimeSpan.FromMinutes(5)) == "gt3m", "");
        check("Scroll: bandas de 25%", Telemetry.ScrollBand(0) == "0" && Telemetry.ScrollBand(0.3) == "25" && Telemetry.ScrollBand(0.74) == "50" && Telemetry.ScrollBand(0.8) == "75" && Telemetry.ScrollBand(1) == "100", "");

        var changed = Telemetry.Changed(new SRT.Settings { CloseToTray = true, Mode = "all", Cs2Path = @"C:\Users\Nome\cs2" }, new SRT.Settings());
        check("Definições: só os nomes da lista que mudaram (o caminho do CS2 nunca)", changed.SequenceEqual(new[] { "CloseToTray", "Mode" }), string.Join(",", changed));
        check("Definições: as de fábrica não mandam nada", Telemetry.Changed(new SRT.Settings(), new SRT.Settings()).Count == 0, "");
        check("Definições: todas as da lista existem nas Settings", Telemetry.SettingNames.All(n => typeof(SRT.Settings).GetProperty(n) != null), "");

        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        check("Dias ativos: conta só os últimos 7, sem repetir", Telemetry.ActiveDays(new[] { "2026-11-03", "2026-11-03", "2026-11-01", "2026-10-28", "2026-10-27", "lixo" }, now) == 3, "");

        check("Ambiente: marca de uma lista curta, nunca o modelo", Telemetry.BrandOf("samsung") == "samsung" && Telemetry.BrandOf("Redmi") == "xiaomi" && Telemetry.BrandOf("SM-S918B") == "other" && Telemetry.BrandOf("") == null, "");
        check("Ambiente: Android só o número principal", Telemetry.AndroidOf("14") == "14" && Telemetry.AndroidOf("8.1.0") == "8" && Telemetry.AndroidOf("abc") == null, "");
        check("Ambiente: janela e escala em bandas", Telemetry.WindowBand(1240) == "m" && Telemetry.WindowBand(1020) == "s" && Telemetry.WindowBand(1920) == "l" && Telemetry.ScaleBand(1.25) == "125" && Telemetry.ScaleBand(2) == "175p", "");
        var phone = new PhoneInfo("R58N", "device", "SM-S918B", "MEO", "5G", 80, Brand: "samsung", Android: "15");
        var env = Telemetry.EnvBody("pt", 1240, 1.5, 26100, phone, false);
        check("Ambiente: corpo sem ID, sem país, sem modelo, operadora ou série",
            !env.Contains("\"id\"") && !env.Contains("country") && !env.Contains("SM-S918B") && !env.Contains("MEO") && !env.Contains("R58N")
            && env.Contains("\"brand\":\"samsung\"") && env.Contains("\"android\":\"15\"") && env.Contains("\"net\":\"5g\"") && env.Contains("\"scale\":\"150\""), env);
        check("Ambiente: idioma estranho vai vazio", Telemetry.EnvBody("pt-PT!", 0, 0, 1, null, false).Contains("\"lang\":null"), "");

        // ---------------- fase 2, 2.ª ronda: trocas, quedas, dados, Rejoin, versão de origem, raiva falsa, cabo, Servers
        var chg = Telemetry.ChangeCounts(airplane: true, TimeSpan.FromSeconds(14), 2, QualityLevel.Fair, 1);
        check("Troca: método, tempo, tentativas, qualidade e repetição por jitter em bandas", chg.SequenceEqual(new[] { "chg_air", "chgt_10_20", "chga_2", "chgq_fair", "chg_retry" }), string.Join(",", chg));
        check("Troca: sem medição não manda qualidade", Telemetry.ChangeCounts(false, TimeSpan.FromSeconds(3), 6, QualityLevel.Unknown, 0).SequenceEqual(new[] { "chg_data", "chgt_lt5", "chga_4p" }), "");
        check("Bandas: dados por dia e quedas", Telemetry.DataBand(100_000_000) == "lt250m" && Telemetry.DataBand(1_500_000_000) == "1_2g" && Telemetry.DataBand(9_000_000_000) == "gt2g"
            && Telemetry.DropsBand(0) == "0" && Telemetry.DropsBand(2) == "2" && Telemetry.DropsBand(7) == "3p", "");
        check("Versão de origem como nome de contador", Telemetry.VersionName("0.1.0-beta.8") == "0_1_0_beta_8" && Telemetry.VersionName("1.0.0") == "1_0_0" && Telemetry.VersionName("x; drop") == null, "");
        var act = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        check("Raiva falsa: o clique que traz a janela para a frente não conta", !Telemetry.Counts(true, act, act.AddMilliseconds(200)), "");
        check("Raiva falsa: com a janela sem foco não conta", !Telemetry.Counts(false, act.AddMinutes(-5), act), "");
        check("Raiva falsa: com foco, clique normal conta", Telemetry.Counts(true, act.AddSeconds(-3), act), "");
        var drops = new[] { act.AddMinutes(-9), act.AddMinutes(-4), act };
        check("Cabo: 3 quedas em 10 min avisam", SRT.MainWindow.CableHint(drops, act), "");
        check("Cabo: 3 quedas espalhadas por mais de 10 min não avisam", !SRT.MainWindow.CableHint(new[] { act.AddMinutes(-15), act.AddMinutes(-4), act }, act), "");
        check("Servers: o botão diz o que vai perguntar", SRT.MainWindow.RefreshLabel(0) == "Refresh all" && SRT.MainWindow.RefreshLabel(1) == "Refresh 1" && SRT.MainWindow.RefreshLabel(3) == "Refresh 3", "");

        check("Aviso: já não promete que nada liga um dia ao seguinte (a idade em bandas liga à semana da instalação)",
            !Telemetry.NoticeText.Contains("never links") && Telemetry.NoticeText.Contains("how long ago"), "");

        // ---------------- 4 nov.: verificações a vermelho no relatório e o teste de fugas
        var titles = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml.cs")),
            @"_checks\.Add\(\w+ = new CheckItem \{ Title = ""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        check("Checks: cada linha do painel tem nome curto para o relatório", titles.Count >= 11 && titles.All(Telemetry.CheckKeys.ContainsKey),
            string.Join(", ", titles.Where(x => !Telemetry.CheckKeys.ContainsKey(x))));
        // Beta.12: o contrato (worker/stats/contract.json) é a lista que o Worker lê
        var workerChk = TelemetryTests.Contract().GetProperty("bands").GetProperty("chk").EnumerateArray().Select(x => x.GetString()).ToList();
        check("Checks: os nomes curtos são os mesmos no Worker (família chk fechada)", Telemetry.CheckKeys.Values.OrderBy(k => k).SequenceEqual(workerChk.OrderBy(k => k)), string.Join(",", workerChk));
        var red = Telemetry.RedChecks(new[] { "Windows Firewall", "Mobile data", "Windows Firewall", "texto inventado", null });
        check("Checks: só os nomes conhecidos, uma vez cada, nunca o texto", red.SequenceEqual(new[] { "chk_firewall", "chk_data" }), string.Join(",", red));
        check("Checks: sem linhas a vermelho não manda nada", Telemetry.RedChecks(null).Count == 0 && Telemetry.RedChecks(Array.Empty<string>()).Count == 0, "");
        // ---------------- 4 nov.: Accounts em linhas (sugerido a partir de 12 contas até ele escolher)
        check("Contas: linhas sugeridas a partir de 12 contas, só enquanto não escolheu",
            !SRT.MainWindow.SuggestRows(11, null) && SRT.MainWindow.SuggestRows(12, null) && !SRT.MainWindow.SuggestRows(40, "cards") && !SRT.MainWindow.SuggestRows(40, "rows"), "");
        var xaml = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml"));
        check("Contas: o cartão e a linha usam o mesmo menu e têm Log in, estrela e rotação",
            System.Text.RegularExpressions.Regex.Matches(xaml, @"ContextMenu=""\{StaticResource AccMenu\}""").Count == 2 && xaml.Contains("x:Key=\"AccountRow\"")
            && xaml[xaml.IndexOf("x:Key=\"AccountRow\"")..xaml.IndexOf("x:Key=\"AccCardsPanel\"")] is var row
            && row.Contains("Click=\"AccSwitch_Click\"") && row.Contains("Click=\"AccFav_Click\"") && row.Contains("Click=\"AccToggleQuick_Click\""), "");
        check("Contas: a vista escolhida fica nas definições", new SRT.Settings().AccountsView == null && typeof(SRT.Settings).GetProperty("AccountsView") != null, "");
        // ---------------- 4 nov.: servidor cheio na lista, mas com lugares de espectador
        var full = SRT.MainWindow.FullText("EU Retakes", "10/10");
        check("Cheio: oferece Join anyway e diz que entra como espectador", full.Contains("Join anyway") && full.Contains("spectator") && full.Contains("10/10"), "");
        var srvSrc = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Servers.cs"));
        check("Cheio: Escape não entra; o 2.º botão entra na mesma",
            srvSrc.Contains("\"Wait for a slot\", \"Join anyway\")) { StartSlotWait(s); return true; }") && srvSrc.Contains("if (!_dialogCancelClicked) return false;"), "");
        // ---------------- 4 nov. (o192): as caixas do Windows antes do WPF ficam só onde a janela da app não pode carregar
        var prog = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Program.cs"));
        var boxes = System.Text.RegularExpressions.Regex.Matches(prog, @"MessageBoxW\(IntPtr\.Zero").Count;
        check("Caixas do Windows: só as 5 de antes do WPF, e nenhuma MessageBox.Show na app",
            boxes == 5 && !Directory.GetFiles(Path.Combine(SecurityTests.Repo(), "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).Any(f => File.ReadAllText(f).Contains("MessageBox.Show(")), boxes.ToString());
        // ---------------- 4 nov.: Report a problem (issue pré-preenchido; só versões e modo no link)
        var issue = SRT.MainWindow.IssueUrl("0.1.0-beta.9", "10.0.26300.0", "servers");
        var issueBody = Uri.UnescapeDataString(issue);
        check("Report a problem: abre um issue novo do repo com as versões e o modo",
            issue.StartsWith("https://github.com/Sepd0x/cs2-ip-changer/issues/new?title=") && issueBody.Contains("App: 0.1.0-beta.9") && issueBody.Contains("Windows: 10.0.26300.0") && issueBody.Contains("Mode: servers"), issue);
        var bad = Uri.UnescapeDataString(SRT.MainWindow.IssueUrl("1.0 192.168.1.20 & me", "x\nIP: 203.0.113.9", null));
        check("Report a problem: nada mais entra no link (espaços, quebras e & caem; sem modo diz ?)",
            !bad.Contains("192.168.1.20 ") && !bad.Contains("& me") && !bad.Contains("IP: 203") && bad.Contains("Mode: ?"), bad);
        // ---------------- 4 nov.: verificação do 1.º arranque (Steam e CS2; o telemóvel tem o Phone setup)
        check("1.º arranque: com Steam e CS2 não pergunta nada", SRT.MainWindow.StartProblems(true, true).Count == 0, "");
        check("1.º arranque: sem Steam diz o que fazer (e não fala do CS2)", SRT.MainWindow.StartProblems(false, false) is [var ns] && ns.Contains("install Steam"), "");
        check("1.º arranque: sem CS2 diz para o instalar no Steam", SRT.MainWindow.StartProblems(true, false) is [var nc2] && nc2.Contains("Install it in Steam"), "");
        // ---------------- 4 nov.: as listas que crescem sem limite continuam virtualizadas (Servers; o Log guarda 600 linhas)
        var serverList = xaml[xaml.IndexOf("<ListBox x:Name=\"ServerList\"")..];
        var logList = xaml[xaml.IndexOf("<ListBox x:Name=\"LogList\"")..];
        check("Listas grandes: Servers e Log virtualizam",
            serverList[..serverList.IndexOf('>')].Contains("VirtualizingPanel.IsVirtualizing=\"True\"") && !logList[..logList.IndexOf('>')].Contains("IsVirtualizing=\"False\""), "");
        // ---------------- 4 nov.: o mapa do dashboard usa os nomes reais dos handlers (antes, NewIpNext, Join, Apply… davam sempre 0)
        var uiMap = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "worker", "stats", "dashboard", "ui-map.js"));
        var allXaml = xaml + string.Concat(Directory.GetFiles(Path.Combine(SecurityTests.Repo(), "src", "Views"), "*.cs").Select(File.ReadAllText));
        var ids = System.Text.RegularExpressions.Regex.Matches(uiMap, @"\{ id: '(\w+)', label:").Select(m => m.Groups[1].Value).Distinct().ToList();
        var missing = ids.Where(id => !allXaml.Contains($"Click=\"{id}_Click\"") && !allXaml.Contains($"Click=\"{id}\"") && !allXaml.Contains($"{id}_Click;")).ToList();
        check("Dashboard: cada botão do ui-map é um handler de Click que a app conta", ids.Count > 60 && missing.Count == 0, string.Join(", ", missing));
        var leakSrc = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Protection.cs"));
        check("Teste de fugas: conta passou ou falhou depois do No phone mode (que não tem o que testar)",
            leakSrc.Contains("Telemetry.LeakTest(bad.Count == 0)") && leakSrc.IndexOf("Telemetry.LeakTest(") > leakSrc.IndexOf("if (_s.NoPhone)", leakSrc.IndexOf("async Task<bool> LeakTest()")), "");
    }
}
