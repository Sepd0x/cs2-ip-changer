#nullable enable
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 27 nov., à noite: os pequenos defeitos da 1.0.0 vistos nas capturas a 1366x768 a 125 % e a 1080p a 200 % nas cinco línguas, o
// pedido do dono sobre a barra lateral (larga à esquerda e à direita dos ícones) e os metadados do exe (VirusTotal). Um teste por defeito.
static class Correcoes27NoiteTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    public static void Run(Action<string, bool, string> check)
    {
        void Case(string name, Func<bool> run, Func<string>? why = null)
        {
            try { check(name, run(), why?.Invoke() ?? ""); }
            catch (Exception e) { check(name, false, e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message); }
        }

        var xaml = Src("MainWindow.xaml");
        var theme = Src("Theme.xaml");

        // A linha de estado: um StackPanel horizontal dava espaço sem fim ao texto, e a 960 px "Dados móveis: 312 MB desde que o app al" era cortado a meio
        Case("27 noite: a linha de estado acaba em reticências, não corta a palavra", () =>
        {
            var i = xaml.IndexOf("x:Name=\"StatusLeft\"", StringComparison.Ordinal);
            var block = i < 0 ? "" : xaml[i..xaml.IndexOf("</DockPanel>", i, StringComparison.Ordinal)];
            return xaml.Contains("<DockPanel x:Name=\"StatusLeft\" LastChildFill=\"True\"")
                && Regex.IsMatch(block, "ElementName=DataUsage}\"[^>]*TextTrimming=\"CharacterEllipsis\"")
                && !xaml.Contains("<StackPanel Orientation=\"Horizontal\" ClipToBounds=\"True\">");
        });

        // O aviso de Regions: um "!" de texto a 10 px ficava baixo, com o ponto colado ao traço
        Case("27 noite: o aviso e a dica de Regions são desenhados, sem \"!\" nem \"i\" de texto", () =>
            xaml.Contains("x:Name=\"RegionsWarningMark\"") && xaml.Contains("x:Name=\"RegionsTipMark\"")
            && !xaml.Contains("<TextBlock Text=\"!\"") && !Regex.IsMatch(xaml, "<TextBlock Text=\"i\" FontSize=\"10\""));

        // A data em todas as línguas: Fmt.When usava sempre a cultura invariante ("последний запуск 6 Nov 16:40")
        Case("27 noite: as datas curtas saem na língua da app (Configs, contas, bans)", () =>
        {
            var before = Loc.Lang;
            var at = new DateTime(2026, 11, 6, 16, 40, 0);
            try
            {
                Loc.Init("en"); var en = Fmt.When(at);
                Loc.Init("ru"); var ru = Fmt.When(at);
                Loc.Init("pt-BR"); var pt = Fmt.When(at);
                return en == "6 Nov 16:40" && !ru.Contains("Nov") && ru.StartsWith("6 ") && ru.EndsWith(" 16:40") && pt.Contains("nov") && !pt.Contains("Nov");
            }
            finally { Loc.Init(before); }
        });
        Case("27 noite: a hora da última sincronização separa-se também com o mês noutra língua", () =>
        {
            var re = (Regex)typeof(MainWindow).GetField("CfgResultTime", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return re.IsMatch("6 нояб. 16:40 · Sync now: 2 copied.") && re.IsMatch("6 Nov 16:40 · Sync now: 2 copied.") && re.IsMatch("6 Kas 16:40 · x");
        });
        Case("27 noite: o resultado da sincronização concorda com o número em pt-BR e es (era \"2 copiada, 3 já está igual\")", () =>
        {
            var before = Loc.Lang;
            var at = new DateTime(2026, 11, 6, 16, 40, 0);
            try
            {
                Loc.Init("pt-BR"); var pt = MainWindow.CfgLastResultText(at, "manual", new[] { 2, 3, 0, 2 });
                var pt1 = MainWindow.CfgLastResultText(at, "manual", new[] { 1, 1, 0, 0 });
                Loc.Init("es"); var es = MainWindow.CfgLastResultText(at, "manual", new[] { 2, 3, 0, 0 });
                Loc.Init("ru"); var ru = MainWindow.CfgLastResultText(at, "manual", new[] { 2, 0, 0, 0 });
                Loc.Init("en"); var en = MainWindow.CfgLastResultText(at, "manual", new[] { 2, 3, 0, 0 });
                return pt.Contains("2 copiadas") && pt.Contains("3 já estão iguais") && pt.Contains("2 não mudaram") && pt1.Contains("1 copiada,") && pt1.Contains("1 já está igual")
                    && es.Contains("2 copiadas") && es.Contains("3 ya son iguales") && ru.Contains("2: скопировано") && en.Contains("2 copied, 3 already the same");
            }
            finally { Loc.Init(before); }
        });
        Case("27 noite: nenhuma data mostrada ao utilizador sai na cultura do Windows (\"g\")", () =>
            !Src("Models.cs").Contains("ToString(\"g\")") && !Src("Views", "MainWindow.Kick.cs").Contains("ToString(\"g\")"));

        // Configs: "Sync: ... · last run ..." também cortava sem reticências
        Case("27 noite: a linha Sync de Configs acaba em reticências", () =>
            Regex.IsMatch(xaml, "x:Name=\"CfgSyncLast\"[^>]*TextTrimming=\"CharacterEllipsis\"")
            && Regex.IsMatch(xaml, "<ToggleButton x:Name=\"CfgSyncBtn\"[^>]*>\\s*<DockPanel LastChildFill=\"True\""));

        // Settings: os nomes da lista da esquerda ("Горячие клавиши и звук") eram cortados a meio de uma letra
        Case("27 noite: os nomes da lista de Settings acabam em reticências", () =>
        {
            var i = xaml.IndexOf("x:Key=\"SetNavItem\"", StringComparison.Ordinal);
            return i > 0 && xaml.IndexOf("TextTrimming\" Value=\"CharacterEllipsis\"", i, StringComparison.Ordinal) is var j && j > i && j - i < 2500;
        });

        // A barra lateral (o dono: larga à esquerda e à direita dos ícones): 104 px, a moldura a 8 px das bordas
        Case("27 noite: a barra lateral tem 104 px e a moldura 8 px de cada lado", () =>
            xaml.Contains("<ColumnDefinition x:Name=\"SideColumn\" Width=\"104\"/>") && theme.Contains("<Thickness x:Key=\"NavTile\">8,4</Thickness>"));
        Case("27 noite: o nome mais comprido das páginas cabe na moldura de 88 px (5 línguas, 12 px SemiBold)", () =>
        {
            var names = new[] { "Connection", "Servers", "Regions", "Accounts", "Configs", "Profiles", "Log", "Settings" };
            var face = new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold, System.Windows.FontStretches.Normal);
            double widest = 0;
            foreach (var lang in Loc.Codes)
            {
                var t = lang == "en" ? new Dictionary<string, string>() : Loc.Load(lang);
                foreach (var n in names)
                {
                    var s = t.TryGetValue(n, out var tr) ? tr : n;
                    var ft = new System.Windows.Media.FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, face, 12, System.Windows.Media.Brushes.White, 1.0);
                    widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
                }
            }
            return widest <= 104 - 2 * 10;
        });
        Case("27 noite: numa janela baixa o ícone e o nome cabem na moldura da página", () =>
        {
            var (row, _) = MainWindow.NavFit(SideHeight(516));
            var (tile, gap) = MainWindow.NavTileFit(row);
            var (tall, tallGap) = MainWindow.NavTileFit(66);
            return row - 2 * tile >= 22 + gap + 16 + 2 && tall == 4 && tallGap == 6;
        }, () => $"linha {MainWindow.NavFit(SideHeight(516)).Row}");

        // Acessibilidade: Minimizar, Fechar, Copiar IP, Apagar... eram só um ícone, e o Narrador lia o carácter privado do ícone
        Case("27 noite: um botão só com ícone tem o nome da sua dica para o Narrador", () =>
        {
            bool ok = false;
            var t = new Thread(() =>
            {
                var icon = ((char)0xE711).ToString();
                var close = new System.Windows.Controls.Button { Content = icon, ToolTip = "Close" };
                var named = new System.Windows.Controls.Button { Content = icon, ToolTip = "Close" };
                System.Windows.Automation.AutomationProperties.SetName(named, "Fechar");
                var word = new System.Windows.Controls.Button { Content = "Add", ToolTip = "Opens the Steam login" };
                IconNames.Name(close); IconNames.Name(named); IconNames.Name(word);
                var first = System.Windows.Automation.AutomationProperties.GetName(close);
                close.ToolTip = "Show as cards";   // the code changes a tip later (Accounts' view switch)
                ok = first == "Close" && System.Windows.Automation.AutomationProperties.GetName(close) == "Show as cards"
                    && System.Windows.Automation.AutomationProperties.GetName(named) == "Fechar"
                    && System.Windows.Automation.AutomationProperties.GetName(word) == "" && !IconNames.IsIcon("Add") && IconNames.IsIcon(icon);
            });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
            return ok;
        });
        Case("27 noite: o gancho dos nomes dos ícones arranca com o da tradução", () => Src("Program.cs").Contains("Loc.Hook();\n        IconNames.Hook();") || Src("Program.cs").Contains("Loc.Hook();\r\n        IconNames.Hook();"));

        // Achados da terceira passagem do Codex (ServerRouteToolCodex/CODEX-RELATORIO.md, C1 a C8)
        Case("27 noite C1: Enter no X ou no Cancel de uma pergunta não confirma (apagava a cópia de Configs)", () =>
        {
            object cancel = new object(), close = new object(), ok = new object(), input = new object();
            return !MainWindow.EnterConfirms(close, cancel, close) && !MainWindow.EnterConfirms(cancel, cancel, close)
                && MainWindow.EnterConfirms(ok, cancel, close) && MainWindow.EnterConfirms(input, cancel, close) && MainWindow.EnterConfirms(null!, cancel, close);
        });
        Case("27 noite C2: \"NaN\" ou \"Infinity\" no aviso de dados móveis dá 0, não um número que o settings.json não guarda", () =>
            MainWindow.DataWarnGbOf("NaN") == 0 && MainWindow.DataWarnGbOf("Infinity") == 0 && MainWindow.DataWarnGbOf("-Infinity") == 0
            && MainWindow.DataWarnGbOf("1,5") == 1.5 && MainWindow.DataWarnGbOf("20000") == 10000 && MainWindow.DataWarnGbOf("-3") == 0 && MainWindow.DataWarnGbOf("abc") == 0);
        Case("27 noite C4: a lista completa das estatísticas cabe no ecrã com os botões à vista", () =>
            NoticeWindow.MoreRoom(576, 394) == 146 && NoticeWindow.MoreRoom(1040, 394) == 300 && NoticeWindow.MoreRoom(500, 450) == 80);
        Case("27 noite C5: com Update à vista a 1080p a 200 %, Profiles não fica por baixo de Log", () =>
        {
            foreach (var side in new[] { 514.0, 498.0 })
            {
                var (row, gap) = MainWindow.NavFit(side, 24 + 8);
                var (tile, nameGap) = MainWindow.NavTileFit(row);
                if (74 + gap + 8 * row + 25 + 32 > side || row - 2 * tile < 22 + nameGap + 16) return false;
            }
            return true;
        }, () => MainWindow.NavFit(514, 32).ToString());
        Case("27 noite C7: no cooldown, \"-2h\" e \"1h 30\" não são aceites como 2 h e 1 h", () =>
            Fmt.ParseDuration("-2h") == null && Fmt.ParseDuration("1h 30") == null && Fmt.ParseDuration("1h30m") == TimeSpan.FromMinutes(90)
            && Fmt.ParseDuration("3d 4h") == TimeSpan.FromHours(76) && Fmt.ParseDuration("36") == TimeSpan.FromHours(36) && Fmt.ParseDuration("2 h") == TimeSpan.FromHours(2));
        Case("27 noite C8: o contador filtrado do Log segue o Clear e as linhas novas", () =>
            MainWindow.LogCountText(true, 0, 0) == "0 of 0 lines" && MainWindow.LogCountText(false, 3, 40) == ""
            && Src("Views", "MainWindow.Window.cs").Contains("LogCount.Text = LogCountText(filtered,")
            && !Src("Views", "MainWindow.Window.cs").Contains("LogCount.Text = _logView.Filter == null"));

        Case("27 noite C19: Configs volta a procurar o userdata quando o Steam aparece depois do arranque", () =>
            MainWindow.CfgStale(null, false, () => "C:\\Steam") && !MainWindow.CfgStale(null, false, () => null)
            && !MainWindow.CfgStale("C:\\Steam\\userdata", false, () => "C:\\Steam") && !MainWindow.CfgStale(null, true, () => "C:\\Steam"));
        Case("27 noite C20: a letra do avatar é o primeiro carácter inteiro (emoji incluído)", () =>
            Fmt.Initial("🎮Teste") == "🎮" && Fmt.Initial("nightfall") == "N" && Fmt.Initial("") == "?" && Fmt.Initial(null!) == "?" && Fmt.Initial("🎮Teste").Length == 2);
        Case("27 noite C24: um timestamp do loginusers.vdf em milissegundos fica desconhecido em vez de lançar", () =>
        {
            var ok = Steam.UnixSeconds("1700000000000") == 0 && Steam.UnixSeconds("1700000000") == 1700000000 && Steam.UnixSeconds("-5") == 0 && Steam.UnixSeconds("x") == 0;
            DateTimeOffset.FromUnixTimeSeconds(Steam.UnixSeconds("99999999999999"));   // must not throw
            return ok;
        });

        Case("27 noite C12: o botão maximizar diz Restaurar quando a janela está maximizada, nas cinco línguas", () =>
        {
            var before = Loc.Lang;
            try
            {
                Loc.Init("en"); var en = (MainWindow.MaxTip(false), MainWindow.MaxTip(true));
                Loc.Init("es"); var es = MainWindow.MaxTip(true);
                var all = new[] { "pt-BR", "es", "ru", "tr" }.All(l => Loc.Load(l).ContainsKey("Restore down"));
                return en == ("Maximize", "Restore down") && es == "Restaurar tamaño" && all && Src("MainWindow.xaml.cs").Contains("MaxBtn.ToolTip = MaxTip(");
            }
            finally { Loc.Init(before); }
        });
        Case("27 noite C13: o nome acessível escrito no XAML também é traduzido", () =>
        {
            var before = Loc.Lang;
            string got = "";
            var t = new Thread(() =>
            {
                try
                {
                    Loc.Init("es");
                    var box = new System.Windows.Controls.TextBox();
                    System.Windows.Automation.AutomationProperties.SetName(box, "Find a setting");
                    Loc.Apply(box);
                    got = System.Windows.Automation.AutomationProperties.GetName(box);
                }
                finally { Loc.Init(before); }
            });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
            return got != "Find a setting" && got == Loc.Load("es")["Find a setting"];
        });
        Case("27 noite C14: os dados móveis usam a vírgula da língua (\"1,24 GB\" em pt-BR) e o ponto em inglês", () =>
        {
            var before = Loc.Lang;
            try
            {
                Loc.Init("pt-BR"); var pt = DataMeter.Text(1_240_000_000);
                Loc.Init("en"); var en = DataMeter.Text(1_240_000_000);
                return pt == "1,24 GB" && en == "1.24 GB";
            }
            finally { Loc.Init(before); }
        });

        Case("27 noite C21: sem contas, as launch options dizem que não há contas (diziam que todas já as tinham)", () =>
            MainWindow.LaunchOptsToast(0, 0, 0).StartsWith("No Steam account") && MainWindow.LaunchOptsToast(3, 0, 0) == "Every account already has them."
            && MainWindow.LaunchOptsToast(3, 2, 0).StartsWith("Added on 2 accounts") && new[] { "pt-BR", "es", "ru", "tr" }.All(l => Loc.Load(l).ContainsKey("No Steam account on this PC yet. Log in to Steam once, then try again.")));
        Case("27 noite C23: com a única conta escondida e Show hidden, o aviso de lista vazia sai", () =>
            !MainWindow.AccEmptyShows(0, true, 1) && MainWindow.AccEmptyShows(0, false, 1) && MainWindow.AccEmptyShows(0, true, 0) && !MainWindow.AccEmptyShows(2, false, 0));

        Case("27 noite C18: no Phone setup o Close fica em baixo e os passos fazem scroll", () =>
        {
            var i = xaml.IndexOf("x:Name=\"SetupPane\"", StringComparison.Ordinal);
            var block = xaml[i..xaml.IndexOf("x:Name=\"StartPane\"", i, StringComparison.Ordinal)];
            return block.Contains("<DockPanel LastChildFill=\"True\">") && block.Contains("<Grid DockPanel.Dock=\"Bottom\" Margin=\"0,16,0,0\">")
                && block.IndexOf("SetupCloseBtn", StringComparison.Ordinal) < block.IndexOf("x:Name=\"SetupScroll\"", StringComparison.Ordinal)
                && block.IndexOf("x:Name=\"SetupScroll\"", StringComparison.Ordinal) < block.IndexOf("x:Name=\"SetupGuide\"", StringComparison.Ordinal);
        });
        Case("27 noite C25: em Regions a linha de origem deixa o Refresh dentro da janela", () =>
            MainWindow.RegionsSourceRoom(1080, 60) == 560 && MainWindow.RegionsSourceRoom(780, 60) == 462 && MainWindow.RegionsSourceRoom(300, 60) == 0
            && xaml.Contains("x:Name=\"RegionsHead\"") && xaml.Contains("SizeChanged=\"RegionsHead_SizeChanged\""));
        Case("27 noite C27: um ficheiro de servidores que desaparece antes de ser lido dá null, não uma exceção", () =>
            MainWindow.ReadListFile(Path.Combine(Path.GetTempPath(), "pickroute-nao-existe-" + Guid.NewGuid().ToString("N") + ".txt")).GetAwaiter().GetResult() == null);
        Case("27 noite C28: a volta de Whole PC while CS2 is open não diz que o PC inteiro já está no telemóvel", () =>
            MainWindow.WholePcBackText("all") == "Mobile connection is fine again: Whole PC is back on."
            && MainWindow.WholePcBackText("game").Contains("Whole PC while CS2 is open") && MainWindow.WholePcBackText("game").Contains("when CS2 opens")
            && new[] { "pt-BR", "es", "ru", "tr" }.All(l => Loc.Load(l).ContainsKey("Mobile connection is fine again: Whole PC while CS2 is open is back on. The whole PC uses the phone when CS2 opens.")
                && Loc.Load(l).ContainsKey("Couldn't read that file. Choose it again with Import.")));

        Case("27 noite C9: a dica da caixa de Servers começa pelo atalho e cabe a 1080p a 200 % (o fim é o exemplo)", () =>
            xaml.Contains("Tag=\"Ctrl+Enter adds · e.g. 1.2.3.4:27015\"")
            && Loc.Codes.Where(l => l != "en").All(l => Loc.Load(l).TryGetValue("Ctrl+Enter adds · e.g. 1.2.3.4:27015", out var v) && v.StartsWith("Ctrl+Enter") && v.Length <= 42));
        Case("27 noite C11: a explicação das cópias de Configs quebra a linha em vez de esconder o Original", () =>
            Regex.IsMatch(xaml, "x:Name=\"CfgBackupsNote\"[^>]*TextWrapping=\"Wrap\"") && !Regex.IsMatch(xaml, "x:Name=\"CfgBackupsNote\"[^>]*TextTrimming"));
        Case("27 noite C16: a pesquisa das Settings em pt-BR cabe na caixa", () => Loc.Load("pt-BR")["Find a setting"].Length <= 20);

        // O exe (VirusTotal e Propriedades > Detalhes): dizia "CS2IPChanger" como descrição e não tinha empresa nem direitos de autor
        Case("27 noite: o exe diz Pickroute, Sepd0x e os direitos de autor", () =>
        {
            var asm = typeof(MainWindow).Assembly;
            return asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == "Pickroute"
                && asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company == "Sepd0x"
                && asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright?.Contains("Sepd0x") == true
                && asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "Pickroute";
        });
        Case("27 noite: o manifesto do exe tem o nome e a versão certos (era CS2IPChanger.app 9.0.0.0)", () =>
        {
            var m = Src("app.manifest");
            return m.Contains("name=\"Sepd0x.Pickroute\"") && !m.Contains("9.0.0.0") && m.Contains("requireAdministrator");
        });
    }

    // the sidebar runs the window's whole height inside its 1 px frame
    static double SideHeight(double window) => window - 2;
}
