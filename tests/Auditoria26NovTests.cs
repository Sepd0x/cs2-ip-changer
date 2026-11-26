#nullable enable
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using SRT;
using SRT.Core;

// 26 nov.: a continuação da auditoria do Codex (áreas 7 a 11), achados confirmados no main, um teste por correção. Os relatórios estão
// nas notas da sessão (auditoria-views.md, auditoria-xaml.md, auditoria-site-ci.md, auditoria-worker.md); os do Worker e do site têm
// os seus testes em worker/stats/test/auditoria-w*.test.mjs e tools/site-auditoria-26nov.test.mjs.
public sealed class FonteX : System.ComponentModel.INotifyPropertyChanged
{
    string _t = ""; bool _a;
    public string Texto { get => _t; set { _t = value; PropertyChanged?.Invoke(this, new(nameof(Texto))); } }
    public bool Ativa { get => _a; set { _a = value; PropertyChanged?.Invoke(this, new(nameof(Ativa))); } }
    public string Botao => Ativa ? Loc.T("In use") : Loc.T("Log in");
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Mudou() => PropertyChanged?.Invoke(this, new(nameof(Botao)));
}

static class Auditoria26NovTests
{
    const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    /// <summary>Numa thread STA com a app em pt-BR (em inglês o Loc.Apply não faz nada), e o inglês de volta no fim.</summary>
    static List<(string, bool, string)> EmPtBr(Func<List<(string, bool, string)>> f)
    {
        var r = new List<(string, bool, string)>();
        var t = new Thread(() =>
        {
            var before = Loc.Lang;
            try { Loc.Init("pt-BR"); r = f(); }
            catch (Exception e) { r.Add(("Auditoria XAML: o ensaio correu", false, e.Message)); }
            finally { Loc.Init(before); }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        return r;
    }

    public static void Run(Action<string, bool, string> check)
    {
        foreach (var (n, ok, d) in EmPtBr(() =>
        {
            var l = new List<(string, bool, string)>();
            // X1: Header e ToolTip ligados por Binding seguem o valor, fora do inglês
            var row = new FonteX { Texto = Loc.T("Sign in") };
            var mi = (MenuItem)XamlReader.Parse($"<MenuItem {Ns} Header='{{Binding Texto}}'/>");
            mi.DataContext = row; Pump(); Loc.Apply(mi);
            row.Texto = Loc.T("Sign out"); Pump();
            l.Add(("Auditoria X1: o menu da conta em Profiles diz Sair depois de entrar", mi.Header as string == row.Texto, $"{mi.Header}"));
            var srv = new FonteX { Texto = "1.2.3.4:27015" };
            var b = (Border)XamlReader.Parse($"<Border {Ns} ToolTip='{{Binding Texto}}'/>");
            b.DataContext = srv; Pump(); Loc.Apply(b);
            srv.Texto = "5.6.7.8:27015"; Pump();
            l.Add(("Auditoria X1: a dica de uma linha de Servers segue o servidor", b.ToolTip as string == srv.Texto, $"{b.ToolTip}"));
            // um texto escrito no XAML continua a ser traduzido
            var plain = (Border)XamlReader.Parse($"<Border {Ns} ToolTip='Log in'/>");
            Loc.Apply(plain);
            l.Add(("Auditoria X1: um ToolTip escrito no XAML continua traduzido", plain.ToolTip as string == Loc.T("Log in") && Loc.T("Log in") != "Log in", $"{plain.ToolTip}"));
            // X2: o botão do cartão vem da conta (Content por Binding num Setter), não de um Setter de texto tapado pelo Loc
            var acc = new FonteX();
            var btn = (Button)XamlReader.Parse($"<Button {Ns}><Button.Style><Style TargetType='Button'><Setter Property='Content' Value='{{Binding Botao}}'/>"
                + "<Style.Triggers><DataTrigger Binding='{Binding Ativa}' Value='True'><Setter Property='IsEnabled' Value='False'/></DataTrigger></Style.Triggers></Style></Button.Style></Button>");
            btn.DataContext = acc; Pump(); Loc.Apply(btn);
            acc.Ativa = true; acc.Mudou(); Pump();
            l.Add(("Auditoria X2: a conta em uso diz Em uso depois de uma troca feita pela app", btn.Content as string == Loc.T("In use") && !btn.IsEnabled, $"{btn.Content} {btn.IsEnabled}"));
            // X3: a dica da caixa vem do Tag por TemplateBinding
            var tb = (TextBox)XamlReader.Parse($"<TextBox {Ns}><TextBox.Template><ControlTemplate TargetType='TextBox'><Grid>"
                + "<TextBlock x:Name='ph' Text='{TemplateBinding Tag}'/><ScrollViewer x:Name='PART_ContentHost'/></Grid></ControlTemplate></TextBox.Template></TextBox>");
            tb.Tag = Loc.T("E.g. 21h, 3d, 90m, 2d 12h"); tb.ApplyTemplate();
            var ph = (TextBlock)tb.Template.FindName("ph", tb); Loc.Apply(ph);
            tb.Tag = Loc.T("steamcommunity.com/id/… or SteamID64");
            l.Add(("Auditoria X3: a dica da caixa é a do diálogo aberto", ph.Text == (string)tb.Tag, ph.Text));
            return l;
        })) check(n, ok, d);

        var xaml = Src("MainWindow.xaml");
        check("Auditoria X2: o AccLogin tira o texto da conta, e a conta diz In use ou Log in",
            xaml.Contains("<Setter Property=\"Content\" Value=\"{Binding LoginText}\"/>") && !xaml.Contains("<Setter Property=\"Content\" Value=\"In use\"/>")
            && Src("Models.cs").Contains("public string LoginText => Active ? Loc.T(\"In use\") : Loc.T(\"Log in\");")
            && Src("Models.cs").Contains("Raise(nameof(CardBorder)); Raise(nameof(LoginText));"), "");
        check("Auditoria X1: a dica do Recebe vem da linha (o texto da pausa já não está num trigger)",
            xaml.Contains("<Setter Property=\"ToolTip\" Value=\"{Binding SyncTip}\"/>") && Src("Views", "MainWindow.Configs.cs").Contains("public string SyncTip =>"), "");

        // X4: as frases de segurança nomeiam os botões pelo texto que eles têm
        foreach (var c in new[] { "pt-BR", "es", "ru" })
        {
            var t = Loc.Load(c);
            var allow = t["Allow all (block off)"];
            var wrong = t.Where(kv => kv.Key.Contains("Allow all (block off)") && kv.Key != "Allow all (block off)" && !kv.Value.Contains(allow)).Select(kv => kv.Key[..40]).ToList();
            check($"Auditoria X4: {c} chama Allow all pelo nome do botão", wrong.Count == 0, string.Join(" | ", wrong));
        }
        var pt = Loc.Load("pt-BR");
        check("Auditoria X4: pt-BR chama Join de Conectar nas frases",
            pt["Your servers: players, ping and Join"].EndsWith("Conectar") && pt["Servers, Join and Log in work on your home connection. Servers see your home IP."].Contains("Conectar"), "");

        // X5, X6
        check("Auditoria X5: os continentes do mostrador em maiúsculas pela língua da app", Src("Views", "RegionDial.cs").Contains("ToUpper(CultureInfo.GetCultureInfo(Loc.Lang))")
            && !Src("Views", "RegionDial.cs").Contains("ToUpper(CultureInfo.CurrentCulture)"), "");
        check("Auditoria X6: Change IP, o Close do fim e o Followed passam pelo Loc",
            Src("Views", "MainWindow.Phone.cs").Contains("RunCommand(Loc.T(\"Change IP\"), Loc.T(\"starting…\")") && Src("Views", "MainWindow.Tray.cs").Contains("RunCommand(Loc.T(\"Change IP\")")
            && Src("Views", "NoticeWindow.xaml.cs").Contains("OkBtn.Content = Loc.T(\"Close\")") && Src("Views", "MainWindow.Players.cs").Contains("f.Label = Loc.T(\"Followed\")"), "");

        // X8: o ponto amarelo só entre as regiões com ping
        RegionItem R(string id, int ping) => new() { Info = new RegionInfo(id, id, null, "Spain", "Europe", new[] { id }, Array.Empty<string>()), PingMs = ping };
        check("Auditoria X8: o ponto da mais lenta só entre as medidas",
            RegionDial.Slowest(new[] { R("a", 30), R("b", -1), R("c", -2) })?.Id == "a" && RegionDial.Slowest(new[] { R("b", -1) }) == null
            && RegionDial.Slowest(new[] { R("a", 30), R("d", 80) })?.Id == "d", "");

        // V1: o Join volta a ver a proteção mesmo antes de ligar
        check("Auditoria V1: o Join não liga se a proteção mudou enquanto esperava",
            MainWindow.ProtectionChanged(onHome: false, protOn: false, distrust: false) && MainWindow.ProtectionChanged(false, true, true)
            && !MainWindow.ProtectionChanged(false, true, false) && !MainWindow.ProtectionChanged(true, false, false)
            && Src("Views", "MainWindow.Servers.cs").Contains("if (ProtectionChanged(OnHome, _protOn, _distrust))"), "");
        // V2: a mudança do Wi-Fi do telemóvel compara com a última tratada
        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Auditoria V2: o Wi-Fi do telemóvel a ligar é tratado mesmo quando o Phone setup o leu primeiro",
            phone.Contains("if (PhoneWifiOn != _wifiHandled)") && phone.Contains("_wifiHandled = PhoneWifiOn;") && !phone.Contains("wifiBefore"), "");
        // V3: o relatório tapa as redes Wi-Fi das linhas da própria app
        var line = "[12:00:00] WARN " + MainWindow.PhoneWifiOnText("CasaExemplo-5G", protOn: true, noPhone: false)
            + "\n[12:01:00] INFO Phone connection: hotspot \"iPhone de Ana\" on Wi-Fi.\n[12:02:00] INFO The PC's Wi-Fi is connected to \"Vizinho\", not to the hotspot \"iPhone de Ana\".";
        var hidden = Report.HideSsids(line);
        check("Auditoria V3: o relatório tapa o SSID de uma linha antiga do registo",
            !hidden.Contains("CasaExemplo") && !hidden.Contains("iPhone de Ana") && !hidden.Contains("Vizinho") && hidden.Contains("(hidden)") && Report.HideSsids("no ssid here") == "no ssid here", hidden);
        // V5: o Whole PC tirado por ligação fraca volta depois de um recomeço
        check("Auditoria V5: o Whole PC volta com a primeira medida boa depois de um Change IP ou de reabrir",
            MainWindow.WholePcBack(QualityLevel.Unknown, QualityLevel.Good, true) && MainWindow.WholePcBack(QualityLevel.Unknown, QualityLevel.Fair, true)
            && !MainWindow.WholePcBack(QualityLevel.Unknown, QualityLevel.Bad, true) && !MainWindow.WholePcBack(QualityLevel.Unknown, QualityLevel.Good, false)
            && !MainWindow.WholePcBack(QualityLevel.Good, QualityLevel.Good, true), "");
        // 26 nov. (o dono): com o Phone setup ou as portas abertas a barra de cima ficava baça e os botões da janela por baixo do véu
        var xamlTop = Src("MainWindow.xaml");
        foreach (var pane in new[] { "SetupPane", "StartPane", "Dialog" })
            check($"Barra de cima: o véu de {pane} começa abaixo dela (40 px), como o dos diálogos",
                xamlTop.Contains($"<Grid x:Name=\"{pane}\" Grid.Row=\"1\" Margin=\"0,40,0,0\""), pane);

        // 26 nov. à noite: o que tinha ficado de fora (mês, resultado da Sync, X7, V4, S3)
        foreach (var (n, ok, d) in EmPtBr(() =>
        {
            var l = new List<(string, bool, string)>();
            var nov = new DateTime(2026, 11, 5, 12, 0, 0);
            var data = MainWindow.DataText(85_400_000, 1_240_000_000, nov);
            l.Add(("Barra de estado: o mês na língua da app (era \"em November\")", MainWindow.MonthName(nov) == "novembro" && data.Contains("novembro") && !data.Contains("November"), data));
            var sync = MainWindow.CfgLastResultText(nov, CfgTrigger.Manual, new[] { 2, 3, 0, 0 });
            l.Add(("Configs: o resultado da Sync faz-se na língua de agora, a partir dos números guardados",
                sync.Contains(Loc.F("{0} copied", 2)) && sync.Contains(Loc.F("{0} already the same", 3)) && Loc.F("{0} copied", 2) != "2 copied", sync));
            var pt = Countries.InLanguage("pt-BR");
            string Name(string code) => pt.First(c => c.Code == code).Name;
            l.Add(("Auditoria X7: Profiles > País na língua da app, ordenado por ela",
                Name("BR") == "Brasil" && Name("DE") == "Alemanha" && pt.ToList().FindIndex(c => c.Code == "DE") < pt.ToList().FindIndex(c => c.Code == "BR")
                && pt.Count == Countries.All.Count && Countries.InLanguage("en") == Countries.All, $"{Name("BR")} {Name("DE")}"));
            return l;
        })) check(n, ok, d);
        var cfgSrc = Src("Views", "MainWindow.Configs.cs");
        check("Configs: a linha da Sync lê os dados guardados (LastAt, LastWhy, LastCounts), e o texto antigo só como reserva",
            cfgSrc.Contains("CfgLastResult(Cfg.State)") && !cfgSrc.Contains("Cfg.State.LastResult));") && Src("Core", "Steam", "Cs2Configs.cs").Contains("public int[] LastCounts"), "");
        var prof = Src("Views", "MainWindow.Profiles.cs");
        var a = new ProfileTemplate(); var b = new ProfileTemplate();
        check("Auditoria V4: depois do Copy look, os campos só mostram o modelo se ele ainda for o escolhido",
            MainWindow.CopyStillShown(a, a) && !MainWindow.CopyStillShown(b, a) && !MainWindow.CopyStillShown(null, a)
            && !prof.Contains("ShowTemplate(t); SaveTemplates();") && prof.Split("ShowAfterCopy(t); SaveTemplates();").Length == 4, "");
        // 26 nov. à noite (o dono): a barra lateral nova ficou sem os ícones da antiga
        var theme = Src("Theme.xaml");
        var nav = theme.Substring(theme.IndexOf("<Style x:Key=\"Nav\" TargetType=\"RadioButton\">"), 2600);
        var navRows = System.Text.RegularExpressions.Regex.Matches(xaml, "Style=\"\\{StaticResource Nav\\}\" Tag=\"&#x[0-9A-F]{4};\"").Count;
        check("Barra lateral: cada página com o seu ícone antes do nome, a azul na página aberta",
            nav.Contains("<TextBlock x:Name=\"icon\" Text=\"{TemplateBinding Tag}\" FontFamily=\"{StaticResource Icons}\"")
            && nav.Contains("<Setter TargetName=\"icon\" Property=\"Foreground\" Value=\"{StaticResource Accent}\"/>") && navRows == 8, navRows.ToString());
        var key = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tools", "release-key.mjs"));
        check("Auditoria S3: a frase-passe não se pede numa janela que a mostra (Git Bash sem TTY)",
            key.Contains("if (hidden && !canHide(process.stdin.isTTY))") && key.Contains("const canHide = (isTTY) => !!isTTY;"), "");
    }
}
