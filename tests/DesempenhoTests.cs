using System.IO;

static class DesempenhoTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var source = Path.Combine(SecurityTests.Repo(), "src");
        string View(string name) => File.ReadAllText(Path.Combine(source, "Views", $"MainWindow.{name}.cs")).Replace("\r\n", "\n");

        var report = View("Report");
        check("Desempenho: recolha de placas, rotas e Firewall do relatório fora da UI",
            report.Contains("await Task.Run(async () =>\n        {\n            var adapters = Net.Scan();\n            var routes = await Net.ListRoutes();\n            return (adapters, routes, rules: Firewall.Remaining());\n        });"), "");

        var regions = View("Regions");
        check("Desempenho: leitura da cache e agrupamento das regiões fora da UI",
            regions.Contains("await Task.Run(async () =>\n            {\n                var config = await Regions.Load(game: state.Game);\n                return (config, config == null ? null : Regions.Group(config));\n            });")
            && regions.Contains("ShowRegionRows(state, infos);"), "");

        var phone = View("Phone");
        check("Desempenho: no primeiro arranque, a descoberta do Steam e CS2 corre fora da UI",
            phone.Contains("await Task.Run(() =>\n        {\n            var installed = Steam.InstallDir() != null;\n            return (installed, installed ? Steam.FindCs2(saved) : null);\n        });")
            && phone.Contains("StartProblems(steam, cs2);"), "");
        check("Desempenho: o assistente do telefone suspende o timer quando sai de vista",
            phone.Contains("SetupPane.IsVisibleChanged += (_, _) => _setupTimer.IsEnabled = SetupPane.IsVisible;")
            && phone.Contains("if (wasOpen) return;") && phone.Contains("await ReadSetupNow();")
            && phone.Contains("_setupTimer?.Stop();"), "");

        // 19 nov.: the Regions presets brought the app's only Expander, with no style in Theme.xaml: WPF loaded Windows' whole theme
        // for it (start 1393 to 1643 ms, median of 8 alternated runs of the demo) and drew a white circle. Every control type the
        // windows use has a style of the app's own.
        var theme = File.ReadAllText(Path.Combine(source, "Theme.xaml"));
        var xaml = string.Concat(Directory.GetFiles(source, "*.xaml", SearchOption.AllDirectories).Where(f => !f.EndsWith("Theme.xaml")).Select(File.ReadAllText));
        var unstyled = new[] { "Expander", "TabControl", "TreeView", "DataGrid", "ListView", "GroupBox", "Menu", "StatusBar", "ToolBar", "RichTextBox", "DatePicker", "Calendar" }
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(xaml, $"<{t}[\\s>]") && !theme.Contains($"TargetType=\"{t}\"")).ToList();
        check("Desempenho: nenhum controlo das janelas fica sem estilo da app (carregaria o tema inteiro do Windows)", unstyled.Count == 0, string.Join(", ", unstyled));
    }
}
