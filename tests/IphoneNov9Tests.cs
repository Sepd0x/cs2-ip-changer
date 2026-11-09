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
        check("iPhone 9 nov.: aos 25 s sem o iPhone diz como o trazer; ligado ou antes disso, não",
            MainWindow.IphoneNeedsNudge(false, TimeSpan.FromSeconds(25)) && !MainWindow.IphoneNeedsNudge(false, TimeSpan.FromSeconds(20))
            && !MainWindow.IphoneNeedsNudge(true, TimeSpan.FromSeconds(60)), "");
        check("iPhone 9 nov.: o texto para o trazer fala do Personal Hotspot e do cabo",
            PhoneSetup.IphoneBringBack.Contains("Personal Hotspot") && PhoneSetup.IphoneBringBack.Contains("cable"), "");
        var src = Src("Views", "MainWindow.Iphone.cs");
        check("iPhone 9 nov.: o silêncio da troca começa antes da pergunta (o modo de avião ligado a ler não dá 'dropped')",
            src.IndexOf("_ipChangeQuiet = true;") > 0 && src.IndexOf("_ipChangeQuiet = true;") < src.IndexOf("await Ask(\"Change IP on the iPhone\""), "");
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
