using System.IO;
using SRT;
using SRT.Core;

// 9 nov.: o teste com o iPhone do dono por cabo (beta.13). Cada correção leva aqui o seu teste, que falhava antes.
// Notas da ronda: publish/RONDA-3-com-telemovel.md, "Anotações da ronda".
static class IphoneNov9Tests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        DriverDaApple(check);
        ChangeIpEspera(check);
        LogSemRuido(check);
        await RotaJaSaiu(check);
        PreviousIps(check);
        AvisosNaQueda(check);
        SoOIphone(check);
        CopiasNumaLinha(check);
    }

    // Decisão de 9 nov. (o dono deixou-a comigo): as linhas "copied" de Configs, uma por conta, ficam numa só
    static void CopiasNumaLinha(Action<string, bool, string> check)
    {
        var many = Enumerable.Range(1, 7).Select(i => ("crosshair", " from Acc1", "Acc" + (i + 1))).ToList();
        var lines = MainWindow.CopiedLines("Sync now", many);
        check("Configs 9 nov.: sete cópias iguais dão uma linha, com 5 nomes e o resto contado",
            lines.Count == 1 && lines[0] == "Config (Sync now): copied crosshair from Acc1 to 7 accounts (Acc2, Acc3, Acc4, Acc5, Acc6 and 2 more). Backups saved.", lines.FirstOrDefault());
        var one = MainWindow.CopiedLines("Sync now", new() { ("crosshair", " from Acc1", "Acc2") });
        check("Configs 9 nov.: uma cópia diz a conta, como antes", one.Count == 1 && one[0] == "Config (Sync now): copied crosshair from Acc1 to Acc2. Backup saved.", one.FirstOrDefault());
        var two = MainWindow.CopiedLines("Sync now", new() { ("crosshair", "", "A"), ("video", "", "B") });
        check("Configs 9 nov.: cópias diferentes ficam em linhas diferentes; nenhuma cópia, nenhuma linha",
            two.Count == 2 && MainWindow.CopiedLines("Sync now", new()).Count == 0, string.Join(" | ", two));
    }

    // 14:08: o Wi-Fi do PC desligado, só o iPhone; a app acabada de abrir mediu o "IP de casa" pelo iPhone e disse
    // "The phone's IP (...) is the same as your home IP: it is sharing your Wi-Fi"
    static void SoOIphone(Action<string, bool, string> check)
    {
        var iphone = new Adapter { Index = 68, IsPhone = true, Up = true, LocalIp = "172.20.10.3" };
        var wifi = new Adapter { Index = 18, IsWifi = true, Up = true, LocalIp = "192.168.87.14" };
        check("iPhone 9 nov.: o IP de casa nunca se mede pela placa de um telemóvel (nem antes de a app saber qual é)",
            MainWindow.HomeIf(new GuardReport { Adapters = new() { iphone }, PcDefaultIndex = 68 }, null) == -1
            && MainWindow.HomeIf(new GuardReport { Adapters = new() { iphone, wifi }, PcDefaultIndex = 18 }, null) == 18, "");
        check("iPhone 9 nov.: sem rede de casa não há 'mesmo IP que o de casa'",
            !MainWindow.SameAsHome("92.250.100.160", "92.250.100.160", hasHome: false)
            && MainWindow.SameAsHome("92.250.100.160", "92.250.100.160", hasHome: true) && MainWindow.SameAsHome("1.1.1.1", "1.1.1.1", null)
            && !MainWindow.SameAsHome("1.1.1.1", "2.2.2.2", true) && !MainWindow.SameAsHome(null, "2.2.2.2", true), "");
    }

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    // O PC só tinha o appleusb.inf: o iPhone aparecia no USB, sem placa de rede, e o setup parava em "Trust this computer"
    static void DriverDaApple(Action<string, bool, string> check)
    {
        var iph = new PhoneUsbState(true, null, "Apple");
        var sem = new PhoneSetupState { Iphone = true, Usb = iph, AppleDriver = false };
        var steps = PhoneSetup.Steps(sem);
        var now = PhoneSetup.Current(steps);
        check("iPhone 9 nov.: sem o driver de rede da Apple o passo é o driver, não o Trust",
            now?.Step == PhoneStep.UsbDriverError && PhoneSetup.Next(sem) == PhoneStep.UsbDriverError, now?.Title);
        check("iPhone 9 nov.: o passo do driver tem o botão Install", now?.Action == "Install" && now.Detail.Contains("Apple Devices"), now?.Detail);
        check("iPhone 9 nov.: sem driver, o Trust e o Personal Hotspot esperam por ele",
            steps.First(i => i.Step == PhoneStep.Unauthorized).State == PhoneSetup.StepState.Waiting
            && steps.First(i => i.Step == PhoneStep.TetherOff).State == PhoneSetup.StepState.Waiting, "");
        var com = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = iph, AppleDriver = true });
        check("iPhone 9 nov.: com o driver, o driver fica verde e o passo é o Trust",
            com.First(i => i.Step == PhoneStep.UsbDriverError).State == PhoneSetup.StepState.Ok && PhoneSetup.Current(com)?.Step == PhoneStep.Unauthorized, "");
        check("iPhone 9 nov.: ligado pelo cabo, tudo verde mesmo sem ler o driver",
            PhoneSetup.Current(PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = iph, Tethered = true, AppleDriver = false })) == null, "");
        check("iPhone 9 nov.: um Android nunca recebe o botão da Apple",
            PhoneSetup.Steps(new PhoneSetupState { Usb = new PhoneUsbState(true, 28, "Samsung"), AdbTool = true, AdbState = "none", AppleDriver = false })
                .All(i => i.Action != "Install" || i.Step == PhoneStep.NoAdbTool), "");
        check("iPhone 9 nov.: a Store abre o Apple Devices (9NP83LWLPZ9K) na janela pequena",
            PhoneSetup.AppleDevicesStoreUri == "ms-windows-store://pdp/?ProductId=9NP83LWLPZ9K&mode=mini", PhoneSetup.AppleDevicesStoreUri);
        var phone = Src("Views", "MainWindow.Phone.cs");
        check("iPhone 9 nov.: a app não corre o winget como admin (abre a Store, que instala como o utilizador)",
            !phone.Contains("winget") && !Src("Core", "Phone", "PhoneSetup.cs").Contains("winget install")
            && phone.Contains("Process.Start(new ProcessStartInfo(PhoneSetup.AppleDevicesStoreUri) { UseShellExecute = true });"), "");
        check("iPhone 9 nov.: o setup lê o driver da Apple", phone.Contains("AppleDriver = !iphone || phone != null || await Task.Run(PhoneUsb.AppleNetDriver)"), "");
        check("iPhone 9 nov.: este PC responde se tem o driver (sem rebentar)", PhoneUsb.AppleNetDriver() || !PhoneUsb.AppleNetDriver(), "");
    }

    // O Change IP desistia aos 90 s; o hotspot por USB do iPhone não voltou sozinho depois do modo de avião
    static void ChangeIpEspera(Action<string, bool, string> check)
    {
        check("iPhone 9 nov.: antes da queda espera 90 s, depois dela 5 minutos",
            MainWindow.IphoneLimit(false) == TimeSpan.FromSeconds(90) && MainWindow.IphoneLimit(true) == TimeSpan.FromMinutes(5), "");
        check("iPhone 9 nov.: aos 2 minutos sem voltar ainda espera",
            MainWindow.IphoneChangeStep(true, false, "1.1.1.1", null, TimeSpan.FromMinutes(2), MainWindow.IphoneLimit(true)) == MainWindow.IphoneChange.Off, "");
        check("iPhone 9 nov.: volta aos 4 minutos com IP novo e conta como troca",
            MainWindow.IphoneChangeStep(true, true, "1.1.1.1", "2.2.2.2", TimeSpan.FromMinutes(4), MainWindow.IphoneLimit(true)) == MainWindow.IphoneChange.BackNew, "");
        check("iPhone 9 nov.: ao fim de 5 minutos sem voltar acaba",
            MainWindow.IphoneChangeStep(true, false, "1.1.1.1", null, TimeSpan.FromMinutes(5.1), MainWindow.IphoneLimit(true)) == MainWindow.IphoneChange.TimedOut, "");
        // 3.ª volta (13:41): ligar e desligar o modo de avião várias vezes não o trouxe; o pnputil /restart-device trouxe-o logo.
        // A app reconecta sozinha e só depois das suas tentativas pede a mão dele.
        check("iPhone 9 nov.: aos 75 s sem o iPhone diz como o trazer à mão; ligado ou antes disso, não",
            MainWindow.IphoneNeedsNudge(false, TimeSpan.FromSeconds(75)) && !MainWindow.IphoneNeedsNudge(false, TimeSpan.FromSeconds(60))
            && !MainWindow.IphoneNeedsNudge(true, TimeSpan.FromSeconds(90)), "");
        check("iPhone 9 nov.: reconecta aos 15 s, depois de 20 em 20 s, no máximo 8 vezes",
            !MainWindow.IphoneRestartDue(TimeSpan.FromSeconds(10), null, 0) && MainWindow.IphoneRestartDue(TimeSpan.FromSeconds(15), null, 0)
            && !MainWindow.IphoneRestartDue(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), 1) && MainWindow.IphoneRestartDue(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(20), 1)
            && !MainWindow.IphoneRestartDue(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1), MainWindow.IphoneRestartMax), "");
        var ids = new[]
        {
            new UsbDevice(@"USB\VID_05AC&PID_12A8&MI_02\7&1B6DAF1B&0&0002", "Apple Mobile Device Ethernet", null),
            new UsbDevice(@"USB\VID_05AC&PID_12A8\00008120001E48EE26D1A01E", "Apple Mobile Device USB Composite Device", null),
            new UsbDevice(@"USB\VID_05AC&PID_024F\ABCDEF012345", "Magic Keyboard", null),
        };
        check("iPhone 9 nov.: reinicia o dispositivo USB inteiro do iPhone, não uma parte nem um teclado da Apple",
            PhoneUsb.IphoneDeviceId(ids) == @"USB\VID_05AC&PID_12A8\00008120001E48EE26D1A01E"
            && PhoneUsb.IphoneDeviceId(new[] { ids[0], ids[2] }) == null, PhoneUsb.IphoneDeviceId(ids));
        check("iPhone 9 nov.: o pnputil só recebe um ID de iPhone (nada mais passa para a linha de comando)",
            PhoneUsb.RestartArgs(@"USB\VID_05AC&PID_12A8\00008120001E48EE26D1A01E") == "/restart-device \"USB\\VID_05AC&PID_12A8\\00008120001E48EE26D1A01E\""
            && PhoneUsb.RestartArgs("USB\\VID_05AC&PID_12A8\\0000\" & calc") == null && PhoneUsb.RestartArgs(@"USB\VID_04E8&PID_6860\R58N12345") == null
            && PhoneUsb.RestartArgs(null) == null, "");
        var isrc = Src("Views", "MainWindow.Iphone.cs");
        check("iPhone 9 nov.: o Change IP e a proteção reconectam o iPhone sozinhos",
            isrc.Contains("IphoneRestartDue(sw.Elapsed - dr, sw.Elapsed - restartedAt, restarts)") && isrc.Contains("await Proc.Run(\"pnputil\", args, 30000)")
            && Src("Views", "MainWindow.Protection.cs").Contains("IphoneWatch(rep, home);"), "");
        check("iPhone 9 nov.: o texto para o trazer fala do Personal Hotspot e do cabo",
            PhoneSetup.IphoneBringBack.Contains("Personal Hotspot") && PhoneSetup.IphoneBringBack.Contains("cable"), "");
        var src = Src("Views", "MainWindow.Iphone.cs");
        // 4.ª volta (14:13): o dono achou confuso carregar em "I'm doing it"; o Change IP começa logo a esperar
        check("iPhone 9 nov.: o Change IP do iPhone começa logo a esperar, sem a pergunta 'I'm doing it'",
            !src.Contains("\"I'm doing it\"") && !src.Contains("await Ask(\"Change IP on the iPhone\"")
            && src.IndexOf("_ipChangeQuiet = true;") > 0 && src.IndexOf("_ipChangeQuiet = true;") < src.IndexOf("Log.Info(\"iPhone: Change IP started.")
            && src.Contains("\"on the iPhone: Airplane Mode on, wait 3 s, then off…\""), "");
        check("iPhone 9 nov.: o Log diz quando o Change IP do iPhone começa e quando é cancelado",
            src.Contains("Log.Info(\"iPhone: Change IP started.") && src.Contains("Log.Info(\"iPhone: Change IP cancelled.\")"), "");
        check("iPhone 9 nov.: o Change IP já não promete 'up to 90 s'", !src.Contains("up to 90 s"), "");
    }

    // O Log com o iPhone a voltar: 6 linhas do kill switch em 16 s e 3 "hasn't been checked yet" em 12 s
    static void LogSemRuido(Action<string, bool, string> check)
    {
        string last = null;
        const string none = "Kill switch: servers in the list and CS2 blocked (no connection).";
        const string trust = "Kill switch: servers in the list and CS2 blocked (the phone isn't trusted yet).";
        const string open = "Kill switch: servers in the list and CS2 only go out through \"Ethernet 6\" (172.20.10.3).";
        var lines = new[] { none, trust, none, trust, none, trust, open, none }.Select(t => Guard.KillLine(t, ref last, false)).ToList();
        check("iPhone 9 nov.: bloqueado, a razão a ir e vir dá uma linha só",
            lines[0] == none && lines.Skip(1).Take(5).All(l => l == null), string.Join(" | ", lines));
        check("iPhone 9 nov.: abrir e voltar a bloquear diz outra vez", lines[6] == open && lines[7] == none, "");
        string l2 = none;
        check("iPhone 9 nov.: regras refeitas dizem sempre", Guard.KillLine(trust, ref l2, repaired: true) == trust, "");

        var p = new ProblemLog();
        var t0 = new DateTime(2026, 11, 9, 11, 3, 8);
        var u = DistrustText.Of(Distrust.Unchecked);
        var a = p.Next(false, u, Distrust.Unchecked, t0);
        p.Next(false, null, Distrust.None, t0.AddSeconds(2));
        var b = p.Next(false, u, Distrust.Unchecked, t0.AddSeconds(6));
        p.Next(false, null, Distrust.None, t0.AddSeconds(8));
        var c = p.Next(false, u, Distrust.Unchecked, t0.AddSeconds(12));
        p.Next(false, null, Distrust.None, t0.AddSeconds(20));
        var d = p.Next(false, u, Distrust.Unchecked, t0.AddMinutes(2));
        check("iPhone 9 nov.: 'hasn't been checked yet' uma vez por minuto", a != null && b == null && c == null && d != null, $"{a} | {b} | {c} | {d}");
        var q = new ProblemLog();
        q.Next(false, u, Distrust.Unchecked, t0);
        q.Next(true, null, Distrust.None, t0.AddSeconds(1));
        check("iPhone 9 nov.: desligar e ligar a proteção diz outra vez", q.Next(false, u, Distrust.Unchecked, t0.AddSeconds(2)) != null, "");

        var guard = Src("Core", "Network", "Guard.cs");
        check("iPhone 9 nov.: o Log não fala em 'adapter 68'", !guard.Contains("The phone's adapter {_metricAppliedTo} is gone")
            && guard.Contains("The phone's connection went away (cable, Personal Hotspot or Airplane Mode)."), "");
    }

    // "Couldn't delete the route 205.196.6.0/24 on adapter 68: Element not found." três vezes quando o iPhone caiu
    static async Task RotaJaSaiu(Action<string, bool, string> check)
    {
        check("iPhone 9 nov.: 'Element not found' (e o português, e o código) é rota que já saiu",
            Guard.RouteAlreadyGone("Element not found.") && Guard.RouteAlreadyGone("Elemento não encontrado.") && Guard.RouteAlreadyGone("HRESULT 0x80070490")
            && !Guard.RouteAlreadyGone("Access is denied.") && !Guard.RouteAlreadyGone(null), "");

        const string key = "205.196.6.1/32|68|172.20.10.1";
        var dir = Path.Combine(Path.GetTempPath(), "srt-iphone9-" + Guid.NewGuid().ToString("N"));
        Untangle12Tests.OwnedDir(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { Routes = new() { key }, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        var table = new List<Net.Route> { new("205.196.6.1/32", 68, "172.20.10.1") };
        var guard = new Guard(file, (a, ms) => Task.FromResult(new ProcResult(0, "")), new Guard.RouteTable(
            () => Task.FromResult(table.ToList()),
            rt => { table.Remove(rt); return Task.FromResult(new ProcResult(1, "Element not found.")); }));
        var warned = 0;
        void Seen(LogEntry e) { if (e.Text.StartsWith("Couldn't delete the route 205.196.6.1/32")) warned++; }
        Log.Added += Seen;
        try
        {
            await guard.RemoveIps(new[] { "205.196.6.1" }, new Settings());
            check("iPhone 9 nov.: a rota que já saiu não vai ao Log e deixa de ser da app",
                warned == 0 && !guard.Owned().Routes.Contains(key), $"{warned} linhas");
        }
        finally { Log.Added -= Seen; }
    }

    // 2.ª volta (11:20): no segundo do modo de avião, "Another VPN may be overriding it" e cinco "Route ...: Element not found /
    // The object already exists" ao pôr as rotas numa placa que estava a sair
    static void AvisosNaQueda(Action<string, bool, string> check)
    {
        check("iPhone 9 nov.: 'already exists' (e o português, e o código) é rota que já lá está",
            Guard.RouteAlreadyThere("The object already exists.") && Guard.RouteAlreadyThere("O objeto já existe.") && Guard.RouteAlreadyThere("0x80071392")
            && !Guard.RouteAlreadyThere("Element not found.") && !Guard.RouteAlreadyThere(null), "");
        var phone = new Adapter { Index = 68, Up = true, LocalIp = "172.20.10.3" };
        check("iPhone 9 nov.: a placa ainda lá, com o mesmo IP, conta como ligada",
            Guard.StillUp(phone, new[] { phone }) && !Guard.StillUp(phone, new[] { new Adapter { Index = 68, Up = false, LocalIp = "172.20.10.3" } })
            && !Guard.StillUp(phone, new[] { new Adapter { Index = 68, Up = true, LocalIp = "169.254.152.150" } }) && !Guard.StillUp(phone, Array.Empty<Adapter>())
            && !Guard.StillUp(null, new[] { phone }), "");
        var guard = Src("Core", "Network", "Guard.cs");
        check("iPhone 9 nov.: o aviso da VPN e o das rotas só com a placa ainda ligada",
            guard.Contains("if (!routeOk[ip] && StillUp(egress) && _warned.Add(ip))")
            && guard.Contains("else if (!RouteAlreadyGone(r.Output) && StillUp(via) && _warned.Add(\"add \" + p))")
            && guard.Contains("else if (RouteAlreadyThere(r.Output)) Own(RouteKey.Of(p, via));"), "");
    }

    // O IP novo do iPhone ficou "at startup" em Previous IPs, quando a app já estava aberta há 10 minutos
    static void PreviousIps(Action<string, bool, string> check)
    {
        check("iPhone 9 nov.: um IP medido fora de um Change IP diz 'detected', não 'at startup'",
            new IpHistoryEntry { Reason = "detetado" }.ReasonText == "detected" && new IpHistoryEntry { Reason = "trocado" }.ReasonText == "changed", "");
    }
}
