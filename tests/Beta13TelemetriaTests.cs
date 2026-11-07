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
        CliqueNaJanela(check);
        Entrada(check, repo);
    }

    /// <summary>
    /// Verificação de 7 nov.: o MouseDown passa pelo HeatClick, que se testa aqui numa janela WPF a sério (STA, sem a mostrar):
    /// a célula nova com o scroll da página, só a janela principal, nada com um painel por cima, e as listas numa caixa (Previous
    /// IPs, as listas das Configs) sem o seu scroll somado. Cada verificação falha com a linha antiga correspondente.
    /// </summary>
    static void CliqueNaJanela(Action<string, bool, string> check)
    {
        string page = null, other = null, overlaid = null, inBox = null, inList = null, popup = null;
        List<double> boxScroll = null;
        Exception err = null;
        var t = new Thread(() =>
        {
            try
            {
                System.Windows.Controls.Border Row(double h) => new() { Height = h };
                // a list in a box (MaxHeight 120, like Previous IPs) half way down a page that scrolls
                var boxRows = new System.Windows.Controls.StackPanel();
                for (var i = 0; i < 20; i++) boxRows.Children.Add(Row(30));
                var box = new System.Windows.Controls.ScrollViewer { MaxHeight = 120, Content = boxRows, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
                var target = Row(40);
                var pageRows = new System.Windows.Controls.StackPanel();
                pageRows.Children.Add(Row(900)); pageRows.Children.Add(box); pageRows.Children.Add(target); pageRows.Children.Add(Row(1500));
                var pageSv = new System.Windows.Controls.ScrollViewer { Content = pageRows, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
                var root = new System.Windows.Controls.Grid();
                root.Children.Add(pageSv);
                var w = new System.Windows.Window { Content = root, Width = 1240, Height = 800 };
                root.Measure(new System.Windows.Size(1240, 800)); root.Arrange(new System.Windows.Rect(0, 0, 1240, 800));
                pageSv.ScrollToVerticalOffset(700); box.ScrollToVerticalOffset(200);
                root.UpdateLayout();
                var overlay = false;
                Telemetry.HeatFrom(w, () => overlay);
                Telemetry.Activated();
                var later = DateTime.UtcNow.AddSeconds(2);
                // a click 500 px down the window, on the row under the box: 500 + 700 of page scroll = 1200 → row 30
                page = Telemetry.HeatClick(w, target, new System.Windows.Point(400, 500), true, later);
                if (page == null) err = new Exception($"dbg anc={root.IsAncestorOf(target)} counts={Telemetry.Counts(true, DateTime.UtcNow, later)} content={w.Content is System.Windows.FrameworkElement} w={root.ActualWidth} sc={string.Join("+", Telemetry.ScrolledAbove(target, w))} app={System.Windows.Application.Current != null} hw={ReferenceEquals(typeof(Telemetry).GetField("_heatWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null), w)} act={typeof(Telemetry).GetField("_activatedAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null):o} later={later:o}");
                other = Telemetry.HeatClick(new System.Windows.Window { Content = new System.Windows.Controls.Grid() }, target, new System.Windows.Point(400, 500), true, later);
                overlay = true;
                overlaid = Telemetry.HeatClick(w, target, new System.Windows.Point(400, 500), true, later);
                overlay = false;
                // a click on a row of the box scrolled 200: only the page's 700 is added (the capture shows the box from its start)
                var boxRow = (System.Windows.Controls.Border)boxRows.Children[10];
                boxScroll = Telemetry.ScrolledAbove(boxRow, w);
                inBox = Telemetry.HeatClick(w, boxRow, new System.Windows.Point(400, 300), true, later);
                // a list that fills the page (no MaxHeight, like Servers or Log) does add its scroll
                inList = Telemetry.PageCell(400, Telemetry.DocY(300, Telemetry.ScrolledAbove(target, w)), 1240);
                // an element outside the window's tree (a dropdown's open list) is not the page
                popup = Telemetry.HeatClick(w, new System.Windows.Controls.Border(), new System.Windows.Point(400, 300), true, later);
                Telemetry.HeatFrom(null, null);
            }
            catch (Exception e) { err = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        check("Mapa (janela): o clique numa página com scroll dá a célula nova com o scroll somado", err == null && page == "s6,30", $"{page} {err?.Message}");
        check("Mapa (janela): outra janela que não a principal não conta", err == null && other == null, other ?? "");
        check("Mapa (janela): com o Phone setup ou uma pergunta por cima não conta", err == null && overlaid == null, overlaid ?? "");
        check("Mapa (janela): uma lista numa caixa (Previous IPs, Configs) não soma o seu scroll, só o da página",
            err == null && boxScroll != null && boxScroll.SequenceEqual(new[] { 700.0 }) && inBox == "s6,25", $"{string.Join("+", boxScroll ?? new())} {inBox}");
        check("Mapa (janela): fora da árvore da janela (a lista aberta de uma ComboBox) não conta", err == null && popup == null, popup ?? "");
        check("Mapa (janela): o MouseDown passa pelo HeatClick", File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Core", "Telemetry.cs")) is var src
            && src[src.IndexOf("public static void MouseDown(", StringComparison.Ordinal)..].Split("static void AddHeat")[0].Contains("HeatClick("), inList ?? "");
    }

    /// <summary>
    /// Verificação de 7 nov.: depois do BeforeRestart a cópia velha não conta nem grava (HandOver), e se o Install falhar volta a
    /// contar (TakeBack), para o upd_failed e o erro no Log chegarem.
    /// </summary>
    static void Entrada(Action<string, bool, string> check, string repo)
    {
        check("Atualização: depois do BeforeRestart a cópia velha deixa de contar e fica marcada",
            Telemetry.HandOver(true, false) == (false, true) && Telemetry.HandOver(false, false) == (false, false), "");
        check("Atualização: se a instalação falhar, volta a contar só quem estava ligado",
            Telemetry.TakeBack(false, true) == (true, false) && Telemetry.TakeBack(false, false) == (false, false) && Telemetry.TakeBack(true, false) == (true, false), "");
        var src = File.ReadAllText(Path.Combine(repo, "src", "Core", "Telemetry.cs"));
        var before = src[src.IndexOf("public static void BeforeRestart()", StringComparison.Ordinal)..];
        before = before[..before.IndexOf('}')];
        check("Atualização: o BeforeRestart usa o HandOver e espera por um envio a meio", before.Contains("HandOver(_on, _handedOver)") && before.Contains("_busy"), "");
        var sent = src[src.IndexOf("static void Sent(Snapshot s)", StringComparison.Ordinal)..];
        check("Atualização: uma resposta que chega depois não grava por cima da cópia nova", sent[..sent.IndexOf("lock (Gate)", StringComparison.Ordinal)].Contains("if (!_on) return;"), "");
        var upd = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Update.cs"));
        var c = upd[upd.IndexOf("catch (Exception ex)", upd.IndexOf("Telemetry.BeforeRestart()", StringComparison.Ordinal), StringComparison.Ordinal)..];
        check("Atualização: se o Install falhar, a telemetria volta antes de contar upd_failed e o erro",
            c.IndexOf("Telemetry.AfterFailedRestart()", StringComparison.Ordinal) is var a && a > 0 && a < c.IndexOf("Log.Error(", StringComparison.Ordinal) && a < c.IndexOf("upd_failed", StringComparison.Ordinal), "");
        // 1.ª entrada num servidor: a app conta as entradas (só quantas, nunca qual)
        var kick = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Kick.cs"));
        var con = kick[kick.IndexOf("if (Cs2Console.IsConnectLine(line))", StringComparison.Ordinal)..];
        check("Entrada num servidor: cada entrada conta join, sem o servidor", con[..con.IndexOf("continue;", StringComparison.Ordinal)].Contains("Telemetry.Count(\"join\")"), "");
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
