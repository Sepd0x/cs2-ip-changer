using System.IO;
using SRT;
using SRT.Core;

// 9 nov.: o feedback de um utilizador da beta.12 (xao), reencaminhado pelo dono: contas que pedem login, tethering ligado que a app
// diz Off, voltar a um servidor com um IP já banido, e o cooldown. Cada correção tem aqui o seu teste, que falhava antes.
static class Xao9NovTests
{
    public static void Run(Action<string, bool, string> check)
    {
        LoginComRememberMe(check);
        TetheringLigado(check);
        IpBanido(check);
        Cooldown(check);
    }

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    // "tenho de fazer sempre login" (v12): com Start Steam minimized, a beta.12 lançava "-login conta -silent" e o Steam lia
    // -silent como a password. A beta.13 já mete os extras antes (fd1117f). Aqui: a troca só lança o Steam por LoginArgs.
    static void LoginComRememberMe(Action<string, bool, string> check)
    {
        var acc = Src("Core", "Steam", "SteamAccounts.cs");
        var sw = acc[acc.IndexOf("public static async Task<long> SwitchTo(")..acc.IndexOf("public static string LoginArgs(")];
        check("xao 9 nov.: a troca de conta só lança o Steam com LoginArgs (o -login fica no fim, nada é lido como password)",
            sw.Contains("Launch(LoginArgs(accountName, extraArgs))") && !sw.Contains("Launch(\"-login")
            && Steam.LoginArgs("conta_1", "-silent").EndsWith("-login conta_1") && Steam.LoginArgs("conta_1", "-silent -tcp").EndsWith("-login conta_1"), "");
    }

