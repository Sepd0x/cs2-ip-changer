using System.IO;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 24 nov. (estudo de usabilidade, publish/plano-1-0-0/1-app-usabilidade.md): 46 de 57 instalações nunca ligaram o telemóvel e usam a
// app para escolher regiões. Cada mudança aprovada pelo dono tem aqui o seu teste: A (as duas portas), B (o hotspot desde o 1.º
// segundo no passo do cabo), C1 (o botão do topo sem telemóvel), C2 (o fim do Phone setup), C3 (a regra de regiões fica com a app
// fechada) e C4 (a última paragem do tour noutras línguas).
static class Usabilidade24NovTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    public static void Run(Action<string, bool, string> check)
    {
        Portas(check);
        Hotspot(check);
        BotaoDoTopo(check);
        FimDoSetup(check);
        RegrasFicam(check);
        Visita(check);
        Textos(check);
        Verificador(check);
    }

    // A: a primeira execução pergunta o que a pessoa quer, em vez de empurrar todos para o telemóvel
    static void Portas(Action<string, bool, string> check)
    {
        check("Usabilidade A: sem telemóvel visto, a janela abre em Regions", MainWindow.StartOnRegions(false, null, false) && MainWindow.StartOnRegions(false, "regions", false), "");
        check("Usabilidade A: quem escolheu o telemóvel, quem já tem telemóvel e o demo abrem na Connection",
            !MainWindow.StartOnRegions(false, "phone", false) && !MainWindow.StartOnRegions(true, null, false) && !MainWindow.StartOnRegions(false, null, true), "");
        check("Usabilidade A: as definições antigas não trazem escolha", new Settings().StartDoor == null, "");
        var name = new Regex(@"^[A-Za-z0-9_]{1,48}$");
        var counts = Enum.GetValues<Texts.Door>().Select(Texts.DoorCount).ToList();
        check("Usabilidade A: os contadores das portas são tour_door_* (o Worker junta tour_* por instalação, sem deploy)",
            counts.SequenceEqual(new[] { "tour_door_regions", "tour_door_phone", "tour_door_tour", "tour_door_later" }) && counts.All(name.IsMatch)
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "worker", "stats", "src", "api.js")).Contains("'tour_*'"), string.Join(",", counts));
        var xaml = Src("MainWindow.xaml");
        check("Usabilidade A: a caixa tem as duas portas, Show me around e Later",
            xaml.Contains("x:Name=\"StartPane\"") && xaml.Contains("Click=\"DoorRegions_Click\"") && xaml.Contains("Click=\"DoorPhone_Click\"")
            && xaml.Contains("Click=\"DoorTour_Click\"") && xaml.Contains("Click=\"DoorLater_Click\""), "");
        check("Usabilidade A: cada porta diz o que abre (antes não pareciam clicáveis)", xaml.Contains("Texts.DoorRegionsGo") && xaml.Contains("<Run Text=\"{x:Static local:Texts.SetUpPhone}\"/>"), "");
        check("Usabilidade A: os cartões têm estilo no tema (sem estilo o WPF carrega o tema do Windows)",
            Src("Theme.xaml").Contains("x:Key=\"Door\"") && Regex.Matches(xaml, "Style=\"\\{StaticResource Door\\}\"").Count == 2, "");
        var tour = Src("Views", "MainWindow.Tour.cs");
        check("Usabilidade A: abrem onde a visita abria sozinha (uma vez, antes de qualquer telemóvel)", tour.Contains("OpenDoors();") && !tour.Contains("OpenTour(auto: true);"), "");
        var start = Src("Views", "MainWindow.Start.cs");
        check("Usabilidade A: vistas ficam guardadas ao abrir, e a visita continua atrás de Show me around",
            start.Contains("_s.TourSeen = true;") && start.Contains("OpenTour(auto: true);"), "");
        // 25 nov.: a decisão é uma função (antes o teste só procurava a linha no ficheiro)
        check("Usabilidade A: Esc é Later, só com as portas abertas, e mais nenhuma tecla é delas",
            MainWindow.DoorsEsc(true, System.Windows.Input.Key.Escape) && !MainWindow.DoorsEsc(false, System.Windows.Input.Key.Escape)
            && !MainWindow.DoorsEsc(true, System.Windows.Input.Key.Enter) && !MainWindow.DoorsEsc(true, System.Windows.Input.Key.Right)
            && Src("Views", "MainWindow.Feedback.cs").Contains("DoorsKey(e.Key)"), "");
        // 25 nov.: a página escolhe-se antes de a janela aparecer (Correcao25NovTests tem a ordem)
        check("Usabilidade A: a janela abre em Regions no arranque", start.Contains("if (StartOnRegions(_s.PhoneSeen, _s.StartDoor, _demo)) NavRegions.IsChecked = true;")
            && Src("MainWindow.xaml.cs").Contains("PickStartPage();"), "");
        check("Usabilidade A: o Before you start já não diz que o telemóvel vem a seguir", !Src("Views", "MainWindow.Phone.cs").Contains("The phone comes next"), "");
    }

    static Adapter Wifi(string ssid, bool up = true, bool hidden = false, string gateway = "192.168.1.1") =>
        new() { Id = "w" + ssid, Name = "Wi-Fi", IsWifi = true, Up = up, LocalIp = up ? "192.168.1.20" : null, Ssid = ssid, SsidUnknown = hidden, Gateway = gateway };

    // O verificador independente (24 nov.) achou estes casos; cada um tem aqui o seu teste
    static void Verificador(Action<string, bool, string> check)
    {
        var noCable = new PhoneSetupState { AdbTool = true, AdbState = "none", HotspotAsked = true };
        check("Verificador B1: com o cabo ligado a meio, o hotspot deixa de estar pendente (o botão volta ao tethering)",
            PhoneSetup.HotspotPending(noCable) && !PhoneSetup.HotspotPending(noCable with { Usb = new(true, null, "Google") })
            && !PhoneSetup.HotspotPending(noCable with { AdbState = "device" }) && !PhoneSetup.HotspotPending(noCable with { PcWifi = false }), "");
        check("Verificador B1: o clique só vai para o hotspot enquanto está pendente", Src("Views", "MainWindow.Phone.cs").Contains("case PhoneStep.TetherOff when _setupState != null && PhoneSetup.HotspotPending(_setupState):"), "");
        var noAdb = new PhoneSetupState { AdbTool = false, AdbState = "none", Hotspot = true };
        check("Verificador B2: no hotspot, sem a ferramenta (ou à espera do Install), o Phone setup fica pronto",
            PhoneSetup.Current(PhoneSetup.Steps(noAdb)) == null && PhoneSetup.Current(PhoneSetup.Steps(noAdb with { AdbWaits = true })) == null
            && PhoneSetup.Steps(noAdb)[0].State == PhoneSetup.StepState.Waiting && PhoneSetup.Steps(noAdb)[0].Detail == Texts.HotspotOnlyChangeIp, "");
        check("Verificador B2: a juntar-se ao hotspot sem a ferramenta, o passo é a ligação, não o download",
            PhoneSetup.Current(PhoneSetup.Steps(noAdb with { Hotspot = false, HotspotAsked = true }))?.Step == PhoneStep.TetherOff, "");
        var steps = PhoneSetup.Steps(new PhoneSetupState { AdbTool = false, AdbState = "none" });
        var cableRow = new SetupStepRow(steps.First(i => i.Step == PhoneStep.NoUsb), PhoneSetup.Current(steps), null, false);
        check("Verificador B2: Use the hotspot aparece já com a ferramenta a descarregar", PhoneSetup.Current(steps).Step == PhoneStep.NoAdbTool
            && cableRow.HasQuietAction && !cableRow.HasMainAction, "");
        check("Verificador B2: e funciona com a ferramenta ocupada", Src("Views", "MainWindow.Phone.cs").Contains("if (step == PhoneStep.NoUsb && row.Item.Quiet) { StartHotspot(); return; }"), "");
        check("Verificador B3: o Wi-Fi de casa com o nome escondido no clique não é oferecido quando o nome aparece",
            MainWindow.HotspotJoined(new[] { Wifi("Casa") }, null, "192.168.1.1") == null
            && MainWindow.HotspotJoined(new[] { Wifi("Galaxy A54", gateway: "192.168.43.1") }, null, "192.168.1.1")?.Ssid == "Galaxy A54", "");
        check("Verificador B3: ao confirmar, lê as redes outra vez", Src("Views", "MainWindow.Phone.cs").Contains("HotspotJoined(await Task.Run(Net.Scan), _hotspotFrom, _hotspotFromGateway)"), "");
        // 25 nov.: estes quatro passaram a testar o comportamento (antes só procuravam a linha no ficheiro)
        check("Verificador C1: o menu da área de notificação também só abre o Phone setup sem telemóvel",
            MainWindow.TrayDoes("on", phoneKnown: false) == "setup" && MainWindow.TrayDoes("on", phoneKnown: true) == "on"
            && MainWindow.TrayDoes("setup", phoneKnown: true) == "setup" && MainWindow.TrayDoes("off", phoneKnown: false) == "off"
            && Src("Views", "MainWindow.Tray.cs").Contains("case \"setup\": ShowFromTray(); NavNet.IsChecked = true; _ = RunPhoneSetup(); break;"), "");
        check("Verificador A: a nota de Regions espera pelas portas (abertas, ou ainda por abrir neste arranque)",
            MainWindow.PitchWaitsForDoors(doorsOpen: true, doorsDue: false) && MainWindow.PitchWaitsForDoors(false, true) && !MainWindow.PitchWaitsForDoors(false, false)
            && Src("Views", "MainWindow.Tour.cs").Contains("if (PitchWaitsForDoors(StartPane.Visibility == Visibility.Visible, _doorsDue)) return;")
            && Src("Views", "MainWindow.Start.cs").Contains("ShowRegionsPitch();"), "");
        var before = Loc.Lang;
        (string Regions, string Phone) names;
        try { Loc.Init("es"); names = MainWindow.DoorNames(); }
        finally { Loc.Init(before); }
        var es = Loc.Load("es");
        check("Verificador A: os nomes das portas para leitores de ecrã na língua da app",
            names == (es[Texts.DoorRegions], es[Texts.DoorPhone]) && names.Regions != Texts.DoorRegions && names.Phone != Texts.DoorPhone
            && MainWindow.DoorNames() == (Loc.T(Texts.DoorRegions), Loc.T(Texts.DoorPhone)), names.ToString());
        check("Verificador A: o Tab fica na caixa das portas",
            Src("MainWindow.xaml").Contains("x:Name=\"StartPane\" Grid.Row=\"1\" Background=\"#B0000000\" Visibility=\"Collapsed\" KeyboardNavigation.TabNavigation=\"Cycle\""), "");
    }

    // B: o hotspot do telemóvel desde o primeiro segundo, sem confundir a rede de casa com o telemóvel
    static void Hotspot(Action<string, bool, string> check)
    {
        var noCable = new PhoneSetupState { AdbTool = true, AdbState = "none" };
        var cable = PhoneSetup.Steps(noCable).First(i => i.Step == PhoneStep.NoUsb);
        check("Usabilidade B: o cabo oferece o hotspot logo, num botão cinzento", cable.State == PhoneSetup.StepState.Todo && cable.Action == Texts.HotspotUse && cable.Quiet
            && cable.Detail.Contains(Texts.CableHotspot) && cable.Detail.StartsWith(Texts.CableTodo), cable.Detail);
        check("Usabilidade B: o texto ao vivo continua (o painel nunca parece parado)", PhoneSetup.LiveDetail(cable, 4).Contains(Texts.CableLive(4)), "");
        var late = PhoneSetup.LiveDetail(cable, Texts.CableHelpAfterSeconds);
        check("Usabilidade B: aos 20 s só fala do cabo (o hotspot já está em cima)", late.Contains("another cable") && !late.Contains("How the PC connects to the phone"), late);
        var desk = PhoneSetup.Steps(noCable with { PcWifi = false }).First(i => i.Step == PhoneStep.NoUsb);
        check("Usabilidade B: um PC sem Wi-Fi não oferece o hotspot e diz porquê", desk.Action == null && desk.Detail.Contains(Texts.CableNoWifi), desk.Detail);

        var asked = noCable with { HotspotAsked = true };
        var steps = PhoneSetup.Steps(asked);
        var now = PhoneSetup.Current(steps);
        check("Usabilidade B: depois de Use the hotspot o cabo espera e a ligação é o passo a fazer",
            steps.First(i => i.Step == PhoneStep.NoUsb).State == PhoneSetup.StepState.Waiting && now?.Step == PhoneStep.TetherOff && now.Action == Texts.HotspotWifiList
            && now.Detail.Contains(Texts.HotspotAlready), now?.Detail);
        check("Usabilidade B: a depuração USB só espera, para o Change IP", steps.Where(i => i.Step is PhoneStep.NoUsbDebugging or PhoneStep.Unauthorized)
            .All(i => i.State == PhoneSetup.StepState.Waiting && i.Detail == Texts.HotspotOnlyChangeIp), "");
        check("Usabilidade B: o Wi-Fi do telemóvel fica ligado no hotspot", steps.First(i => i.Step == PhoneStep.WifiOn).State == PhoneSetup.StepState.Ok, "");
        var seen = PhoneSetup.Current(PhoneSetup.Steps(asked with { WifiJoined = "Galaxy A54" }));
        check("Usabilidade B: a rede nova é perguntada pelo nome, com It's the phone", seen.Detail == Texts.HotspotSeen("Galaxy A54") && seen.Action == Texts.HotspotItsPhone, seen.Detail);
        var hidden = PhoneSetup.Current(PhoneSetup.Steps(asked with { WifiNameHidden = true }));
        // 25 nov.: a seguir vem o que fazer com o hotspot (Correcao25NovTests, achado 7)
        check("Usabilidade B: nome escondido pelo Windows: diz como ligar a Localização", hidden.Detail.StartsWith(Net.SsidLocationText), hidden.Detail);
        var plugged = PhoneSetup.Steps(asked with { Usb = new(true, null, "Google") });
        check("Usabilidade B: um cabo ligado a meio volta ao caminho do cabo", plugged.First(i => i.Step == PhoneStep.NoUsb).State == PhoneSetup.StepState.Ok
            && PhoneSetup.Current(plugged)?.Step == PhoneStep.NoUsbDebugging, "");

        var picked = PhoneSetup.Steps(noCable with { Hotspot = true });
        check("Usabilidade B: no hotspot escolhido não sobra nada a fazer (antes ficava em \"3 de 6\")", PhoneSetup.Current(picked) == null && PhoneSetup.StepKey(picked) == "done", "");
        var summary = MainWindow.SetupSummaryText(picked, null, true, false);
        check("Usabilidade B: o resumo diz que o hotspot está pronto e o que falta", summary.StartsWith(Texts.HotspotReady) && summary.Contains("Turn on protection"), summary);
        check("Usabilidade B: o resto do resumo fica como era", MainWindow.SetupSummaryText(PhoneSetup.Steps(noCable), PhoneSetup.Current(PhoneSetup.Steps(noCable)), false, false).Contains("Next: USB cable"), "");

        check("Usabilidade B: o portátil que já estava no Wi-Fi de casa não o vê oferecido", MainWindow.HotspotJoined(new[] { Wifi("Casa") }, "Casa") == null, "");
        check("Usabilidade B: a rede para onde o PC mudou é a oferecida", MainWindow.HotspotJoined(new[] { Wifi("Galaxy A54") }, "Casa")?.Ssid == "Galaxy A54", "");
        check("Usabilidade B: um Wi-Fi desligado não conta", MainWindow.HotspotJoined(new[] { Wifi("Galaxy A54", up: false) }, null) == null, "");
        check("Usabilidade B: nome escondido só com um Wi-Fi ligado", MainWindow.HotspotNameHidden(new[] { Wifi(null, hidden: true) }) && !MainWindow.HotspotNameHidden(new[] { Wifi("Casa") }), "");

        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Usabilidade B: a confirmação é a mesma da lista da Connection", phone.Contains("HotspotAskText(ssid)") && Src("Views", "MainWindow.Protection.cs").Contains("HotspotAskText(a.Ssid)"), "");
        check("Usabilidade B: conta quem pede e quem fica no hotspot (phone_*, por instalação)", phone.Contains("Telemetry.Count(\"phone_hotspot_ask\");") && phone.Contains("Telemetry.Count(\"phone_hotspot_ok\");"), "");
        check("Usabilidade B: o botão cinzento e o azul na linha", Src("MainWindow.xaml").Contains("{Binding HasQuietAction") && Src("MainWindow.xaml").Contains("{Binding HasMainAction"), "");
    }

    // C1: sem telemóvel, o botão do topo só abre o Phone setup (11 dos 46 sem telemóvel carregaram em Turn on protection)
    static void BotaoDoTopo(Action<string, bool, string> check)
    {
        check("Usabilidade C1: nunca houve telemóvel: Set up the phone, cinzento", MainWindow.TurnOnLook(false) is (Texts.SetUpPhone, "Btn", Texts.SetUpPhoneTip), "");
        check("Usabilidade C1: com telemóvel visto: Turn on protection, azul, como antes", MainWindow.TurnOnLook(true) is ("Turn on protection", "Primary", MainWindow.TurnOnTip), "");
        var prot = Src("Views", "MainWindow.Protection.cs");
        // 25 nov.: a decisão é uma função (antes o teste só procurava a linha no ficheiro)
        check("Usabilidade C1: sem telemóvel não liga nada, só abre o Phone setup", MainWindow.TurnOnPress(phoneKnown: false).OnlySetup && !MainWindow.TurnOnPress(phoneKnown: true).OnlySetup
            && prot.Contains("if (TurnOnPress(PhoneKnown) is (true, var count)) { Telemetry.Count(count); await RunPhoneSetup(); return; }"), "");
        check("Usabilidade C1: segue o estado a cada relatório", prot.Contains("TurnOnLook(PhoneKnown)"), "");
        check("Usabilidade C1: a dica do XAML é a mesma de TurnOnTip", Src("MainWindow.xaml").Contains($"ToolTip=\"{MainWindow.TurnOnTip}\""), "");
    }

    // C2: Phone setup pronto com a proteção desligada acaba em Turn on protection, com Close ao lado
    static void FimDoSetup(Action<string, bool, string> check)
    {
        check("Usabilidade C2: pronto e desligado: Close e Turn on protection", MainWindow.SetupEnd(true, false, false) == ("Close", false, true), "");
        check("Usabilidade C2: pronto e ligado, ou No phone: Done como antes", MainWindow.SetupEnd(true, true, false) == ("Done", true, false) && MainWindow.SetupEnd(true, false, true) == ("Done", true, false), "");
        check("Usabilidade C2: por acabar: Close", MainWindow.SetupEnd(false, false, false) == ("Close", false, false), "");
        var phone = Src("Views", "MainWindow.Phone.cs");
        var handler = phone[phone.IndexOf("void SetupTurnOn_Click")..];
        check("Usabilidade C2: liga pelo mesmo caminho da Connection (com a pergunta do CS2 aberto)",
            Src("MainWindow.xaml").Contains("Click=\"SetupTurnOn_Click\"") && handler[..handler.IndexOf("\n    }")].Contains("TurnProtectionOn(ask: true)"), "");
    }

    // C3: Regions diz que o bloqueio fica com a app fechada (21 dos 46 de um dia saíram com regiões bloqueadas)
    static void RegrasFicam(Action<string, bool, string> check)
    {
        var cs2 = MainWindow.RuleOnText(null, false);
        check("Usabilidade C3: a linha do CS2 diz que fica e como se tira", cs2.StartsWith("Block is on. Only CS2 is affected.") && cs2.EndsWith(Texts.RegionsStaysOn), cs2);
        var dota = MainWindow.RuleOnText("Dota 2", true);
        check("Usabilidade C3: por jogo, com o aviso de poucas regiões", dota.Contains("Only Dota 2 is affected") && dota.Contains("Few regions left") && dota.EndsWith(Texts.RegionsStaysOn), dota);
        check("Usabilidade C3: aponta para o botão que existe", Texts.RegionsStaysOn.Contains("Allow all (block off)") && Src("MainWindow.xaml").Contains("Content=\"Allow all (block off)\""), "");
    }

    // C4: na última paragem o "5 de 5" e os botões cabem em ru, es e pt-BR
    static void Visita(Action<string, bool, string> check)
    {
        var xaml = Src("MainWindow.xaml");
        var box = xaml[xaml.IndexOf("x:Name=\"TourBox\"")..xaml.IndexOf("</HeaderedContentControl>", xaml.IndexOf("x:Name=\"TourBox\""))];
        check("Usabilidade C4: a caixa continua com 360 px", box.Contains("Width=\"360\""), "");
        check("Usabilidade C4: o \"5 de 5\" está no título", Regex.IsMatch(box, @"<HeaderedContentControl.Header>\s*<TextBlock[^>]*><Run x:Name=""TourTitle""/>.*<Run x:Name=""TourCount"""), "");
        check("Usabilidade C4: os botões passam para baixo quando não cabem", Regex.IsMatch(box, @"<WrapPanel x:Name=""TourButtons""[^>]*>\s*<Button x:Name=""TourSkipBtn""") && !box.Contains("Orientation=\"Horizontal\""), "");
    }

    static void Textos(Action<string, bool, string> check)
    {
        var texts = Src("Texts.cs");
        check("Usabilidade textos: sem travessões", !texts.Contains('—') && !texts.Contains('–'), "");
        var xaml = Src("MainWindow.xaml");
        foreach (var t in new[] { Texts.DoorsTitle, Texts.DoorRegions, Texts.DoorRegionsText, Texts.DoorPhone, Texts.DoorPhoneText, Texts.DoorTour })
            check($"Usabilidade textos: \"{t[..Math.Min(20, t.Length)]}\" vem de Texts, não do XAML", !xaml.Contains($"\"{t}\""), "");
    }
}
