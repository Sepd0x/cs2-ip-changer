using System.IO;
using SRT;
using SRT.Core;

// Beta.12 (6 nov.): o refactor do untangle-codebase (publish/untangle-2026-11-06/registo.md). Testes de comportamento: cada um
// falhava antes da sua correção.
static class Untangle12Tests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        await MetricBack(check);
        IphoneOnWifi(check);
        StoppedClock(check);
        RouteKeys(check);
        OursOnTheExit(check);
        await RouteNotDeleted(check);
    }

    // B1: a chave das rotas da app num só tipo, no formato que o owned.json sempre teve
    static void RouteKeys(Action<string, bool, string> check)
    {
        var key = RouteKey.Of(new Net.Route("203.0.113.9/32", 17, "192.0.2.1"));
        check("B1: a chave de uma rota escreve-se como no owned.json", key.ToString() == "203.0.113.9/32|17|192.0.2.1", key.ToString());
        check("B1: uma linha de um owned.json antigo volta à mesma chave", RouteKey.TryParse("203.0.113.9/32|17|192.0.2.1", out var back) && back == key, back.ToString());
        check("B1: sem três partes ou com um índice que não é número, não é chave",
            !RouteKey.TryParse("203.0.113.9/32|17", out _) && !RouteKey.TryParse("203.0.113.9/32|-3|192.0.2.1", out _) && !RouteKey.TryParse(null, out _), "");
        var usb = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var onLink = new Adapter { Index = 21, Up = true, LocalIp = "10.0.0.2" };
        check("B1: a rota que a app junta tem o gateway do adaptador, ou 0.0.0.0 sem gateway",
            RouteKey.Of("1.0.0.1/32", usb).ToString() == "1.0.0.1/32|17|192.168.42.129" && RouteKey.Of("1.0.0.1/32", onLink).Hop == "0.0.0.0", "");
        check("B1: o owned.json continua a aceitar as mesmas linhas",
            Guard.Trusted(new Guard.OwnedState { Routes = new() { key.ToString(), "0.0.0.0/1|17|192.0.2.1", "10.0.0.0/8|17|192.0.2.1" } }, out var dropped).Routes.Count == 2 && dropped == 1, "");
    }

    // B1: "esta rota é da app" também no hotspot ou numa rede USB (a ligação do telemóvel), mas nunca uma rota de outro programa
    static void OursOnTheExit(Action<string, bool, string> check)
    {
        var none = new HashSet<string>();
        var servers = new HashSet<string> { "203.0.113.9" };
        var wifi = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "172.20.10.2", Gateway = "172.20.10.1", Ssid = "Ana" };
        var usbPhone = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var adapters = new List<Adapter> { wifi, usbPhone };
        Net.Route On(string prefix, int index) => new(prefix, index, "172.20.10.1");

        check("B1: um servidor da lista no hotspot é da app quando o hotspot é a ligação do telemóvel",
            Guard.IsOurs(On("203.0.113.9/32", 12), none, adapters, 12, servers), "");
        check("B1: sem ligação conhecida (--restore), o hotspot fica como hoje", !Guard.IsOurs(On("203.0.113.9/32", 12), none, adapters, -1, servers), "");
        check("B1: as metades do PC e os blocos da Valve no hotspot também",
            Guard.IsOurs(On("0.0.0.0/1", 12), none, adapters, 12, none) && Guard.IsOurs(On(Net.ValvePrefixes[0], 12), none, adapters, 12, none), "");
        check("B1: um /32 que não é da lista no hotspot nunca é da app (o servidor de uma VPN)",
            !Guard.IsOurs(On("198.51.100.7/32", 12), none, adapters, 12, servers, anyHostOnPhone: true), "");
        check("B1: num adaptador de telemóvel, qualquer /32 conta quando a lista pode ter mudado",
            Guard.IsOurs(On("198.51.100.7/32", 17), none, adapters, 12, servers, anyHostOnPhone: true), "");
        check("B1: outras rotas (a rota por defeito, uma rede) nunca são da app",
            !Guard.IsOurs(On("0.0.0.0/0", 12), none, adapters, 12, servers) && !Guard.IsOurs(On("10.0.0.0/8", 17), none, adapters, 12, servers), "");
        check("B1: o que está no owned.json é da app em qualquer adaptador",
            Guard.IsOurs(On("203.0.113.9/32", 5), new HashSet<string> { "203.0.113.9/32|5|172.20.10.1" }, adapters, -1, none), "");

        check("B1: a ligação do telemóvel conta enquanto tem o mesmo IP local", Guard.ExitIndex(wifi, adapters) == 12, "");
        var moved = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "192.168.1.40", Gateway = "192.168.1.1" };
        check("B1: o Wi-Fi que saltou para o router de casa já não conta", Guard.ExitIndex(wifi, new List<Adapter> { moved }) == -1, "");
        var vpn = new Adapter { Index = 30, IsVpn = true, Up = true, LocalIp = "10.8.0.2" };
        check("B1: o adaptador de uma VPN escolhido à mão nunca conta", Guard.ExitIndex(vpn, new List<Adapter> { vpn }) == -1, "");
    }

    // B1: uma rota que o Windows não apaga continua a ser da app (no owned.json também) e vai ao Log uma vez, não a cada sync
    static async Task RouteNotDeleted(Action<string, bool, string> check)
    {
        const string key = "203.0.113.9/32|9999|192.0.2.1";
        var dir = Path.Combine(Path.GetTempPath(), "srt-untangle12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { Routes = new() { key }, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        var table = new List<Net.Route> { new("203.0.113.9/32", 9999, "192.0.2.1") };
        var deletes = 0;
        var refuse = true;
        var guard = new Guard(file, (a, ms) => Task.FromResult(new ProcResult(0, "")), new Guard.RouteTable(
            () => Task.FromResult(table.ToList()),
            rt => { deletes++; if (refuse) return Task.FromResult(new ProcResult(1, "Element not found.")); table.Remove(rt); return Task.FromResult(new ProcResult(0, "Ok.")); }));
        check("B1: o Guard lê a rota do owned.json", guard.Owned().Routes.Contains(key), "");

        var warned = 0;
        void Seen(LogEntry e) { if (e.Text.StartsWith("Couldn't delete the route 203.0.113.9/32")) warned++; }
        Log.Added += Seen;
        try
        {
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            check("B1: a rota que o Windows recusa apagar continua da app", deletes == 2 && guard.Owned().Routes.Contains(key)
                && Store.Load(file, () => new Guard.OwnedState()).Routes.Contains(key), $"{deletes} tentativas");
            check("B1: e a falha vai ao Log uma vez, não a cada tentativa", warned == 1, $"{warned} linhas");
            refuse = false;
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            check("B1: apagada, deixa de ser da app", table.Count == 0 && !guard.Owned().Routes.Contains(key)
                && !Store.Load(file, () => new Guard.OwnedState()).Routes.Contains(key), "");
        }
        finally { Log.Added -= Seen; }
    }

    // O relógio do demo parado: o que a app mostra e carimba lê o AppClock, por isso as capturas saem iguais em cada corrida
    static void StoppedClock(Action<string, bool, string> check)
    {
        var at = new DateTime(2026, 11, 6, 16, 44, 0, DateTimeKind.Local);
        LogEntry got = null;
        void Seen(LogEntry e) { if (e.Text == "untangle12 clock") got = e; }
        AppClock.StopAt(at);
        try
        {
            Log.Added += Seen;
            Log.Info("untangle12 clock");
            check("Relógio: parado, diz sempre a mesma hora", AppClock.Now == at && AppClock.Today == at.Date, "");
            check("Relógio: a linha do Log leva a hora do relógio da app", got?.Time == at, got?.Time.ToString("O") ?? "sem linha");
            check("Relógio: \"há 5 min\" conta a partir do relógio da app", Fmt.Ago(at.AddMinutes(-5)) == "5 min ago", Fmt.Ago(at.AddMinutes(-5)));
            check("Relógio: Previous IPs mostra a hora no dia do relógio", new IpHistoryEntry { Time = at.AddHours(-3) }.TimeText == "13:44"
                && new IpHistoryEntry { Time = at.AddDays(-1) }.TimeText == "5 Nov", "");
        }
        finally { Log.Added -= Seen; AppClock.Run(); }
        check("Relógio: volta a andar com o Windows", Math.Abs((AppClock.Now - DateTime.Now).TotalSeconds) < 5, "");
    }

    /// <summary>Um owned.json desta sessão do Windows (a métrica só vive até reiniciar).</summary>
    static string OwnedWithMetric(int ifIndex)
    {
        var dir = Path.Combine(Path.GetTempPath(), "srt-untangle12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { MetricIf = ifIndex, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        return file;
    }

    // A7: devolver o adaptador do telemóvel ao fechar com o PowerShell a falhar não pode esquecer que a métrica 900 ficou lá
    static async Task MetricBack(Action<string, bool, string> check)
    {
        var file = OwnedWithMetric(7);
        var failing = new Guard(file, (args, ms) => Task.FromResult(new ProcResult(1, "NetTCPIP\\Set-NetIPInterface : Access is denied.")));
        check("A7: o Guard lê a métrica aplicada do owned.json", failing.PcOffPhoneApplied, "");
        await failing.ReleasePcOffPhone();
        check("A7: com o PowerShell a falhar a métrica continua marcada", failing.PcOffPhoneApplied && failing.Owned().MetricIf == 7, "");
        check("A7: e o owned.json guarda-a para o próximo arranque ou o --restore",
            Store.Load(file, () => new Guard.OwnedState()).MetricIf == 7, "");

        string ran = null;
        var working = new Guard(file, (args, ms) => { ran = args; return Task.FromResult(new ProcResult(0, "")); });
        await working.ReleasePcOffPhone();
        check("A7: com o PowerShell a funcionar a métrica sai", !working.PcOffPhoneApplied && Store.Load(file, () => new Guard.OwnedState()).MetricIf == -1, "");
        check("A7: e o comando devolve a rota por defeito ao adaptador 7", ran?.Contains("-InterfaceIndex 7 ") == true && ran.Contains("-IgnoreDefaultRoutes Disabled"), ran ?? "");
    }

    // A8: um iPhone só por hotspot Wi-Fi, sem cabo, recebe os passos do iPhone e não os do Android
    static void IphoneOnWifi(Action<string, bool, string> check)
    {
        var none = PhoneUsbState.None;
        check("A8: um hotspot com nome de iPhone diz que é um iPhone", PhoneUsb.IphoneSeen(false, none, null, "Ana's iPhone") == true, "");
        check("A8: um Android pronto no ADB diz que não é", PhoneUsb.IphoneSeen(true, none, null, "Ana's iPhone") == false, "");
        check("A8: um hotspot Wi-Fi com outro nome e sem cabo não diz nada", PhoneUsb.IphoneSeen(false, none, null, "MEO-5G") == null, "");
        check("A8: sem nada visto, o iPhone guardado vale enquanto há hotspot", PhoneUsb.IsIphoneNow(null, true, "MEO-5G"), "");
        check("A8: sem hotspot, o iPhone guardado não vale (o cabo decide)", !PhoneUsb.IsIphoneNow(null, true, null), "");
        check("A8: o que se vê ganha ao guardado", !PhoneUsb.IsIphoneNow(false, true, "MEO-5G") && PhoneUsb.IsIphoneNow(true, false, null), "");

        var steps = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Hotspot = true, Usb = none }, ssid: "MEO-5G");
        check("A8: o Phone setup de um iPhone só por Wi-Fi diz que o cabo é opcional",
            steps.Any(s => s.Detail?.Contains("the cable is optional") == true) && !steps.Any(s => s.Detail?.Contains("USB debugging") == true), "");
        check("A8: o iPhone fica guardado nas definições", typeof(Settings).GetProperty(nameof(Settings.PhoneIsIphone)) != null, "");
    }
}
