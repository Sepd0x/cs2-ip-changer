using System.IO;
using System.Text;
using SRT.Core;

// beta.11: o teste do Rafael a 4 nov. (Turn on sem tethering, auto-join sozinho, perguntas sem saída, o IP em "—") e as
// opções de arranque do CS2 (-condebug -conclearlog) postas pela app
static class Beta11Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray()));

        // ---------------- a) Turn on protection com o telemóvel por ligar
        check("Turn on: ligado não faz mais nada", SRT.MainWindow.NextAfterTurnOn(true, false, true, false) == SRT.MainWindow.TurnOnStep.None, "");
        check("Turn on: telemóvel no ADB sem tethering, a app liga o tethering", SRT.MainWindow.NextAfterTurnOn(false, false, true, false) == SRT.MainWindow.TurnOnStep.Tether, "");
        check("Turn on: com o Wi-Fi do telemóvel ligado, desliga-o primeiro", SRT.MainWindow.NextAfterTurnOn(false, false, true, true) == SRT.MainWindow.TurnOnStep.WifiThenTether, "");
        check("Turn on: sem ADB (ou sem autorizar), abre o Phone setup", SRT.MainWindow.NextAfterTurnOn(false, false, false, false) == SRT.MainWindow.TurnOnStep.Setup, "");
        check("Turn on: hotspot guardado, abre o Phone setup (não há tethering a ligar)", SRT.MainWindow.NextAfterTurnOn(false, true, true, false) == SRT.MainWindow.TurnOnStep.Setup, "");
        var prot = Src("Views", "MainWindow.Protection.cs");
        check("Turn on: depois de ligar, liga o telemóvel ou diz o que falta", prot.Contains("await ConnectPhoneAfterTurnOn(openSetup: ask);"), "");

        // ---------------- b) Auto-join nunca começa sozinho
        check("Auto-join: a 1.ª vez que o jogador aparece não é mudança", !SRT.MainWindow.FollowAutoJoins(true, moved: false, cs2Running: true), "");
        check("Auto-join: com o CS2 fechado não abre o CS2", !SRT.MainWindow.FollowAutoJoins(true, moved: true, cs2Running: false), "");
        check("Auto-join: desligado não faz nada", !SRT.MainWindow.FollowAutoJoins(false, true, true), "");
        check("Auto-join: mudou de servidor com o CS2 aberto, pergunta", SRT.MainWindow.FollowAutoJoins(true, true, true), "");
        var players = Src("Views", "MainWindow.Players.cs");
        check("Auto-join: uma verificação de cada vez (antes entravam duas)", players.Contains("if (_checkingFollowed) return;"), "");
        check("Auto-join: pergunta sempre antes de entrar", players.Contains("if (!_demo && !await Ask(\"Join the player you follow?\""), "");

        // ---------------- c) Toda a pergunta tem saída
        var xaml = Src("MainWindow.xaml");
        check("Pergunta: não tapa a barra de cima (o Cancel)", xaml.Contains("<Grid x:Name=\"Dialog\" Grid.Row=\"1\" Margin=\"0,44,0,0\""), "");
        check("Pergunta: tem um X que fecha sem escolher", xaml.Contains("x:Name=\"DialogClose\"") && Src("Views", "MainWindow.Feedback.cs").Contains("void DialogClose_Click"), "");
        check("Pergunta: um clique fora da caixa também fecha", xaml.Contains("MouseLeftButtonDown=\"DialogBackdrop_MouseDown\""), "");
        check("Servidor cheio: fechar sem escolher não espera nem entra", Src("Views", "MainWindow.Servers.cs").Contains("if (!_dialogCancelClicked) return false;"), "");

        // ---------------- d) O IP que os servidores veem
        var (ip1, f1) = SRT.MainWindow.EgressAfterCheck("92.250.101.129", null, null, "8|192.168.58.226");
        check("IP: uma medição boa mostra o IP", ip1 == "92.250.101.129" && !f1, ip1);
        var (ip2, f2) = SRT.MainWindow.EgressAfterCheck(null, "92.250.101.129", "8|192.168.58.226", "8|192.168.58.226");
        check("IP: uma medição falhada na mesma ligação guarda o último (esbatido), não volta a \"—\"", ip2 == "92.250.101.129" && f2, ip2);
        var (ip3, _) = SRT.MainWindow.EgressAfterCheck(null, "92.250.101.129", "8|192.168.58.226", "9|192.168.42.10");
        check("IP: outra ligação limpa o IP antigo", ip3 == null, ip3);
        var (ip4, _) = SRT.MainWindow.EgressAfterCheck(null, "92.250.101.129", "8|192.168.58.226", null);
        check("IP: sem telemóvel limpa", ip4 == null, ip4);
        check("IP: o tick espera pela medição anterior (antes a nova passava à frente e nenhuma chegava)",
            Src("MainWindow.xaml.cs").Contains("_ipRunning == 0 && (DateTime.Now - _lastIpCheck)"), "");

        // ---------------- 3. -condebug -conclearlog nas opções de arranque
        check("Opções: vazio fica com as duas", Cs2LaunchOptions.Add("") == "-condebug -conclearlog" && Cs2LaunchOptions.Add(null) == "-condebug -conclearlog", Cs2LaunchOptions.Add(""));
        check("Opções: o que lá está fica, as que faltam vão para o fim", Cs2LaunchOptions.Add("-novid +fps_max 400") == "-novid +fps_max 400 -condebug -conclearlog", Cs2LaunchOptions.Add("-novid +fps_max 400"));
        check("Opções: só a que falta", Cs2LaunchOptions.Add("-condebug -high") == "-condebug -high -conclearlog", Cs2LaunchOptions.Add("-condebug -high"));
        check("Opções: já lá estão (outra caixa), nada muda", Cs2LaunchOptions.Add("-CONDEBUG -conclearlog") == "-CONDEBUG -conclearlog" && Cs2LaunchOptions.Has("-CONDEBUG -conclearlog"), "");
        check("Opções: -condebugx não conta como -condebug", !Cs2LaunchOptions.Has("-condebugx -conclearlog"), "");

        static string Vdf(string apps) =>
            "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"Other\"\t\t\"x\"\n" + apps + "\t\t\t}\n\t\t}\n\t}\n\t\"friends\"\n\t{\n\t\t\"a\"\t\t\"1\"\n\t}\n}\n";
        var withOpts = Vdf("\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-novid \\\"+exec x.cfg\\\"\"\n\t\t\t\t\t\t\"Playtime\"\t\t\"10\"\n\t\t\t\t\t}\n\t\t\t\t\t\"440\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-tf\"\n\t\t\t\t\t}\n\t\t\t\t}\n");
        var p1 = Cs2LaunchOptions.Patch(withOpts);
        check("localconfig: acrescenta ao 730, aspas escapadas ficam", p1.Changed && p1.After == "-novid \"+exec x.cfg\" -condebug -conclearlog", p1.After ?? p1.Why);
        var back = VdfNode.Parse(p1.Text ?? "");
        var steamNode = back.Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam");
        check("localconfig: os outros jogos e o resto do ficheiro ficam iguais",
            steamNode?.Child("apps")?.Child("440")?.Get("LaunchOptions") == "-tf" && steamNode?.Child("apps")?.Child("730")?.Get("Playtime") == "10"
            && steamNode?.Get("Other") == "x" && back.Child("UserLocalConfigStore")?.Child("friends")?.Get("a") == "1", "");
        check("localconfig: escrito volta a ler-se igual (round trip)", VdfNode.RoundTrips(p1.Text ?? ""), "");
        var p2 = Cs2LaunchOptions.Patch(p1.Text);
        check("localconfig: a 2.ª vez não muda nada", !p2.Changed && p2.Why == "already there", p2.Why);
        var p3 = Cs2LaunchOptions.Patch(Vdf(""));
        check("localconfig: sem apps nem 730, cria-os como a Steam", p3.Changed && VdfNode.Parse(p3.Text).Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps")?.Child("730")?.Get("LaunchOptions") == "-condebug -conclearlog", p3.Why);
        check("localconfig: um ficheiro com palavras sem aspas fica como está", !Cs2LaunchOptions.Patch("\"UserLocalConfigStore\" { #include x }").Changed, "");
        check("localconfig: outro ficheiro qualquer fica como está", !Cs2LaunchOptions.Patch("\"users\"\n{\n}\n").Changed && !Cs2LaunchOptions.Patch("").Changed, "");

        // Numa userdata a fingir: escreve, faz backup, a Steam aberta trava a escrita
        var root = Path.Combine(Path.GetTempPath(), "srt-launchopts-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var ud = Path.Combine(root, "userdata");
            foreach (var id in new[] { "111", "222" }) Directory.CreateDirectory(Path.Combine(ud, id, "config"));
            Directory.CreateDirectory(Path.Combine(ud, "0", "config"));
            File.WriteAllText(Path.Combine(ud, "111", "config", "localconfig.vdf"), withOpts, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(ud, "222", "config", "localconfig.vdf"), p1.Text, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(ud, "0", "config", "localconfig.vdf"), withOpts, new UTF8Encoding(false));
            var bk = Path.Combine(root, "backups");
            var blocked = Cs2LaunchOptions.ApplyAll(ud, bk, () => true);
            check("userdata: com a Steam aberta nada se escreve", blocked.All(r => !r.Changed) && File.ReadAllText(Path.Combine(ud, "111", "config", "localconfig.vdf")) == withOpts, "");
            var r = Cs2LaunchOptions.ApplyAll(ud, bk, () => false);
            check("userdata: só a conta que precisa muda; a pasta 0 não é conta",
                r.Count == 2 && r.Single(x => x.Id32 == "111").Changed && !r.Single(x => x.Id32 == "222").Changed, string.Join(",", r.Select(x => x.Id32 + ":" + x.Changed)));
            check("userdata: backup do ficheiro antes de escrever", Directory.Exists(bk) && Directory.GetFiles(bk).Length == 1 && File.ReadAllText(Directory.GetFiles(bk)[0]) == withOpts, "");
            check("userdata: a conta 111 ficou com as opções", Cs2LaunchOptions.Count(ud) is (2, 2), Cs2LaunchOptions.Count(ud)?.ToString());
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        // ---------------- iPhone: o Phone setup e o Change IP próprios
        var iph = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_05AC&PID_12A8\abc", "Apple iPhone", null) });
        check("iPhone: o Windows a ver um iPhone dá Apple", PhoneUsb.IsIphone(iph) && iph.Seen, iph.Vendor);
        check("iPhone: um teclado da Apple não é telemóvel", !PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_05AC&PID_0267\x", "Magic Keyboard", null) }).Seen, "");
        check("iPhone: sem o driver da Apple (código 28) conta como iPhone com problema",
            PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_05AC&PID_12A8\abc", "Apple Mobile Device Ethernet", 28) }) is { Seen: true, Problem: 28, Vendor: "Apple" }, "");
        check("iPhone: a placa \"Apple Mobile Device Ethernet\" é de iPhone", PhoneUsb.IsIphone(new Adapter { Description = "Apple Mobile Device Ethernet #2" }) && !PhoneUsb.IsIphone(new Adapter { Description = "Remote NDIS based Internet Sharing Device" }), "");
        var ipNone = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, AdbTool = false, AdbState = "none" });
        check("iPhone: nenhum passo fala de ADB nem de depuração USB", !ipNone.Any(i => i.Title.Contains("ADB") || i.Title.Contains("debugging") || i.Detail.Contains("USB debugging")), string.Join(", ", ipNone.Select(i => i.Title)));
        check("iPhone: sem cabo, o passo é o cabo", PhoneSetup.Current(ipNone)?.Step == PhoneStep.NoUsb && PhoneSetup.Next(new PhoneSetupState { Iphone = true }) == PhoneStep.NoUsb, PhoneSetup.Current(ipNone)?.Title);
        var ipPlugged = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = iph });
        check("iPhone: com cabo, Confiar e depois o Personal Hotspot", PhoneSetup.Current(ipPlugged)?.Title == "Trust this computer" && ipPlugged.Any(i => i.Title == "Personal Hotspot" && i.State == PhoneSetup.StepState.Todo), "");
        check("iPhone: com cabo, o próximo passo geral é o hotspot", PhoneSetup.Next(new PhoneSetupState { Iphone = true, Usb = iph }) == PhoneStep.TetherOff, "");
        var ipOn = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = iph, Tethered = true });
        check("iPhone: ligado, tudo verde e o Change IP explicado", PhoneSetup.Current(ipOn) == null && ipOn.Last().Detail.Contains("Airplane Mode") && PhoneSetup.Next(new PhoneSetupState { Iphone = true, Tethered = true }) == PhoneStep.Done, "");
        check("iPhone: o Android continua com os passos de sempre", PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new PhoneUsbState(true, null, "Google") }).Any(i => i.Title == "USB debugging"), "");
        var lim = TimeSpan.FromSeconds(90);
        check("Change IP no iPhone: ainda ligado, espera pelo modo de avião", SRT.MainWindow.IphoneChangeStep(false, true, "1.1.1.1", null, TimeSpan.FromSeconds(5), lim) == SRT.MainWindow.IphoneChange.WaitingOff, "");
        check("Change IP no iPhone: a ligação caiu, pede para desligar o modo de avião", SRT.MainWindow.IphoneChangeStep(true, false, "1.1.1.1", null, TimeSpan.FromSeconds(9), lim) == SRT.MainWindow.IphoneChange.Off, "");
        check("Change IP no iPhone: voltou, a medir", SRT.MainWindow.IphoneChangeStep(true, true, "1.1.1.1", null, TimeSpan.FromSeconds(20), lim) == SRT.MainWindow.IphoneChange.Measuring, "");
        check("Change IP no iPhone: voltou com IP novo", SRT.MainWindow.IphoneChangeStep(true, true, "1.1.1.1", "2.2.2.2", TimeSpan.FromSeconds(25), lim) == SRT.MainWindow.IphoneChange.BackNew, "");
        check("Change IP no iPhone: voltou com o mesmo IP", SRT.MainWindow.IphoneChangeStep(true, true, "1.1.1.1", "1.1.1.1", TimeSpan.FromSeconds(25), lim) == SRT.MainWindow.IphoneChange.BackSame, "");
        check("Change IP no iPhone: 90 s sem voltar acaba", SRT.MainWindow.IphoneChangeStep(true, false, "1.1.1.1", null, TimeSpan.FromSeconds(91), lim) == SRT.MainWindow.IphoneChange.TimedOut, "");
        check("Change IP no iPhone: um IP novo que chega tarde ainda conta", SRT.MainWindow.IphoneChangeStep(true, true, "1.1.1.1", "2.2.2.2", TimeSpan.FromSeconds(95), lim) == SRT.MainWindow.IphoneChange.BackNew, "");

        var steamSrc = Src("Core", "Steam", "SteamAccounts.cs");
        check("Opções: a app trata delas sempre que ela própria fecha a Steam", steamSrc.Contains("if (SteamClosedNow() && WhileClosed != null)"), "");
        check("Opções: ligado por defeito", new SRT.Settings().Cs2LaunchOptions, "");
    }
}
