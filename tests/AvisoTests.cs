using System.IO;
using SRT;
using SRT.Core;

// beta.11 (6 nov.): o aviso das estatísticas curto (crítico 14 dos utilizadores simulados) com a frase do art. 13 do RGPD na linha
// "Kept by"; a lista inteira de antes fica em See the full list. NoticeVersion 4: quem aceitou o 3 vê-o uma vez mais.
static class AvisoTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray()));

        check("Aviso: NoticeVersion é 4", Telemetry.NoticeVersion == 4, Telemetry.NoticeVersion.ToString());

        // o texto curto: Sent, Never sent e Kept by, cada um na sua linha
        var curto = Telemetry.NoticeShort;
        var linhas = curto.Split('\n');
        check("Aviso: o curto tem as linhas Sent, Never sent e Kept by", linhas.Any(l => l.StartsWith("Sent: ")) && linhas.Any(l => l.StartsWith("Never sent: "))
            && linhas.Any(l => l.StartsWith("Kept by the author (Sepd0x on GitHub)")), "");
        var kept = linhas.FirstOrDefault(l => l.StartsWith("Kept by")) ?? "";
        check("Aviso: Kept by diz Cloudflare, 12 meses, Delete my stats, as issues e a autoridade de proteção de dados",
            kept.Contains("Cloudflare") && kept.Contains("12 months") && kept.Contains("Settings > Delete my stats") && kept.Contains("GitHub issues")
            && kept.Contains("data protection authority") && kept.Contains("because you accept this notice") && kept.Contains("only to fix bugs"), kept);
        check("Aviso: o curto diz obrigatório, quando envia e o Quit", curto.Contains("required while it is in beta") && curto.Contains("every 30 minutes")
            && curto.Contains("Quit closes the app. Nothing on your PC changes."), "");
        check("Aviso: o curto nunca chama anónimo ao relatório com ID", !curto.Contains("anonymous", StringComparison.OrdinalIgnoreCase), "");
        // cada categoria da lista de antes tem a sua palavra no curto
        var sent = linhas.First(l => l.StartsWith("Sent: "));
        var faltam = new[] { "random ID", "app and Windows versions", "country", "screens", "buttons", "settings", "ping", "errors", "phone's brand", "Android version" }
            .Where(w => !sent.Contains(w)).ToList();
        check("Aviso: o curto nomeia todas as categorias", faltam.Count == 0, string.Join(", ", faltam));
        var never = linhas.First(l => l.StartsWith("Never sent: "));
        check("Aviso: o curto diz o que nunca vai, como o de antes", new[] { "IPs", "servers", "accounts", "Steam names or IDs", "carrier", "anything you type" }.All(never.Contains), never);

        // a lista inteira continua a um clique: os 7 pontos, iguais aos do texto de antes
        var lista = Telemetry.NoticeList.Split('\n');
        check("Aviso: a lista inteira tem os 7 pontos", lista.Length == 7 && lista.All(l => l.StartsWith("•  ")), lista.Length.ToString());
        check("Aviso: cada ponto da lista está no texto de antes", lista.All(Telemetry.NoticeText.Contains), "");
        var app = Src("App.xaml.cs");
        check("Aviso: a janela leva a lista inteira em See the full list", app.Contains("\"Accept\", \"Quit\", more: Telemetry.NoticeList"), "");
        var xaml = Src("Views", "NoticeWindow.xaml");
        var cs = Src("Views", "NoticeWindow.xaml.cs");
        check("Aviso: o botão See the full list mostra e esconde a lista", xaml.Contains("Content=\"See the full list\"") && xaml.Contains("x:Name=\"MoreText\"")
            && cs.Contains("MoreText.Text = more") && cs.Contains("\"Hide the full list\""), "");
        check("Aviso: Accept e Quit como antes (Enter aceita, Escape sai)", xaml.Contains("x:Name=\"OkBtn\"") && xaml.Contains("IsDefault=\"True\"")
            && xaml.Contains("x:Name=\"OtherBtn\"") && xaml.Contains("IsCancel=\"True\"") && cs.Contains("void Other_Click(object sender, RoutedEventArgs e) { Accepted = false; Close(); }"), "");

        // quem aceitou o aviso 3 (beta.10) vê o curto uma vez mais, com o que mudou e só isso
        var tmp = Path.Combine(Path.GetTempPath(), "srt-aviso-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var b10 = Path.Combine(tmp, "s-b10.json");
            File.WriteAllText(b10, "{ \"StatsAccepted\": true, \"StatsNotice\": 3 }");
            var asked = 0;
            check("Aviso: aceite na beta.10 (aviso 3) volta a ver o aviso uma vez", App.StatsNoticeIn(b10) == 3 && !App.StatsAcceptedIn(b10)
                && App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, b10) && asked == 1 && App.StatsAskedAgain, "");
            var corpo = App.StatsNoticeBody(false, 3);
            var mudou = Telemetry.NoticeChangedFor(3);
            check("Aviso: com o que mudou (mais curto, quem guarda, quanto tempo, como perguntar)", corpo.StartsWith(Telemetry.NoticeShort) && corpo.Contains(mudou)
                && mudou.Contains("shorter") && mudou.Contains("who keeps the stats") && mudou.Contains("how long") && mudou.Contains("how to ask"), mudou);
            check("Aviso: quem aceitou o 3 não relê os 30 minutos nem o ID", !mudou.Contains("every 30 minutes") && !mudou.Contains("kept for this install"), "");
            check("Aviso: quem aceitou antes da beta.9 lê tudo o que mudou", Telemetry.NoticeChangedFor(1).Contains("every 30 minutes") && Telemetry.NoticeChangedFor(1).Contains("kept for this install")
                && Telemetry.NoticeChangedFor(2).Contains("every 30 minutes") && !Telemetry.NoticeChangedFor(2).Contains("kept for this install"), "");
            check("Aviso: 1.º arranque sem o que mudou; depois de uma atualização com o Quit", App.StatsNoticeBody(false, 0) == Telemetry.NoticeShort
                && App.StatsNoticeBody(true, 3).EndsWith(Telemetry.NoticeAfterUpdate), "");
            var atual = Path.Combine(tmp, "s-b11.json");
            File.WriteAllText(atual, $"{{ \"StatsAccepted\": true, \"StatsNotice\": {Telemetry.NoticeVersion} }}");
            check("Aviso: quem aceitou o 4 não volta a ver", App.StatsAcceptedIn(atual) && Telemetry.NoticeChangedFor(4) == ""
                && App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, atual) && asked == 1, "");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }

        // a build de gravação continua sem enviar nada nem perguntar
        check("Aviso: a build de gravação não pergunta nem envia", app.Contains("if (args.Contains(\"--demo\") || Rec.On) return true;")
            && Src("Core", "Telemetry.cs").Contains("if (demo || Rec.On) return;"), "");
    }
}
