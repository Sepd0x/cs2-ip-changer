#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 26 nov.: as correções da triagem dos críticos (publish/plano-1-0-0/22-criticos-26nov.md), um teste por correção.
static class Criticos26NovTests
{
    static string Xaml() => File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml"));

    public static void Run(Action<string, bool, string> check)
    {
        var xaml = Xaml();

        // 1. Servers pt-BR: "Proteção desligada" corria por baixo do botão Join
        check("Críticos 1: com a proteção desligada a Rota diz Off, curto", MainWindow.HomeRouteText("off") == "Off" && MainWindow.HomeRouteText("game") == "Home IP", "");
        var row = xaml.Substring(xaml.IndexOf("x:Key=\"ServerRow\""), 12000);
        var route = row.Substring(row.IndexOf("{Binding RouteBrush}") - 400, 500);
        check("Críticos 1: a Rota corta com reticências antes do Join (DockPanel com margem, não StackPanel)",
            route.Contains("<DockPanel Grid.Column=\"5\"") && route.Contains("Margin=\"0,0,8,0\""), "");

        // 2. pt-BR: "Entrar" para três ações; e o Join cortado ("Entra") na coluna de 56 px
        var pt = Lang("pt-BR");
        check("Críticos 2: pt-BR com uma palavra para cada ação (Join, Log in, Sign in)",
            pt["Join"] == "Conectar" && pt["Log in"] == "Entrar" && pt["Sign in"] == "Fazer login", $"{pt["Join"]} / {pt["Log in"]} / {pt["Sign in"]}");
        foreach (var code in new[] { "pt-BR", "es", "ru", "tr" })
        {
            var t = Lang(code);
            check($"Críticos 2: {code} sem a mesma palavra para Join e Sign in", t["Join"] != t["Sign in"], t["Join"]);
        }
        check("Críticos 2: a coluna do Join tem 80 px na linha e no cabeçalho",
            Count(xaml, "<ColumnDefinition Width=\"112\"/>\n                    <!-- 80, not 56") == 1 && Count(xaml, "<ColumnDefinition Width=\"112\"/>\n                                        <ColumnDefinition Width=\"80\"/>") == 1, "");

        // 3. "Before you start": aspeto do Windows clássico e só um OK
        check("Críticos 3: com Steam e sem CS2 o botão abre a instalação do CS2 no Steam",
            MainWindow.StartProblemButtons(true) == ("Install CS2 in Steam", "Later") && MainWindow.StartProblemButtons(false) == ("OK", null)
            && MainWindow.Cs2InstallUrl == "steam://install/730", "");
        var theme = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Theme.xaml"));
        var dialog = theme.Substring(theme.IndexOf("x:Key=\"DialogGroup\""), 1600);
        check("Críticos 3: o título do diálogo dentro da caixa, 16 px Display, sem a etiqueta em cima da borda",
            !dialog.Contains("#040404") && dialog.Contains("TextElement.FontSize=\"16\"") && dialog.Contains("DisplayFont"), "");
        foreach (var pane in new[] { "SetupPane", "StartPane", "Dialog", "TourPane" })
        {
            var at = xaml.IndexOf($"<Grid x:Name=\"{pane}\"");
            check($"Críticos 3: {pane} com Look.Round (botões em pill, o Primary cheio)", at > 0 && xaml.Substring(at, 300).Split('>')[0].Contains("local:Look.Round=\"True\""), pane);
        }

        // 4. Contas: o "Checking 1/4…" no botão partia os filtros; Regiões: países em inglês e cortados
        check("Críticos 4: o progresso do Refresh vai para a linha do título", MainWindow.CheckingText(1, 4) == " · Checking 1/4…" && xaml.Contains("x:Name=\"AccChecking\"")
            && !Src("Views", "MainWindow.Accounts.cs").Contains("AccRefreshText.Text = Loc.F(\"Checking"), MainWindow.CheckingText(1, 4));
        var continentOf = (Dictionary<string, string>)typeof(Regions).GetField("ContinentOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var regionsCs = Src("Core", "Network", "Regions.cs");
        var noName = continentOf.Keys.Where(c => !regionsCs.Contains($"\"{c}\" => Loc.T(\"{c}\")")).ToList();
        check("Críticos 4: cada país das regiões passa pela tradução", noName.Count == 0, string.Join(", ", noName));
        var noPt = continentOf.Keys.Where(c => !pt.ContainsKey(c)).ToList();
        check("Críticos 4: os países têm nome em pt-BR", noPt.Count == 0 && pt["Spain"] == "Espanha" && pt["United States"] == "Estados Unidos", string.Join(", ", noPt));
        var va = new RegionItem { Info = new RegionInfo("iad", "Ashburn", "Virginia", "United States", "North America", new[] { "iad" }, new[] { "1.2.3.4" }) };
        check("Críticos 4: o estado dos EUA só no tooltip", va.Country == "United States" && va.Tip.StartsWith("Ashburn, Virginia · "), va.Country + " | " + va.Tip);

        // 5. Connection: o nome da operadora em 4 linhas; os botões de palavra 8 px dentro da borda
        check("Críticos 5: o nome da operadora como se diz",
            Net.CarrierName("MEO - SERVICOS DE COMUNICACOES E MULTIMEDIA S.A.") == "MEO" && Net.CarrierName("Deutsche Telekom AG") == "Deutsche Telekom"
            && Net.CarrierName("Telefonica Germany") == "Telefonica Germany" && Net.CarrierName("VODAFONE PORTUGAL") == "Vodafone Portugal"
            && Net.CarrierName("Orange S.A.") == "Orange" && Net.CarrierName(null) == null,
            $"{Net.CarrierName("MEO - SERVICOS DE COMUNICACOES E MULTIMEDIA S.A.")} | {Net.CarrierName("VODAFONE PORTUGAL")} | {Net.CarrierName("Orange S.A.")}");
        check("Críticos 5: a linha da operadora usa o nome curto", new Net.IpInfo("MEO - SERVICOS DE COMUNICACOES E MULTIMEDIA S.A.", "Sesimbra", "Portugal", false, false).Short == "MEO · Sesimbra · Portugal", "");
        check("Críticos 5: a linha de casa em duas linhas no máximo", Has(xaml, "x:Name=\"HomeLine\"", "MaxHeight=\"33.6\"") && Has(xaml, "x:Name=\"HomeLine\"", "TextTrimming=\"CharacterEllipsis\""), "");
        foreach (var b in new[] { "TurnOffBtn", "ChecksToggle", "ClearHistoryBtn", "RegionsRefreshBtn" })
            check($"Críticos 5: {b} com o texto rente à borda direita (-8 px)", Has(xaml, $"x:Name=\"{b}\"", ",-8,0\""), b);
        check("Críticos 5: Turn off protection continua a perguntar antes", Src("Views", "MainWindow.Settings.cs").Contains("Ask(Loc.T(\"Turn off protection?\")"), "");

        // 6. Configs: a mira cortada a meio do hex; interruptores acesos com a Sync desligada; o chevron do lado errado
        check("Críticos 6: uma cor personalizada diz-se por nome",
            Cs2Config.ColorWord(0, 255, 0) == "green" && Cs2Config.ColorWord(0x50, 0xFF, 0) == "green" && Cs2Config.ColorWord(255, 255, 0) == "yellow"
            && Cs2Config.ColorWord(255, 255, 255) == "white" && Cs2Config.ColorWord(128, 128, 128) == "grey" && Cs2Config.ColorWord(255, 0, 255) == "pink"
            && Cs2Config.ColorWord(255, 120, 0) == "orange" && Cs2Config.ColorWord(0, 0, 255) == "blue", Cs2Config.ColorWord(0x50, 0xFF, 0));
        check("Críticos 6: com a Sync desligada o Gets sync aparece desligado e guarda o valor",
            xaml.Contains("<DataTrigger Binding=\"{Binding SyncPaused}\" Value=\"True\">") && xaml.Contains("<Trigger Property=\"Tag\" Value=\"paused\">")
            && Src("Views", "MainWindow.Configs.cs").Contains("SyncPaused = _s.ConfigMode == CfgMode.Off"), "");
        var fold = xaml.Substring(xaml.IndexOf("x:Key=\"CfgFold\""), 3000);
        check("Críticos 6: o chevron do Backups and snapshots à direita, como o How it works",
            fold.Contains("<Path x:Name=\"chev\" Grid.Column=\"1\" Data=\"M 0,0 L 3.5,3.5 L 7,0\"") && fold.Contains("Value=\"M 0,3.5 L 3.5,0 L 7,3.5\""), "");

        // 7. O botão do topo: a conta marcada como conta
        check("Críticos 7: a conta no botão do topo com a marca de pessoa, na cor de destaque",
            xaml.Contains("x:Name=\"SideQuickIcon\"") && Has(xaml, "x:Name=\"SideQuickNext\"", "Foreground=\"{StaticResource Accent}\""), "");

        // 8. Servidores sem nomes com a proteção desligada, e de onde vêm os IPs
        check("Críticos 8: com a proteção desligada a linha do título diz porque não há nomes",
            MainWindow.ServerCountText(22, 2, false, notAsked: true) == "22 servers on 2 IPs · names, players and ping show once protection is on"
            && MainWindow.ServerCountText(12, 2, true) == "12 servers on 2 IPs · all protected", MainWindow.ServerCountText(22, 2, false, notAsked: true));
        check("Críticos 8: Where to find IPs? ao lado de Import", xaml.Contains("x:Name=\"WhereIpsBtn\"") && Src("Views", "MainWindow.Servers.cs").Contains("void WhereIps_Click"), "");

        // 9. Os baixos do designer
        check("Críticos 9: o tempo com duas unidades no máximo e sem zeros",
            Fmt.Left(TimeSpan.FromDays(2)) == "2 d" && Fmt.Left(TimeSpan.FromHours(28)) == "1 d 4 h" && Fmt.Left(TimeSpan.FromMinutes(95)) == "1 h 35 min"
            && Fmt.Left(TimeSpan.FromHours(1)) == "1 h" && Fmt.Left(TimeSpan.FromMinutes(5)) == "5 min" && Fmt.Left(TimeSpan.FromSeconds(65)) == "1 min 05 s",
            $"{Fmt.Left(TimeSpan.FromDays(2))} | {Fmt.Left(TimeSpan.FromMinutes(95))} | {Fmt.Left(TimeSpan.FromSeconds(65))}");
        check("Críticos 9: a busca com 200 px em todas as páginas",
            Has(xaml, "x:Name=\"AccSearch\"", "Width=\"200\"") && Has(xaml, "x:Name=\"CfgAccSearch\"", "Width=\"200\"") && Has(xaml, "x:Name=\"LogSearch\"", "Width=\"200\"")
            && xaml.Substring(xaml.IndexOf("<TextBox x:Name=\"ServerSearch\"") - 400, 400).Contains("<ColumnDefinition Width=\"200\"/>"), "");
        check("Críticos 9: Picture e Privacy no mesmo estilo",
            xaml.Contains("<TextBlock Text=\"Picture\" FontSize=\"13\" Foreground=\"{StaticResource Muted}\" MinHeight=\"20\" Margin=\"0,0,0,6\"/>")
            && xaml.Contains("<TextBlock Text=\"Privacy\" FontSize=\"13\" Foreground=\"{StaticResource Muted}\" MinHeight=\"20\" Margin=\"0,0,0,6\"/>"), "");
        check("Críticos 9: a linha da Sync com separador", MainWindow.CfgSyncLastText("6 Nov 16:40", "Sync now: 2 copied.") == "last run 6 Nov 16:40 · Sync now: 2 copied.", "");
        check("Críticos 9: o último servidor da conta numa linha, o tempo primeiro",
            !Has(xaml, "Text=\"{Binding LastServerText}\"", "TextWrapping=\"Wrap\"") && Src("Models.cs").Contains("Loc.F(\"Last: {0} · {1}\", Fmt.Ago(Meta.LastServerAt)"), "");
        check("Críticos 9: a etiqueta da Perfis com 112 px", xaml.Substring(xaml.IndexOf("x:Key=\"TplLabel\""), 500).Contains("Value=\"112\""), "");

        // Só da build de desenvolvimento (src/Privado): o diagnóstico e o "dev" na versão não estão no código público
        var pub = Directory.GetFiles(Path.Combine(SecurityTests.Repo(), "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(Path.DirectorySeparatorChar + "Privado" + Path.DirectorySeparatorChar)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => { var s = File.ReadAllText(f); return s.Contains("\"Record diagnostics\"") || s.Contains("\"Mark kick\"") || s.Contains("\"Export diagnostics\""); }).ToList();
        check("Críticos: Record diagnostics, Mark kick e Export diagnostics só na build de desenvolvimento", pub.Count == 0, string.Join(", ", pub.Select(Path.GetFileName)));
        var csproj = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "ServerRouteTool.csproj"));
        check("Críticos: o +dev da versão só com a pasta Privado", csproj.Contains("<PropertyGroup Condition=\"Exists('$(MSBuildProjectDirectory)\\Privado')\">") && Count(csproj, "+dev") == 1, "");

        // A página Status já não existe: os textos dizem Connection
        var stale = pt.Keys.Concat(pt.Values).Where(v => v.Contains("in Status") || v.Contains("(Status ") || v.Contains("em Status")).ToList();
        check("Críticos: nenhum texto manda ligar a proteção em Status", stale.Count == 0, string.Join(" | ", stale.Take(2)));
    }

    static string Src(params string[] path) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(path).ToArray()));

    /// <summary>The element that has <paramref name="id"/> (up to its closing "&gt;") contains <paramref name="what"/>.</summary>
    static bool Has(string xaml, string id, string what)
    {
        var i = xaml.IndexOf(id, StringComparison.Ordinal);
        if (i < 0) return false;
        var start = xaml.LastIndexOf('<', i);
        var end = xaml.IndexOf('>', i);
        return xaml.Substring(start, end - start).Contains(what);
    }

    static int Count(string s, string what) { int n = 0, i = 0; s = s.Replace("\r\n", "\n"); while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; } return n; }

    static Dictionary<string, string> Lang(string code) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Lang", code + ".json")))!;
}