    // "ja esta ativo no telemovel mas a app não vê": a linha dizia "Off. The app can turn it on"
    static void TetheringLigado(Action<string, bool, string> check)
    {
        check("xao 9 nov.: sys.usb.state com ncm (telemóveis novos) conta como tethering ligado",
            Adb.TetherInUsbState("ncm,adb") && Adb.TetherInUsbState("rndis,adb") && Adb.TetherInUsbState("rndis\r\n")
            && !Adb.TetherInUsbState("mtp,adb") && !Adb.TetherInUsbState("adb") && !Adb.TetherInUsbState(null), "");

        check("xao 9 nov.: o 'UsbNcm Host Device' do Windows 11 é o telemóvel",
            Net.IsPhoneAdapter("UsbNcm Host Device #2", "Ethernet 4", false, "192.168.42.129", null)
            && Net.LooksLikePhone("UsbNcm Host Device"), "");

        var ips = new[] { "192.168.42.129" };
        check("xao 9 nov.: uma placa com nome desconhecido é o telemóvel quando o gateway é o IP do tethering dele (ADB)",
            Net.IsPhoneAdapter("Xyz Mobile Network Card", "Ethernet 5", false, "192.168.42.129", ips)
            && !Net.IsPhoneAdapter("Xyz Mobile Network Card", "Ethernet 5", false, "192.168.1.1", ips)
            && !Net.IsPhoneAdapter("Intel(R) Wi-Fi 6E AX211", "Wi-Fi", true, "192.168.42.129", ips)
            && !Net.IsPhoneAdapter("Realtek USB GbE Family Controller", "Ethernet", false, "192.168.1.1", null), "");

        var addrs = Adb.ParseAddresses(
            "1: lo    inet 127.0.0.1/8 scope host lo\\       valid_lft forever preferred_lft forever\n" +
            "14: rmnet_data1    inet 10.45.12.7/30 scope global rmnet_data1\\       valid_lft forever preferred_lft forever\n" +
            "25: rndis0    inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0\\       valid_lft forever preferred_lft forever\n" +
            "31: wlan0    inet 192.168.1.50/24 brd 192.168.1.255 scope global wlan0\n" +
            "33: swlan0    inet 192.168.122.1/24 brd 192.168.122.255 scope global swlan0\n" +
            "40: ncm0    inet 10.153.218.1/24 scope global ncm0\n");
        check("xao 9 nov.: os endereços de partilha do telemóvel (rndis, ncm, hotspot), sem os dos dados móveis",
            addrs.Count == 4 && addrs.Any(a => a.Ip == "192.168.42.129" && !a.Wifi) && addrs.Any(a => a.Ip == "10.153.218.1" && !a.Wifi)
            && addrs.Any(a => a.Ip == "192.168.122.1" && a.Wifi) && !addrs.Any(a => a.Ip.StartsWith("10.45.") || a.Ip.StartsWith("127.")),
            string.Join(", ", addrs.Select(a => a.Interface + "=" + a.Ip)));

        // O PC no hotspot Wi-Fi do telemóvel, sem o hotspot escolhido: a linha oferece usá-lo em vez de "Off. The app can turn it on"
        var wifi = new Adapter { Id = "{W}", IsWifi = true, Up = true, LocalIp = "192.168.122.40", Gateway = "192.168.122.1", Ssid = "Galaxy S25" };
        var offer = ConnectionOffer.Find(new List<Adapter> { wifi }, addrs, null, false, null);
        var steps = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Offer = offer });
        var tether = steps.First(x => x.Step == PhoneStep.TetherOff);
        check("xao 9 nov.: no hotspot do telemóvel sem o escolher, o setup diz isso e oferece Use it (e não 'Off')",
            offer is { CanUse: true, AdapterId: "{W}", Ssid: "Galaxy S25" } && tether.Action == "Use it" && tether.Detail.Contains("Galaxy S25")
            && !tether.Detail.Contains("Off.") && steps.First(x => x.Step == PhoneStep.WifiOn).State == PhoneSetup.StepState.Ok, tether.Detail);
        check("xao 9 nov.: um Wi-Fi que não é do telemóvel (a casa) não é oferecido",
            ConnectionOffer.Find(new List<Adapter> { new Adapter { Id = "{H}", IsWifi = true, Up = true, LocalIp = "192.168.1.20", Gateway = "192.168.1.1", Ssid = "Casa" } }, addrs, null, false, null) == null
            && ConnectionOffer.Find(new List<Adapter> { wifi }, null, null, false, null) == null
            && ConnectionOffer.Find(new List<Adapter> { wifi }, addrs, "{W}", true, wifi) == null, "");
        var noName = ConnectionOffer.Find(new List<Adapter> { new Adapter { Id = "{W}", IsWifi = true, Up = true, LocalIp = "192.168.122.40", Gateway = "192.168.122.1", SsidUnknown = true } }, addrs, null, false, null);
        check("xao 9 nov.: sem o nome do Wi-Fi (Localização desligada) diz o que fazer, sem botão",
            noName is { CanUse: false } && noName.Detail.Contains("Location")
            && PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Offer = noName }).First(x => x.Step == PhoneStep.TetherOff).Action == null, "");

        // A app presa a outra placa (escolhida à mão) e o tethering do telemóvel ligado: oferece voltar ao USB automático
        var usb = new Adapter { Id = "{U2}", IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var pinned = ConnectionOffer.Find(new List<Adapter> { usb }, addrs, "{U1}", false, null);
        check("xao 9 nov.: presa a uma placa que já não existe, com o tethering ligado, oferece o USB automático",
            pinned is { CanUse: true, AdapterId: null, Ssid: null } && pinned.Detail.StartsWith("On,")
            && PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Offer = pinned }).First(x => x.Step == PhoneStep.TetherOff).Title == "USB tethering", pinned?.Detail ?? "");
        check("xao 9 nov.: sem nada a oferecer, a linha continua a ser 'Off. The app can turn it on'",
            PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device" }).First(x => x.Step == PhoneStep.TetherOff).Detail.StartsWith("Off."), "");

        var phone = Src("Views", "MainWindow.Phone.cs");
        check("xao 9 nov.: o Use it do setup muda a ligação; o Turn on só corre sem nada a oferecer",
            phone.Contains("case PhoneStep.TetherOff when _setupState?.Offer is { CanUse: true } offer:")
            && phone.IndexOf("case PhoneStep.TetherOff when") < phone.IndexOf("Busy(\"Turning on USB tethering…\")"), "");
    }

    // "alertar se estiver a entrar de novo com um ip que ja fui ban e cortar mesmo com proteção off e on"
    static void IpBanido(Action<string, bool, string> check)
    {
        var ban = new ServerBan { At = new DateTime(2026, 11, 8, 21, 0, 0), Name = "EXAMPLE.GG • Retakes #1", Ip = "85.240.10.20" };
        var bans = new Dictionary<string, ServerBan> { ["203.0.113.37:27015"] = ban };
        var servers = new List<(string, string, string)>
        {
            ("203.0.113.37:27015", "EXAMPLE.GG • Retakes #1", null),
            ("203.0.113.40:27016", "EXAMPLE.GG • Retakes #2", null),
            ("198.51.100.9:27015", "Outra comunidade", null),
        };
        var targets = ServerBan.IpTargets(bans, servers, "85.240.10.20");
        check("xao 9 nov.: com o IP banido, bloqueia esse servidor e os da mesma comunidade, não os outros",
            targets.Count == 2 && targets.Contains("203.0.113.37:27015") && targets.Contains("203.0.113.40:27016"), string.Join(", ", targets));
        check("xao 9 nov.: com outro IP (Change IP) ou um ban sem IP (marcado à mão, antigo) não bloqueia nada",
            ServerBan.IpTargets(bans, servers, "85.240.10.99").Count == 0 && ServerBan.IpTargets(bans, servers, null).Count == 0
            && ServerBan.IpTargets(new Dictionary<string, ServerBan> { ["203.0.113.37:27015"] = new ServerBan { At = DateTime.Now, Name = "x" } }, servers, "85.240.10.20").Count == 0, "");
        var acabado = new ServerBan { At = DateTime.Now.AddHours(-2), Ip = "85.240.10.20", Until = DateTime.Now.AddMinutes(-1) };
        check("xao 9 nov.: um ban com tempo que já acabou não bloqueia",
            ServerBan.IpTargets(new Dictionary<string, ServerBan> { ["203.0.113.37:27015"] = acabado }, servers, "85.240.10.20").Count == 0, "");

        var accs = new List<(string, IEnumerable<KeyValuePair<string, ServerBan>>)> { ("conta1", bans), ("conta2", null) };
        var hit = ServerBan.IpBanFor(accs, "85.240.10.20", "203.0.113.40:27016", "EXAMPLE.GG • Retakes #2", null);
        check("xao 9 nov.: o Join vê o ban do IP de qualquer conta, também noutro servidor da comunidade",
            hit is { Account: "conta1", Key: "203.0.113.37:27015" } && ServerBan.IpBanFor(accs, "85.240.10.99", "203.0.113.40:27016", "EXAMPLE.GG • Retakes #2", null) == null
            && ServerBan.IpBanFor(accs, "85.240.10.20", "198.51.100.9:27015", "Outra comunidade", null) == null, "");

        check("xao 9 nov.: o IP que os servidores veem (proteção ligada: o do telemóvel; desligada: o de casa)",
            MainWindow.ServersSee(onHome: false, pcOnPhone: false, "37.1.1.1", "85.240.10.20") == "37.1.1.1"
            && MainWindow.ServersSee(onHome: false, pcOnPhone: false, null, "85.240.10.20") == null
            && MainWindow.ServersSee(onHome: true, pcOnPhone: false, "37.1.1.1", "85.240.10.20") == "85.240.10.20"
            && MainWindow.ServersSee(onHome: true, pcOnPhone: true, "37.1.1.1", "85.240.10.20") == "37.1.1.1", "");

        var rule = Firewall.BannedIpRule(new[] { "203.0.113.37:27015", "203.0.113.40:27016", "203.0.113.37:27015", "lixo", "::1:27015" });
        check("xao 9 nov.: a regra da Firewall bloqueia só UDP para esses servidores (IP e portas), sem programa nem origem",
            rule != null && rule.Name == Firewall.BannedIpRuleName && rule.Protocol == Firewall.NET_FW_IP_PROTOCOL_UDP
            && rule.RemoteIps.SequenceEqual(new[] { "203.0.113.37", "203.0.113.40" }) && rule.RemotePorts == "27015,27016"
            && rule.Program == null && rule.AllowLocalIp == null && Firewall.BannedIpRule(Array.Empty<string>()) == null, rule?.RemotePorts ?? "");
        check("xao 9 nov.: a regra sai no Turn off, ao fechar, no --restore e no Keep; não conta como proteção que ficou",
            Firewall.RulesToRemove(true).Contains(Firewall.BannedIpRuleName) && Firewall.RulesToRemove(false).Contains(Firewall.BannedIpRuleName)
            && !Firewall.BlockLeft(new[] { Firewall.BannedIpRuleName }) && Firewall.Meaning(Firewall.BannedIpRuleName) != "From an older version"
            && Src("Core", "Network", "Guard.cs").Contains("Firewall.Remove(Firewall.BannedIpRuleName)"), "");

        var text = MainWindow.IpBanText("Retakes #2", "85.240.10.20", "conta1", ban.At, onHome: false, noPhone: false);
        check("xao 9 nov.: o aviso diz o IP, quando, a conta e o que fazer (Change IP; proteção desligada: ligar)",
            text.Contains("85.240.10.20") && text.Contains("conta1") && text.Contains("Change IP")
            && MainWindow.IpBanText("x", "1", "c", ban.At, onHome: true, noPhone: false).Contains("turn on protection")
            && MainWindow.IpBanText("x", "1", "c", ban.At, onHome: true, noPhone: true).Contains("No phone mode"), text);

        var kick = Src("Views", "MainWindow.Kick.cs");
        var servers2 = Src("Views", "MainWindow.Servers.cs");
        check("xao 9 nov.: só os bans do console guardam o IP (a marca à mão não), e o Join para antes de entrar",
            kick.Contains("MarkServerBan(acc, inList, d.Minutes, ServersSeeIp())")
            && kick.Contains("MarkServerBan(acc, server, null, null, d.Minutes, ServersSeeIp())")
            && kick.Contains("MarkServerBan(a, s);") && !kick.Contains("MarkServerBan(a, s, ")
            && servers2.Contains("if (IpBanOn(s) is { } ib && !await LeaveBannedIp(s, ib)) return false;")
            && servers2.IndexOf("IpBanOn(s) is { } ib") < servers2.IndexOf("Steam.OpenUrl($\"steam://connect/{s.Key}\")"), "");
    }

    // "seria possivel automaticamente detectar um cooldown e as horas?"
    static void Cooldown(Action<string, bool, string> check)
    {
        const string line = "11/09 20:14:02 [Client] Disconnected from server: NETWORK_DISCONNECT_KICKED_COMPETITIVECOOLDOWN";
        check("xao 9 nov.: o cooldown do CS2 no console é reconhecido (continua a ser da conta, não do IP)",
            Cs2Console.IsCooldown(line) && Cs2Console.Classify(line) == KickKind.Steam && !Cs2Console.IsCooldown("NETWORK_DISCONNECT_KICKED_VACBANNED"), "");
        check("xao 9 nov.: o aviso do cooldown diz que o CS2 não diz o tempo e oferece Set time",
            MainWindow.CooldownToast("conta1").Contains("Set time") && MainWindow.CooldownToast("conta1").Contains("doesn't say")
            && Src("Views", "MainWindow.Kick.cs").Contains("\"Set time\", () => AskCooldown(acc)"), "");
    }
}
