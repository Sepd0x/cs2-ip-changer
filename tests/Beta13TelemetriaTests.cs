using System.IO;
using SRT;
using SRT.Core;

// 7 nov.: o que o dono trouxe depois de usar a beta.12, parte da telemetria (o que a app manda, o mapa de calor com o scroll e só
// com a app em primeiro plano). Cada correção leva aqui o seu teste, que falhava antes.
static class Beta13TelemetriaTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        check("Beta13 telemetria: o repositório encontra-se", Directory.Exists(Path.Combine(repo, "src")), repo);
        Mapa(check, repo);
        Atualizacao(check, repo);
    }

    /// <summary>
    /// Ponto 9 (1.png): os pontos de Accounts caíam fora do sítio porque a linha era a altura do clique na janela, sem o scroll da
    /// página. O formato novo ("s" + coluna, linha de 40 px no conteúdo da página, tão grossa como a antiga) soma o scroll e não se confunde com o antigo.
    /// </summary>
    static void Mapa(Action<string, bool, string> check, string repo)
    {
        check("Mapa novo: o canto de cima à esquerda da página é s0,0", Telemetry.PageCell(80, 0, 1240) == "s0,0", Telemetry.PageCell(80, 0, 1240));
        check("Mapa novo: a coluna é a mesma da grelha antiga (24 à direita da barra lateral)",
            Telemetry.PageCell(1239.9, 10, 1240) == "s23,0" && Telemetry.PageCell(163, 238, 1240) == "s1,5", Telemetry.PageCell(163, 238, 1240));
        check("Mapa novo: a barra lateral, fora da janela e abaixo das 150 linhas não contam",
            Telemetry.PageCell(40, 10, 1240) == null && Telemetry.PageCell(1240, 10, 1240) == null && Telemetry.PageCell(100, -1, 1240) == null
            && Telemetry.PageCell(100, 6000, 1240) == null && Telemetry.PageCell(100, 10, 60) == null, "");
        // Accounts com o scroll a 700 px: um clique a 500 px da janela está a 1200 px da página (linha 30), não na linha 12
        var y = Telemetry.DocY(500, new[] { Telemetry.ScrollPx(700, true, 0, 0) });
        check("Mapa novo: o clique numa página com scroll cai na linha do conteúdo, com o scroll somado",
            Telemetry.PageCell(400, y, 1240) == "s6,30" && Telemetry.PageCell(400, 500, 1240) == "s6,12", Telemetry.PageCell(400, y, 1240));
        check("Mapa novo: uma lista que rola por itens (Servers) converte os itens em píxeis",
            Telemetry.ScrollPx(3, false, 10, 400) == 120 && Telemetry.ScrollPx(3, false, 0, 400) == 0 && Telemetry.ScrollPx(-5, true, 0, 0) == 0, "");
        check("Mapa novo: o formato não passa no formato antigo (as versões até à beta.12 e o Worker antigo não o confundem)",
            !System.Text.RegularExpressions.Regex.IsMatch(Telemetry.PageCell(400, 300, 1240), @"^(\d{1,2}),(\d{1,2})$"), "");
        // o contrato: as mesmas medidas na app, no Worker e no dashboard
        var page = TelemetryTests.Contract().GetProperty("page");
        check("Mapa novo: as medidas da grelha estão no contrato",
            page.GetProperty("cols").GetInt32() == Telemetry.Cols && page.GetProperty("rowPx").GetDouble() == Telemetry.RowPx && page.GetProperty("rows").GetInt32() == Telemetry.PageRows, "");
        // só com a app em primeiro plano e fora dos painéis por cima da página (Phone setup, as perguntas)
        var t = new DateTime(2026, 11, 7, 1, 0, 0, DateTimeKind.Utc);
        check("Mapa: só conta com a janela ativa e depois do clique que a trouxe para a frente",
            Telemetry.Counts(true, t.AddSeconds(-5), t) && !Telemetry.Counts(false, t.AddSeconds(-5), t) && !Telemetry.Counts(true, t.AddMilliseconds(-200), t), "");
        check("Mapa: um clique num painel por cima da página (Phone setup, uma pergunta) não vai para o mapa da página",
            !Telemetry.Counts(true, t.AddSeconds(-5), t, overlay: true), "");
        var hook = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Telemetry.cs"));
        check("Mapa: a janela principal diz ao mapa qual é e quando há um painel por cima",
            hook.Contains("Telemetry.HeatFrom(this") && hook.Contains("SetupPane") && hook.Contains("Dialog.Visibility"), "");
    }

    /// <summary>
    /// Ponto 3 (zip de 7 nov.): numa atualização a beta.10 mandou upd_accepted ao fechar e a beta.12 mandou-o outra vez (e o fs_):
    /// a cópia nova lia o telemetry.json antes de a antiga o gravar. Agora a antiga manda e grava antes de abrir a nova.
    /// </summary>
    static void Atualizacao(Action<string, bool, string> check, string repo)
    {
        var upd = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Update.cs"));
        int flush = upd.IndexOf("Telemetry.BeforeRestart()", StringComparison.Ordinal), install = upd.IndexOf("Updater.Install(", StringComparison.Ordinal);
        check("Atualização: a telemetria manda e grava antes de a versão nova arrancar (não conta duas vezes)", flush > 0 && install > flush, $"{flush} {install}");
    }
}
