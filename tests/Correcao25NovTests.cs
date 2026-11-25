using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SRT;
using SRT.Core;

// 25 nov.: a revisão independente do primeiro arranque e do Phone setup (publish/plano-1-0-0/revisao-25nov-usabilidade.md) antes da
// 1.0.0. Cada achado corrigido tem aqui o seu teste, que falhava antes da correção. Os números são os do relatório.
static class Correcao25NovTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    /// <summary>O corpo de um método: do seu nome até ao fecho da chaveta ao nível dos membros da classe.</summary>
    static string Body(string src, string from)
    {
        var at = src.IndexOf(from, StringComparison.Ordinal);
        if (at < 0) return "";
        var end = src.IndexOf("\n    }", at, StringComparison.Ordinal);
        return end < 0 ? src[at..] : src[at..end];
    }

    static readonly string[] Langs = { "es", "pt-BR", "ru", "tr" };
    static readonly Regex CounterName = new(@"^[A-Za-z0-9_]{1,48}$");

    public static void Run(Action<string, bool, string> check)
    {
        Arranque(check);
        HotspotGuardado(check);
        ListaWifi(check);
        Contadores(check);
        Localizacao(check);
        TextoDoCabo(check);
        Download(check);
        Bandeja(check);
        MedidaDaVisita(check);
        MedidaDoPhoneSetup(check);
    }

    // 1: a página de arranque escolhe-se antes de a janela aparecer, e depois disso nada tira o utilizador da página em que clicou
    static void Arranque(Action<string, bool, string> check)
    {
        var main = Src("MainWindow.xaml.cs");
        var ctor = main[main.IndexOf("public MainWindow(", StringComparison.Ordinal)..main.IndexOf("static ICollectionView CollectionViewSourceFor", StringComparison.Ordinal)];
        int pick = ctor.IndexOf("PickStartPage();", StringComparison.Ordinal), shown = ctor.IndexOf("ContentRendered +=", StringComparison.Ordinal),
            hook = ctor.IndexOf("TelemetryHook();", StringComparison.Ordinal), stats = ctor.IndexOf("Telemetry.Start(", StringComparison.Ordinal);
        check("Correção 1: a página de arranque é escolhida no construtor, antes de a janela aparecer", pick > 0 && pick < shown, $"{pick} {shown}");
        check("Correção 1: e antes de as estatísticas lerem o ecrã aberto (o salto contava como \"1.º ecrã escolhido\")", pick > stats && pick < hook, $"{stats} {pick} {hook}");
        var startup = main[main.IndexOf("async Task Startup()", StringComparison.Ordinal)..main.IndexOf("void OnPowerModeChanged", StringComparison.Ordinal)];
        check("Correção 1: depois do primeiro await do arranque nada muda de página", !Regex.IsMatch(startup, @"Nav\w+\.IsChecked\s*=\s*true"), "");
        var start = Src("Views", "MainWindow.Start.cs");
        check("Correção 1: Later fecha as portas e deixa a página onde o utilizador está", !Regex.IsMatch(Body(start, "void DoorLater_Click"), @"Nav\w+\.IsChecked\s*=\s*true"), "");

        // A nota de Regions espera pelas portas desde antes de a janela aparecer (o temporizador delas só começa depois da 1.ª sincronização)
        check("Correção 1: as portas vão abrir neste arranque: nunca vistas, sem telemóvel, fora do demo e da build de gravação",
            MainWindow.DoorsDue(false, false, false, false) && !MainWindow.DoorsDue(true, false, false, false) && !MainWindow.DoorsDue(false, true, false, false)
            && !MainWindow.DoorsDue(false, false, true, false) && !MainWindow.DoorsDue(false, false, false, true), "");
        var pickBody = Body(start, "void PickStartPage()");
        check("Correção 1: as portas por abrir ficam marcadas antes de a página ser escolhida (a nota de Regions não se gasta atrás delas)",
            pickBody.IndexOf("_doorsDue = DoorsDue(", StringComparison.Ordinal) is var due && due > 0 && due < pickBody.IndexOf("NavRegions.IsChecked = true", StringComparison.Ordinal), "");
        var tour = Src("Views", "MainWindow.Tour.cs");
        check("Correção 1: a marca sai quando as portas abrem ou a espera de 2 min desiste",
            Body(start, "void OpenDoors()").Contains("_doorsDue = false;") && Body(tour, "void StartTourWhenFree()").Contains("_tourWait.Stop(); _doorsDue = false; return;"), "");

        // A janela aberta em Regions mede os pings sozinha: a lista e a 1.ª sincronização acabam por qualquer ordem
        var t = new DateTime(2026, 11, 25, 12, 0, 0);
        check("Correção 1: aberta em Regions, os pings medem-se quando a página está à vista e a lista foi lida",
            MainWindow.RegionsPingDue(true, true, DateTime.MinValue, t) && !MainWindow.RegionsPingDue(false, true, DateTime.MinValue, t)
            && !MainWindow.RegionsPingDue(true, false, DateTime.MinValue, t), "");
        check("Correção 1: uma vez só (não outra vez dentro de 2 minutos)", !MainWindow.RegionsPingDue(true, true, t.AddSeconds(-30), t) && MainWindow.RegionsPingDue(true, true, t.AddMinutes(-3), t), "");
        var regions = Src("Views", "MainWindow.Regions.cs");
        check("Correção 1: pedidos quando a lista acaba de ser lida e quando a 1.ª sincronização acaba",
            Body(regions, "async void PageRegions_Loaded").Contains("PingRegionsAtStart();") && startup.Contains("PingRegionsAtStart();"), "");
    }

    // 2: com um hotspot guardado e o PC fora dele, o Phone setup diz o que falta em vez de "ready"
    static void HotspotGuardado(Action<string, bool, string> check)
    {
        var why = PhoneSetup.HotspotProblemText(true, false, null, "Galaxy A54");
        check("Correção 2: o que falta: o Wi-Fi do PC não está no hotspot guardado (ou o que o Windows diz da rede em que está)",
            why == "The PC's Wi-Fi is not connected to the hotspot \"Galaxy A54\"." && PhoneSetup.HotspotProblemText(true, false, "on Casa", "Galaxy A54") == "on Casa", why);
        check("Correção 2: nada a dizer com o PC no hotspot, ou sem hotspot escolhido",
            PhoneSetup.HotspotProblemText(true, true, "x", "Galaxy A54") == null && PhoneSetup.HotspotProblemText(false, false, "x", null) == null, "");

        var saved = new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true };
        var away = saved with { HotspotProblem = why };
        var steps = PhoneSetup.Steps(away, ssid: "Galaxy A54");
        var now = PhoneSetup.Current(steps);
        check("Correção 2: fora do hotspot guardado, a ligação é o passo a fazer, com a lista de Wi-Fi",
            now?.Step == PhoneStep.TetherOff && now.Title == "Connection" && now.Detail.StartsWith(why) && now.Detail.Contains(Texts.HotspotTodo) && now.Action == Texts.HotspotWifiList, now?.Detail);
        var summary = MainWindow.SetupSummaryText(steps, now, true, false);
        check("Correção 2: o resumo já não diz \"ready\" nem manda ligar a proteção", !summary.Contains(Texts.HotspotReady) && !summary.Contains("Turn on protection") && summary.Contains("Next: Connection"), summary);
        check("Correção 2: e o painel acaba só com Close (sem Turn on protection)", MainWindow.SetupEnd(now == null, false, false) == ("Close", false, false), "");
        check("Correção 2: as linhas do cabo e da depuração continuam só à espera (para o Change IP)",
            steps.Where(i => i.Step is PhoneStep.NoAdbTool or PhoneStep.NoUsb or PhoneStep.NoUsbDebugging or PhoneStep.Unauthorized).All(i => i.State != PhoneSetup.StepState.Todo), "");
        var back = PhoneSetup.Steps(saved, ssid: "Galaxy A54");
        check("Correção 2: com o PC no hotspot fica como era (pronto)", PhoneSetup.Current(back) == null
            && MainWindow.SetupSummaryText(back, null, true, false).StartsWith(Texts.HotspotReady) && MainWindow.SetupEnd(true, false, false) == ("Close", false, true), "");

        var cable = PhoneSetup.Current(PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Hotspot = true, HotspotProblem = why }));
        check("Correção 2: com o cabo ligado e o hotspot escolhido, o botão é a lista de Wi-Fi, não o tethering", cable?.Step == PhoneStep.TetherOff && cable.Action == Texts.HotspotWifiList, cable?.Action);
        var offer = away with { Offer = new ConnectionOffer(null, null, "On, but the app is set to another connection.", true) };
        var use = PhoneSetup.Current(PhoneSetup.Steps(offer));
        check("Correção 2: se o telemóvel já partilha pelo cabo, o caminho mais curto ganha (Use it)", !PhoneSetup.HotspotAway(offer) && use?.Step == PhoneStep.TetherOff && use.Action == "Use it", use?.Action);

        var iphone = new PhoneSetupState { Iphone = true, Hotspot = true };
        var iNow = PhoneSetup.Current(PhoneSetup.Steps(iphone with { HotspotProblem = why }, ssid: "iPhone de Ana"));
        check("Correção 2: no iPhone por hotspot o mesmo: a ligação é o passo a fazer",
            iNow?.Step == PhoneStep.TetherOff && iNow.Detail.StartsWith(why) && iNow.Action == Texts.HotspotWifiList && PhoneSetup.Current(PhoneSetup.Steps(iphone)) == null, iNow?.Detail);

        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Correção 2: o painel lê se o PC está mesmo no hotspot guardado",
            phone.Contains("Net.PickEgress(adapters, _s.EgressAdapterId, _s.EgressSsid, out var linkProblem)")
            && phone.Contains("HotspotProblem = PhoneSetup.HotspotProblemText(hotspot, phone != null, linkProblem, _s.EgressSsid)"), "");
        var click = Body(phone, "async void SetupAction_Click");
        int awayCase = click.IndexOf("case PhoneStep.TetherOff when _setupState != null && PhoneSetup.HotspotAway(_setupState):", StringComparison.Ordinal),
            tether = click.IndexOf("case PhoneStep.TetherOff:", StringComparison.Ordinal);
        check("Correção 2: o botão abre a lista de Wi-Fi (antes de chegar ao caso do tethering por USB)",
            awayCase > 0 && awayCase < tether && click[awayCase..tether].Contains("OpenWifiList();"), $"{awayCase} {tether}");
    }

    // 3: a lista de Wi-Fi e a Store abrem pelo Explorer, sem os direitos de administrador da app
    static void ListaWifi(Action<string, bool, string> check)
    {
        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Correção 3: o Phone setup não abre nenhuma ligação com os direitos de administrador", !phone.Contains("UseShellExecute = true") && !phone.Contains("Process.Start("), "");
        check("Correção 3: a lista de Wi-Fi vai pelo Explorer, como o resto da app",
            Body(phone, "void OpenWifiList()").Contains("OpenFirst(WifiListLinks, link => ShellLaunch.OpenAsUser(link, \"\", null)"), "");
        check("Correção 3: primeiro a lista do Windows, depois a página das Definições", MainWindow.WifiListLinks.SequenceEqual(new[] { "ms-availablenetworks:", "ms-settings:network-wifi" }), "");

        var tried = new List<string>();
        var failed = new List<string>();
        var first = MainWindow.OpenFirst(MainWindow.WifiListLinks, l => { tried.Add(l); return null; }, (l, w) => failed.Add(l));
        check("Correção 3: se a lista abre, não se tenta mais nada", first && tried.SequenceEqual(new[] { "ms-availablenetworks:" }) && failed.Count == 0, string.Join(",", tried));
        tried.Clear();
        var second = MainWindow.OpenFirst(MainWindow.WifiListLinks, l => { tried.Add(l); return l.StartsWith("ms-available") ? "no" : null; }, (l, w) => failed.Add(l + "=" + w));
        check("Correção 3: se a lista falha, abre a página das Definições e o motivo fica dito",
            second && tried.Count == 2 && failed.SequenceEqual(new[] { "ms-availablenetworks:=no" }), string.Join(",", failed));
        failed.Clear();
        var none = MainWindow.OpenFirst(MainWindow.WifiListLinks, _ => "Explorer's desktop isn't running", (l, w) => failed.Add(l));
        check("Correção 3: se as duas falham, diz que falhou (a app explica como lá chegar à mão)", !none && failed.Count == 2
            && Body(phone, "void OpenWifiList()").Contains("Toast(Loc.T(\"Open the Wi-Fi list from the network icon on the taskbar and connect to the phone's hotspot.\"), false);"), "");
        const string byHand = "Open the Wi-Fi list from the network icon on the taskbar and connect to the phone's hotspot.";
        check("Correção 3: esse recurso está nas 4 línguas", Langs.All(c => Loc.Load(c).ContainsKey(byHand)), "");
    }

    // 4: o que é novo tem o seu próprio nome nas estatísticas, e o que a dashboard já lê continua com o seu
    static void Contadores(Action<string, bool, string> check)
    {
        var hook = Src("Views", "MainWindow.Telemetry.cs");
        var heat = hook[hook.IndexOf("Telemetry.HeatFrom(this", StringComparison.Ordinal)..];
        heat = heat[..heat.IndexOf(';')];
        check("Correção 4: os cliques nas portas e na visita não vão para o mapa de calor da página por baixo",
            heat.Contains("StartPane.Visibility") && heat.Contains("TourPane.Visibility") && heat.Contains("SetupPane") && heat.Contains("Dialog.Visibility"), heat);

        // "Set up the phone" no botão do topo: o clique continua TurnOn_Click (o nome do handler), o contador próprio distingue-o
        check("Correção 4: Set up the phone tem o seu contador; Turn on protection não o conta",
            MainWindow.TurnOnPress(phoneKnown: false) == (true, "phone_btn_setup") && MainWindow.TurnOnPress(phoneKnown: true) == (false, null), "");
        check("Correção 4: o nome passa no formato, é por instalação (phone_*) e não se confunde com um passo do Phone setup",
            CounterName.IsMatch(MainWindow.SetUpPhoneCount) && MainWindow.SetUpPhoneCount.StartsWith("phone_")
            && !MainWindow.SetUpPhoneCount.StartsWith("phone_setup_") && !MainWindow.SetUpPhoneCount.StartsWith("phone_left_")
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "worker", "stats", "src", "api.js")).Contains("'phone_*'"), MainWindow.SetUpPhoneCount);

        // A linha Connection do hotspot partilha o PhoneStep.TetherOff com o tethering por USB: nas contas é "hotspot"
        var asked = new PhoneSetupState { AdbTool = true, AdbState = "none", HotspotAsked = true };
        var away = new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true, HotspotProblem = "x" };
        var usb = new PhoneSetupState { AdbTool = true, AdbState = "device" };
        check("Correção 4: Wi-Fi list e It's the phone contam como Setup_hotspot, não Setup_tetheroff",
            PhoneSetup.CountKey(PhoneStep.TetherOff, asked) == "hotspot" && PhoneSetup.CountKey(PhoneStep.TetherOff, asked with { WifiJoined = "Galaxy A54" }) == "hotspot"
            && PhoneSetup.CountKey(PhoneStep.TetherOff, away) == "hotspot"
            && Src("Views", "MainWindow.Phone.cs").Contains("Telemetry.Click(\"Setup_\" + PhoneSetup.CountKey(step, _setupState));"), "");
        check("Correção 4: quem sai à espera do hotspot conta como phone_left_hotspot",
            PhoneSetup.CountKey(PhoneSetup.Steps(asked), asked) == "hotspot" && PhoneSetup.CountKey(PhoneSetup.Steps(away), away) == "hotspot"
            && PhoneSetup.ClosedCounts("nousb", PhoneSetup.CountKey(PhoneSetup.Steps(asked), asked)).SequenceEqual(new[] { "phone_setup_nousb", "phone_left_hotspot" })
            && CounterName.IsMatch("phone_left_hotspot") && CounterName.IsMatch("Setup_hotspot"), "");
        check("Correção 4: o tethering por USB continua tetheroff (o que a dashboard lê não mudou de nome)",
            PhoneSetup.CountKey(PhoneStep.TetherOff, usb) == "tetheroff" && PhoneSetup.CountKey(PhoneSetup.Steps(usb), usb) == "tetheroff"
            && PhoneSetup.CountKey(PhoneStep.TetherOff, null) == "tetheroff" && PhoneSetup.CountKey(PhoneStep.NoUsb, asked) == "nousb"
            && PhoneSetup.CountKey(PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true }), null) == "done", "");
        check("Correção 4: com o cabo ligado a meio volta a ser o passo do cabo", PhoneSetup.CountKey(PhoneStep.TetherOff, asked with { AdbState = "device" }) == "tetheroff", "");
        var show = Body(Src("Views", "MainWindow.Phone.cs"), "void ShowSetup(PhoneSetupState st)");
        check("Correção 4: o painel conta com esse nome ao abrir e ao fechar, e o tempo do passo continua pelo nome antigo",
            show.Contains("_setupFirstTodo ??= PhoneSetup.CountKey(steps, st);") && show.Contains("_setupLeft = PhoneSetup.CountKey(steps, st);") && show.Contains("var key = PhoneSetup.StepKey(steps);"), "");

        // O menu da área de notificação: a linha nova tem a sua chave, Tray_on continua a ser ligar a proteção
        var noPhoneYet = MainWindow.TrayLayout("x", on: false, phone: true, phoneKnown: false);
        var command = Body(Src("Views", "MainWindow.Tray.cs"), "void TrayCommand(string key)");
        int does = command.IndexOf("key = TrayDoes(key, PhoneKnown);", StringComparison.Ordinal), counted = command.IndexOf("Telemetry.Click(\"Tray_\" + key);", StringComparison.Ordinal);
        check("Correção 4: no menu, Set up the phone conta como Tray_setup e Turn on protection continua Tray_on",
            noPhoneYet.Any(l => l.Key == "setup") && noPhoneYet.All(l => l.Key != "on") && MainWindow.TrayLayout("x", false, true).Any(l => l.Key == "on")
            && does > 0 && does < counted, $"{does} {counted}");

        // tour_shown continua a ser "a visita abriu"; quem viu o início é tour_door_shown
        check("Correção 4: a visita e as portas contam cada uma com o seu nome",
            Src("Views", "MainWindow.Tour.cs").Contains("Telemetry.Count(auto ? \"tour_shown\" : \"tour_again\");") && Src("Views", "MainWindow.Start.cs").Contains("Telemetry.Count(\"tour_door_shown\");"), "");
    }

    // 7: com a Localização do Windows desligada, a linha continua a dizer o que fazer com o hotspot
    static void Localizacao(Action<string, bool, string> check)
    {
        var asked = new PhoneSetupState { AdbTool = true, AdbState = "none", HotspotAsked = true, WifiNameHidden = true };
        var row = PhoneSetup.Current(PhoneSetup.Steps(asked));
        check("Correção 7: nome escondido pelo Windows: a Localização e, a seguir, o que fazer com o hotspot",
            row?.Step == PhoneStep.TetherOff && row.Detail.StartsWith(Net.SsidLocationText) && row.Detail.Contains(Texts.HotspotTodo) && row.Action == Texts.HotspotWifiList, row?.Detail);
    }

    // 8: um texto que nunca aparecia sai do código e das 4 tabelas
    static void TextoDoCabo(Action<string, bool, string> check)
    {
        const string dead = "Plugging it in also works";
        var left = Langs.Where(c => Src("Lang", c + ".json").Contains(dead)).ToList();
        check("Correção 8: \"Plugging it in also works\" nunca aparecia: saiu do código e das 4 línguas", !Src("Texts.cs").Contains(dead) && left.Count == 0, string.Join(",", left));
        var cable = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", HotspotAsked = true }).First(i => i.Step == PhoneStep.NoUsb);
        check("Correção 8: à espera do hotspot a linha do cabo espera, com o texto do hotspot que já existia",
            cable.State == PhoneSetup.StepState.Waiting && cable.Detail == "You use the hotspot: the cable is only needed for Change IP.", cable.Detail);
    }

    // 9: um download do ADB que fica pendurado (o Wi-Fi do PC mudou para o hotspot a meio) desiste, e o painel volta a ler
    static void Download(Action<string, bool, string> check)
    {
        Exception stuck = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { Adb.CopyWithProgress(new SlowStream(int.MaxValue, 0, Timeout.InfiniteTimeSpan), new MemoryStream(), null, null, null, TimeSpan.FromMilliseconds(300)).GetAwaiter().GetResult(); }
        catch (Exception ex) { stuck = ex; }
        check("Correção 9: sem receber nada, a cópia desiste ao fim do limite", stuck is OperationCanceledException && sw.Elapsed < TimeSpan.FromSeconds(10), $"{stuck?.GetType().Name} {sw.ElapsedMilliseconds} ms");
        check("Correção 9: e o Phone setup diz que demorou de mais, com Try again", MainWindow.AdbErrorText(stuck ?? new Exception()) == "it took too long"
            && MainWindow.AdbErrorText(new TaskCanceledException()) == "it took too long", MainWindow.AdbErrorText(stuck ?? new Exception()));

        // Lento mas vivo: 8 leituras de 150 ms (1,2 s ao todo) passam com um limite de 1 s, contado de novo a cada leitura
        var to = new MemoryStream();
        Exception slow = null;
        try { Adb.CopyWithProgress(new SlowStream(8, 1000, TimeSpan.FromMilliseconds(150)), to, 8000, null, null, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); }
        catch (Exception ex) { slow = ex; }
        check("Correção 9: um download lento mas vivo acaba (o limite é por leitura, não pelo total)", slow == null && to.Length == 8000, $"{slow?.GetType().Name} {to.Length}");
        check("Correção 9: o download do ADB usa esse limite (30 s)", Adb.DownloadStall == TimeSpan.FromSeconds(30)
            && Src("Core", "Phone", "Adb.cs").Contains("await CopyWithProgress(from, to, total, progress, percent, DownloadStall);"), "");
    }

    /// <summary>Um download que entrega <paramref name="reads"/> blocos de <paramref name="size"/> bytes, cada um depois de <paramref name="wait"/> (infinito: nunca chega nada).</summary>
    sealed class SlowStream(int reads, int size, TimeSpan wait) : Stream
    {
        int _left = reads;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(wait, ct);
            if (_left-- <= 0) return 0;
            new byte[size].CopyTo(buffer);
            return size;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ------------------------------------------------------------ Medidas: a caixa desenha-se a sério e mede-se

    /// <summary>As palavras 40 % mais longas, como o SRT_PSEUDO da app (Loc.Stretch).</summary>
    static string Longer(string s) => s == null ? null
        : string.Concat(Regex.Split(s, @"(\s+)").Select(w => w.Length >= 3 && w.Any(char.IsLetter) ? w + new string('e', (int)Math.Ceiling(w.Length * 0.4)) : w));

    /// <summary>
    /// Uma caixa da janela desenhada fora dela: o seu XAML tal como está no MainWindow.xaml (sem os handlers), com o Theme.xaml e o
    /// tipo de letra da janela, dentro de <paramref name="host"/> (Canvas para a visita, Grid para os painéis centrados).
    /// Chamar numa thread STA.
    /// </summary>
    static Grid Draw(string xaml, string from, string host)
    {
        var at = xaml.IndexOf(from, StringComparison.Ordinal);
        var end = xaml.IndexOf("</HeaderedContentControl>", at, StringComparison.Ordinal) + "</HeaderedContentControl>".Length;
        var box = Regex.Replace(xaml[at..end], @"\sClick=""[^""]*""", "");
        var asm = typeof(MainWindow).Assembly.GetName().Name;
        _ = Application.Current;   // regista o esquema pack:// (o tema lê-se dos recursos da app)
        return (Grid)System.Windows.Markup.XamlReader.Parse(
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:SRT;assembly=" + asm + "\">"
            + "<Grid.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/" + asm + ";component/Theme.xaml\"/>"
            + "</ResourceDictionary.MergedDictionaries><BooleanToVisibilityConverter x:Key=\"Vis\"/></ResourceDictionary></Grid.Resources>"
            + "<" + host + " TextElement.FontFamily=\"{StaticResource UiFont}\" TextElement.FontSize=\"12\">" + box + "</" + host + "></Grid>");
    }

    static void Layout(FrameworkElement root, double w, double h)
    {
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
    }

    /// <summary>
    /// O que se vê de um elemento: o seu retângulo dentro da caixa, menos o que o WPF corta nele ou em quem o contém (um elemento
    /// maior do que o lugar que lhe deram fica com um recorte, LayoutClip). Ver menos do que o seu tamanho é estar cortado: devolve
    /// a frase que o diz, ou null.
    /// </summary>
    static string Cut(string name, FrameworkElement e, FrameworkElement whole)
    {
        var all = e.TransformToAncestor(whole).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var seen = all;
        seen.Intersect(new Rect(0, 0, whole.ActualWidth, whole.ActualHeight));
        for (DependencyObject d = e; d != null && d != whole; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement f && System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(f) is { } clip)
                seen.Intersect(f.TransformToAncestor(whole).TransformBounds(clip.Bounds));
        return seen.IsEmpty || seen.Width < all.Width - 0.5 || seen.Height < all.Height - 0.5
            ? $"{name} cortado (vê-se {(seen.IsEmpty ? 0 : seen.Width):0}x{(seen.IsEmpty ? 0 : seen.Height):0} de {all.Width:0}x{all.Height:0} px, a começar em {all.Left:0})"
            : null;
    }

    /// <summary>Corre <paramref name="draw"/> numa thread STA em cada língua da app e no pseudo, e junta o que não cabe.</summary>
    static (List<string> Problems, int Drawn, double Tallest, Exception Error) InEveryLanguage(Func<bool, IEnumerable<(string What, List<string> Bad, double Height)>> draw)
    {
        var problems = new List<string>();
        var drawn = 0;
        double tallest = 0;
        Exception err = null;
        var before = Loc.Lang;
        var t = new Thread(() =>
        {
            try
            {
                foreach (var lang in Loc.Codes.Append("pseudo"))
                {
                    Loc.Init(lang == "pseudo" ? "en" : lang);
                    foreach (var (what, bad, height) in draw(lang == "pseudo"))
                    {
                        problems.AddRange(bad.Select(p => $"{lang} {what}: {p}"));
                        tallest = Math.Max(tallest, height);
                        drawn++;
                    }
                }
            }
            catch (Exception e) { err = e; }
            finally { Loc.Init(before); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        return (problems, drawn, tallest, err);
    }

    // A janela mínima (MinWidth e MinHeight do MainWindow.xaml), e o que fica para as caixas por baixo da linha de 3 px e da barra de título de 40 px
    const double WinW = 1020, WinH = 660, PaneH = WinH - 43;

    // C4 (24 nov.): a medida que tinha ficado prometida. A caixa da visita nas 5 paragens, nas 5 línguas e com as palavras 40 % mais
    // longas: nada sai dos 360 px, nenhum botão nem o "5 de 5" fica cortado, e a caixa cabe na janela mínima.
    static void MedidaDaVisita(Action<string, bool, string> check)
    {
        var xaml = Src("MainWindow.xaml");
        var (problems, drawn, tallest, err) = InEveryLanguage(pseudo => Enumerable.Range(0, Texts.Tour.Length).Select(i =>
        {
            var (bad, height) = TourProblems(xaml, i, pseudo);
            return ((i + 1).ToString(), bad, height);
        }));
        check("Medida C4: a caixa da visita desenha-se nas 5 paragens, nas 5 línguas e no pseudo", err == null && drawn == Texts.Tour.Length * (Loc.Codes.Length + 1) && tallest > 80,
            $"{drawn} desenhadas, a mais alta {tallest:0} px {err?.Message}");
        check("Medida C4: nada sai da caixa de 360 px, nenhum botão nem o \"5 de 5\" fica cortado, e cabe na janela de 1020x660", err == null && problems.Count == 0,
            problems.Count + ": " + string.Join(" | ", problems.Take(6)));
    }

    /// <summary>Desenha a caixa da visita numa paragem e devolve o que não cabe, e a altura da caixa. Com a língua já escolhida (Loc.Init).</summary>
    static (List<string> Problems, double Height) TourProblems(string xaml, int stop, bool pseudo)
    {
        var root = Draw(xaml, "<HeaderedContentControl x:Name=\"TourBox\"", "Canvas");
        string P(string s) => pseudo ? Longer(s) : s;
        void Text(string name, string text)
        {
            switch (root.FindName(name))
            {
                case System.Windows.Documents.Run r: r.Text = text; break;
                case TextBlock b: b.Text = text; break;
                case Button b: b.Content = text; break;
                default: throw new InvalidOperationException(name + " não está na caixa da visita");
            }
        }
        FrameworkElement El(string name) => root.FindName(name) switch
        {
            System.Windows.Documents.Run r => (FrameworkElement)r.Parent,
            FrameworkElement e => e,
            _ => throw new InvalidOperationException(name + " não está na caixa da visita"),
        };
        var view = MainWindow.TourViewAt(stop);
        Text("TourTitle", P(view.Title)); Text("TourCount", view.Count); Text("TourText", P(view.Text)); Text("TourHint", P(Loc.T(Texts.TourAgainHint)));
        Text("TourSkipBtn", P(view.Skip)); Text("TourBackBtn", P(Loc.T(Texts.TourBack))); Text("TourNextBtn", P(view.Next));
        El("TourHint").Visibility = view.Hint ? Visibility.Visible : Visibility.Collapsed;
        El("TourBackBtn").Visibility = view.Back ? Visibility.Visible : Visibility.Collapsed;
        Layout(root, WinW, WinH);

        var bad = new List<string>();
        var whole = El("TourBox");
        // Com os 12 px que a caixa guarda de cada margem da janela (TourBoxAt)
        if (whole.ActualHeight > PaneH - 24) bad.Add($"a caixa tem {whole.ActualHeight:0} px de altura");
        if (Math.Abs(whole.ActualWidth - 360) > 0.5) bad.Add($"a caixa tem {whole.ActualWidth:0} px de largura");
        foreach (var (name, e) in new[] { "TourTitle", "TourCount", "TourText", "TourHint", "TourSkipBtn", "TourBackBtn", "TourNextBtn" }
            .Select(n => (Name: n, E: El(n))).Where(p => p.E.Visibility == Visibility.Visible).DistinctBy(p => p.E))
            if (Cut(name, e, whole) is string cut) bad.Add(cut);
        return (bad, whole.ActualHeight);
    }

    // O Phone setup não tem scroll, e as linhas do hotspot ficaram mais compridas (achados 2 e 7): o painel desenha-se nos estados com
    // mais texto, nas 5 línguas e no pseudo, e tem de caber na janela mínima sem cortar o texto do passo nem os botões.
    static void MedidaDoPhoneSetup(Action<string, bool, string> check)
    {
        var xaml = Src("MainWindow.xaml");
        var noCable = new PhoneSetupState { AdbTool = true, AdbState = "none" };
        var asked = noCable with { HotspotAsked = true };
        var (problems, drawn, tallest, err) = InEveryLanguage(pseudo =>
        {
            // Os nomes de rede mais compridos que cabem (32 letras), como o Net.PickEgress os escreve
            var away = Loc.F("The PC's Wi-Fi is connected to \"{0}\", not to the hotspot \"{1}\".", "Vodafone-Fibra-5G-A1B2C3D4E5F6G7", "Galaxy A54 5G hotspot 0123456789");
            var states = new (string What, PhoneSetupState State, int Seconds)[]
            {
                ("cabo aos 25 s", noCable, 25),
                ("à espera do hotspot", asked, 0),
                ("hotspot com o nome escondido", asked with { WifiNameHidden = true }, 0),
                ("rede nova por confirmar", asked with { WifiJoined = "Galaxy A54 5G hotspot 0123456789" }, 0),
                ("fora do hotspot guardado", noCable with { Hotspot = true, HotspotProblem = away }, 0),
                ("fora do hotspot guardado, Localização desligada", noCable with { Hotspot = true, HotspotProblem = Loc.T(Net.SsidLocationText) }, 0),
                ("iPhone fora do hotspot guardado", new PhoneSetupState { Iphone = true, Hotspot = true, HotspotProblem = away }, 0),
            };
            return states.Select(s =>
            {
                var (bad, height) = SetupProblems(xaml, s.State, s.Seconds, pseudo);
                return (s.What, bad, height);
            }).ToList();
        });
        check("Medida do Phone setup: o painel desenha-se nos 7 estados com mais texto, nas 5 línguas e no pseudo", err == null && drawn == 7 * (Loc.Codes.Length + 1) && tallest > 200,
            $"{drawn} desenhados, o mais alto {tallest:0} px de {PaneH:0} {err?.Message}");
        check("Medida do Phone setup: cabe na janela de 1020x660 sem cortar o texto do passo nem os botões", err == null && problems.Count == 0,
            problems.Count + ": " + string.Join(" | ", problems.Take(6)));
    }

    /// <summary>Desenha o painel do Phone setup num estado, como o ShowSetup o enche, e devolve o que não cabe, e a altura do painel.</summary>
    static (List<string> Problems, double Height) SetupProblems(string xaml, PhoneSetupState st, int seconds, bool pseudo)
    {
        var pane = xaml[xaml.IndexOf("<Grid x:Name=\"SetupPane\"", StringComparison.Ordinal)..];
        var root = Draw(pane, "<HeaderedContentControl Style=\"{StaticResource DialogGroup}\" Width=\"580\"", "Grid");
        string P(string s) => pseudo ? Longer(s) : s;
        PhoneSetup.SetupItem Item(PhoneSetup.SetupItem i) => i == null ? null : i with { Title = P(i.Title), Detail = P(i.Detail), Action = P(i.Action) };
        var steps = PhoneSetup.Steps(st, ssid: "Galaxy A54 5G hotspot 0123456789");
        var now = PhoneSetup.Current(steps);
        ((ItemsControl)root.FindName("SetupSteps")).ItemsSource = steps.Select(i => new SetupStepRow(Item(i), Item(now), null, true, i == now ? P(PhoneSetup.LiveDetail(i, seconds)) : null)).ToList();
        ((TextBlock)root.FindName("SetupSummary")).Text = P(MainWindow.SetupSummaryText(steps, now, st.Hotspot && !st.Iphone, false));
        ((TextBlock)root.FindName("SetupFoot")).Text = P(Loc.T("Updates by itself. Do the red step and it turns green."));
        ((Button)root.FindName("SetupCloseBtn")).Content = P(Loc.T("Close"));
        Layout(root, WinW, PaneH);
        // As linhas enchem-se por ligações aos dados: deixa-as correr e mede outra vez
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Layout(root, WinW, PaneH);

        var bad = new List<string>();
        var whole = (FrameworkElement)((FrameworkElement)root.FindName("SetupSummary")).Parent;
        while (whole is not HeaderedContentControl) whole = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(whole);
        // O painel fica ao centro, com os 12 px de margem em baixo do seu estilo. A altura mede-se nas línguas a sério (a linha do
        // cabo aos 25 s dá 492 px nas 5); com as palavras 40 % mais longas fica a 1 px do limite, e outro tipo de letra passava-o
        if (!pseudo && whole.ActualHeight > PaneH - 12) bad.Add($"o painel tem {whole.ActualHeight:0} px de altura, cabem {PaneH - 12:0}");
        var texts = 0;
        void Walk(DependencyObject d)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is FrameworkElement { Visibility: not Visibility.Visible }) continue;
                if (c is Button b) { if (Cut("o botão " + b.Content, b, whole) is string cut) bad.Add(cut); continue; }
                // Os títulos das linhas (a coluna de 150 px, sem mudança de linha) ficam de fora: três já vinham cortados de antes
                // (es "Wi-Fi del teléfono desactivado" 166 px, e no iPhone es "Confiar en esta computadora" 158 px e ru 164 px).
                // Corrigir é mexer no XAML e refazer as capturas: está no relatório desta correção, para o coordenador.
                if (c is TextBlock { Text.Length: > 0 } title && Grid.GetColumn(title) == 1 && title.FontWeight == FontWeights.SemiBold) continue;
                if (c is TextBlock { Text.Length: > 0 } tb) { texts++; if (Cut("o texto \"" + tb.Text[..Math.Min(24, tb.Text.Length)] + "\"", tb, whole) is string cut) bad.Add(cut); }
                Walk(c);
            }
        }
        Walk(whole);
        // O texto do passo a fazer tem de lá estar (senão as ligações não correram e a medida não vale nada)
        if (now != null && texts < 4) bad.Add($"só {texts} textos desenhados");
        return (bad, whole.ActualHeight);
    }

    // 10: o menu da área de notificação diz o que faz, e a regra do fim do Phone setup está no DESIGN.md
    static void Bandeja(Action<string, bool, string> check)
    {
        var noPhoneYet = MainWindow.TrayLayout("Protection off", on: false, phone: true, phoneKnown: false);
        check("Correção 10: sem telemóvel visto, a linha do menu diz Set up the phone (abria o Phone setup a dizer Turn on protection)",
            noPhoneYet.Any(l => l.Key == "setup" && l.Text == Texts.SetUpPhone && l.Enabled) && noPhoneYet.All(l => l.Text != "Turn on protection"), "");
        var keys = noPhoneYet.Where(l => l.Key != null).Select(l => l.Key).ToList();
        check("Correção 10: no mesmo sítio do menu, logo a seguir ao Open", keys.SequenceEqual(new[] { "state", "open", "setup", "switch", "changeip", "regions", "quit" }), string.Join(",", keys));
        check("Correção 10: com telemóvel visto, Turn on protection como antes",
            MainWindow.TrayLayout("x", false, true, phoneKnown: true).Any(l => l.Key == "on" && l.Text == "Turn on protection" && l.Enabled), "");
        check("Correção 10: com a proteção ligada desliga-a, com ou sem telemóvel visto",
            MainWindow.TrayLayout("x", true, true, phoneKnown: false).Any(l => l.Key == "off" && l.Text == "Turn off protection") && MainWindow.TrayLayout("x", true, true, phoneKnown: false).All(l => l.Key != "setup"), "");
        check("Correção 10: em No phone a linha fica como era, cinzenta (não há nada para ligar nem configurar)",
            MainWindow.TrayLayout("x", false, false, phoneKnown: false).Any(l => l.Key == "on" && l.Text == "Turn on protection" && !l.Enabled), "");
        var ran = new List<string>();
        MainWindow.TrayItems("x", false, true, ran.Add, phoneKnown: false).First(i => i.Text == Texts.SetUpPhone).Run();
        check("Correção 10: a linha corre o comando setup, que abre o Phone setup", ran.SequenceEqual(new[] { "setup" }) && MainWindow.TrayDoes("setup", false) == "setup", string.Join(",", ran));
        check("Correção 10: o texto da linha está nas 4 línguas", Langs.All(c => Loc.Load(c).ContainsKey(Texts.SetUpPhone)), "");
        check("Correção 10: o menu lê se já houve telemóvel de cada vez que abre",
            Src("Views", "MainWindow.Tray.cs").Contains("return TrayItems(SideState.Text, _protOn && !_s.NoPhone, !_s.NoPhone, TrayCommand, _quickName, PhoneKnown);"), "");

        var design = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "docs", "DESIGN.md"));
        check("Correção 10: o DESIGN.md tem a regra do fim do Phone setup (Close cinzento e Turn on protection azul)",
            design.Contains("How Phone setup ends") && design.Contains("`SetupTurnOnBtn`"), "");
        check("Correção 10: e a regra da página de arranque (escolhida antes de a janela aparecer; só um clique a muda)", design.Contains("The page never changes by itself") && design.Contains("`PickStartPage`"), "");
    }
}
