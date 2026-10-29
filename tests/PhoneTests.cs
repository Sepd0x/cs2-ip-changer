using System.IO;
using SRT;
using SRT.Core;

// Phone setup (o que fazer a seguir, o driver USB com erro) e o modo sem telemóvel. Sem telemóvel, ADB nem rede.
static class PhoneTests
{
    public delegate void CheckFn(string name, bool ok, string detail = "");

    public static void Run(CheckFn check)
    {
        // ---------------- O USB do PnP: o telemóvel com o driver em erro (o caso real: Samsung, código 10)
        {
            var samsungBad = new[]
            {
                new UsbDevice(@"USB\ROOT_HUB30\4&1A2B3C&0&0", "USB Root Hub (USB 3.0)", null),
                new UsbDevice(@"USB\VID_046D&PID_C08B\1234", "USB Input Device", null),
                new UsbDevice(@"USB\VID_04E8&PID_6860\R5CXXXXXXXX", "SAMSUNG Mobile USB Composite Device", 10),
            };
            var s = PhoneUsb.Summarize(samsungBad);
            check("Telemóvel: Samsung com erro 10 visto", s.Seen && s.Problem == 10 && s.Vendor == "Samsung", $"{s.Seen} {s.Problem} {s.Vendor}");
            var ok = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_04E8&PID_6860\R5C", "SAMSUNG Mobile USB Composite Device", null), new UsbDevice(@"USB\VID_04E8&PID_6860&MI_00\6&abc", "SAMSUNG Mobile USB Modem", null) });
            check("Telemóvel: Samsung a funcionar, sem erro", ok.Seen && ok.Problem == null);
            var childBad = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_18D1&PID_4EE7\X", "Pixel 8", null), new UsbDevice(@"USB\VID_18D1&PID_4EE7&MI_01\Y", "ADB Interface", 28) });
            check("Telemóvel: o erro de uma parte não fica escondido pela que funciona", childBad.Problem == 28 && childBad.Vendor == "Google");
            var byName = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_1234&PID_0001\Z", "Android ADB Interface", null) });
            check("Telemóvel: marca desconhecida reconhecida pelo nome", byName.Seen && byName.Vendor == "Android");
            var none = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_046D&PID_C08B\1234", "USB Input Device", null), new UsbDevice(@"HID\VID_04E8&PID_6860", "Samsung", 10) });
            check("Telemóvel: rato e outros não contam (só USB\\VID_)", !none.Seen && PhoneUsb.Summarize(null) == PhoneUsbState.None);
            var pcStuff = PhoneUsb.Summarize(new[]
            {
                new UsbDevice(@"USB\VID_17EF&PID_6047\5&1", "Lenovo ThinkPad Compact USB Keyboard", null),
                new UsbDevice(@"USB\VID_0B05&PID_19AF\9876", "AURA LED Controller", 10),
                new UsbDevice(@"USB\VID_04E8&PID_4001\S6XXXX", "Samsung Portable SSD T7", null),
            });
            check("Telemóvel: teclado Lenovo, Aura da Asus e SSD Samsung não são telemóveis", !pcStuff.Seen, $"{pcStuff.Seen} {pcStuff.Vendor}");
            var scan = PhoneUsb.Scan();
            check("Telemóvel: a leitura do PnP deste PC não rebenta", scan != null, scan.Seen ? $"visto ({scan.Vendor}{(scan.Problem is int c ? $", código {c}" : "")})" : "nenhum");
        }

        // ---------------- O passo seguinte do Phone setup
        {
            PhoneStep N(PhoneSetupState st) => PhoneSetup.Next(st);
            var ready = new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true };
            check("Setup: sem ADB instalado", N(new PhoneSetupState { AdbTool = false, AdbState = "none" }) == PhoneStep.NoAdbTool);
            check("Setup: ADB não arrancou", N(new PhoneSetupState { AdbTool = true, AdbState = null }) == PhoneStep.AdbFailed);
            check("Setup: nada no USB: ligar o cabo", N(new PhoneSetupState { AdbTool = true, AdbState = "none" }) == PhoneStep.NoUsb);
            check("Setup: driver USB em erro", N(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new(true, 10, "Samsung") }) == PhoneStep.UsbDriverError);
            check("Setup: visto no USB sem depuração USB", N(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new(true, null, "Samsung") }) == PhoneStep.NoUsbDebugging);
            check("Setup: por autorizar no ecrã", N(new PhoneSetupState { AdbTool = true, AdbState = "unauthorized", Usb = new(true, null, "Samsung") }) == PhoneStep.Unauthorized);
            check("Setup: offline", N(new PhoneSetupState { AdbTool = true, AdbState = "offline" }) == PhoneStep.Offline);
            check("Setup: partilha USB desligada", N(ready with { Tethered = false }) == PhoneStep.TetherOff);
            check("Setup: Wi-Fi do telemóvel ligado", N(ready with { WifiOn = true }) == PhoneStep.WifiOn);
            check("Setup: hotspot escolhido: sem partilha USB e o Wi-Fi fica", N(ready with { Tethered = false, Hotspot = true, WifiOn = true }) == PhoneStep.Done);
            check("Setup: o resto do PC pelo telemóvel", N(ready with { PcOnPhone = true }) == PhoneStep.PcOnPhone);
            check("Setup: tudo pronto", N(ready) == PhoneStep.Done);
            // Partilha a funcionar sem ADB: o resto do PC é visto primeiro, depois o passo do ADB (só a Change IP precisa dele)
            check("Setup: partilha sem ADB: o resto do PC primeiro", N(new PhoneSetupState { AdbTool = true, AdbState = "none", Tethered = true, PcOnPhone = true }) == PhoneStep.PcOnPhone);
            check("Setup: partilha sem ADB: depois a depuração USB (marca fora da tabela)", N(new PhoneSetupState { AdbTool = true, AdbState = "none", Tethered = true }) == PhoneStep.NoUsbDebugging);
            check("Setup: o que a app faz e o que é dele", PhoneSetup.AppDoes(PhoneStep.TetherOff) && PhoneSetup.AppDoes(PhoneStep.WifiOn) && PhoneSetup.AppDoes(PhoneStep.PcOnPhone)
                && !PhoneSetup.AppDoes(PhoneStep.NoUsb) && !PhoneSetup.AppDoes(PhoneStep.UsbDriverError) && !PhoneSetup.AppDoes(PhoneStep.Done));

            var drv = PhoneSetup.Advice(PhoneStep.UsbDriverError, 10);
            check("Setup: texto do driver com o código", drv == "Windows sees the phone but its USB driver failed (code 10). Unplug it, use another USB port or cable, or restart the phone.", drv);
            check("Setup: código 28 num Samsung fala do driver da Samsung", PhoneSetup.Advice(PhoneStep.UsbDriverError, 28, samsung: true).Contains("Samsung USB Driver"));
            check("Setup: por autorizar diz o que tocar", PhoneSetup.Advice(PhoneStep.Unauthorized).StartsWith("Allow USB debugging on the phone screen"));
            check("Setup: cabo", PhoneSetup.Advice(PhoneStep.NoUsb).StartsWith("Plug the phone into the PC"));
            check("Setup: depuração USB traz os passos", PhoneSetup.Advice(PhoneStep.NoUsbDebugging).Contains("Build number") && PhoneSetup.Advice(PhoneStep.NoUsbDebugging).Contains("USB debugging"));
            var all = Enum.GetValues<PhoneStep>().Select(x => PhoneSetup.Advice(x, 10, true)).ToList();
            check("Setup: textos sem travessões", all.All(t => !t.Contains('—') && !t.Contains('–')));
            check("Setup: nome do passo para o Log e as contagens (só letras)", Enum.GetValues<PhoneStep>().All(x => System.Text.RegularExpressions.Regex.IsMatch("phone_setup_" + PhoneSetup.Key(x), "^[a-z_]{1,48}$")));
        }

        // ---------------- Wi-Fi do telemóvel: só "1" ou "2" é ligado; erro, vazio ou sem "---" é desconhecido (não muda nada)
        {
            const string Head = "SM-G991B\nMEO\nNR\n  level: 81\n";
            var off = Adb.ParseWifi(Head + "---\n0\nWifi is disabled\n");
            check("Wi-Fi: 0 é desligado", off.On == false && off.Connected == false);
            var on = Adb.ParseWifi(Head + "---\r\n1\r\nWifi is connected to \"CasaNet\"\r\n");
            check("Wi-Fi: 1 ligado à rede", on.On == true && on.Ssid == "CasaNet" && on.Connected == true, on.Ssid);
            var scanOnly = Adb.ParseWifi(Head + "---\n2\nWifi is not connected\n");
            check("Wi-Fi: 2 ligado sem rede (só a procurar)", scanOnly.On == true && scanOnly.Connected == false && scanOnly.Ssid == null);
            check("Wi-Fi: 3 é desligado", Adb.ParseWifi(Head + "---\n3\n").On == false);
            check("Wi-Fi: resposta vazia é desconhecido (a linha do cmd wifi não conta)", Adb.ParseWifi(Head + "---\n\nWifi is connected to \"CasaNet\"\n").On == null);
            check("Wi-Fi: erro do adb é desconhecido", Adb.ParseWifi("error: closed").On == null && Adb.ParseWifi(Head + "---\nFailure calling service settings: Broken pipe (32)\n").On == null);
            check("Wi-Fi: sem --- é desconhecido", Adb.ParseWifi(Head).On == null && Adb.ParseWifi(Head + "---").On == null && Adb.ParseWifi(null).On == null);
            check("Wi-Fi: null de settings é desconhecido", Adb.ParseWifi(Head + "---\nnull\n").On == null);

            // Só bloqueia à 2.ª leitura seguida; desconhecido mantém o que estava; desligado conta logo
            var reads = 0;
            PhoneInfo P(bool on, bool unknown = false) => new("s", "device", "m", "c", "5G", 50, on, on ? "CasaNet" : null, on ? true : false, unknown);
            var a = Adb.SteadyWifi(P(true), P(false), ref reads);
            check("Wi-Fi: 1.ª leitura ligada ainda não bloqueia", !a.WifiOn && reads == 1);
            var b = Adb.SteadyWifi(P(true), a, ref reads);
            check("Wi-Fi: 2.ª leitura seguida bloqueia", b.WifiOn && b.WifiSsid == "CasaNet");
            var c = Adb.SteadyWifi(P(false, unknown: true), b, ref reads);
            check("Wi-Fi: leitura desconhecida mantém ligado", c.WifiOn);
            var d = Adb.SteadyWifi(P(false), c, ref reads);
            check("Wi-Fi: desligado conta logo", !d.WifiOn && reads == 0);
            var e = Adb.SteadyWifi(P(false, unknown: true), d, ref reads);
            var f = Adb.SteadyWifi(P(true), e, ref reads);
            check("Wi-Fi: desconhecido não liga, e uma leitura isolada também não", !e.WifiOn && !f.WifiOn);
            var g = Adb.SteadyWifi(P(false), f, ref reads);
            var h = Adb.SteadyWifi(P(true), g, ref reads);
            check("Wi-Fi: ligado-desligado-ligado não bloqueia (o alarme falso do PC dele)", !g.WifiOn && !h.WifiOn);
        }

        // ---------------- "Rest of the PC": a placa do telemóvel nunca é a rede de casa
        {
            var phone = new Adapter { Index = 21, Name = "Ethernet 5", Description = "Remote NDIS based Internet Sharing Device", Up = true, IsPhone = true, LocalIp = "10.71.5.37", Gateway = "10.71.5.1" };
            var home = new Adapter { Index = 12, Name = "Ethernet", Description = "Realtek PCIe GbE", Up = true, LocalIp = "192.168.1.20", Gateway = "192.168.1.1" };
            var checking = new GuardReport { Adapters = new() { phone, home }, Candidate = phone, PhoneIndex = 21, PcDefaultIndex = 21, PcDefaultName = "Ethernet 5", EffectiveMode = "servers" };
            var r1 = MainWindow.RestOfPc(checking);
            check("Resto do PC: telemóvel ainda por verificar não é casa", r1.Ok == false && r1.Detail.Contains("phone") && !r1.Detail.StartsWith("Home"), r1.Detail);
            var r2 = MainWindow.RestOfPc(new GuardReport { Adapters = new() { phone, home }, Egress = phone, PhoneIndex = 21, PcDefaultIndex = 12, PcDefaultName = "Ethernet", EffectiveMode = "servers" });
            check("Resto do PC: em casa diz o nome da placa de casa", r2.Ok == true && r2.Detail == "Home network: Ethernet", r2.Detail);
            var r3 = MainWindow.RestOfPc(new GuardReport { Adapters = new() { phone }, PhoneIndex = -1, PcDefaultIndex = 21, PcDefaultName = "Ethernet 5", EffectiveMode = "servers" });
            check("Resto do PC: placa de telemóvel sem ser a escolhida também é telemóvel", r3.Detail.Contains("phone"), r3.Detail);
            var r4 = MainWindow.RestOfPc(new GuardReport { Adapters = new(), PcDefaultIndex = -1, PcDefaultName = "—" });
            check("Resto do PC: sem ligação nenhuma", r4.Ok == null && r4.Detail.StartsWith("No internet"), r4.Detail);
        }

        // ---------------- O resto do PC fora do telemóvel (métrica)
        {
            check("Métrica: Game only com o PC no telemóvel e casa disponível", Guard.NeedsPcOffPhone("servers", true, 21, 21, true));
            check("Métrica: No phone também", Guard.NeedsPcOffPhone("home", true, 21, 21, true));
            check("Métrica: Whole PC não", !Guard.NeedsPcOffPhone("all", true, 21, 21, true));
            check("Métrica: sem outra ligação não", !Guard.NeedsPcOffPhone("servers", true, 21, 21, false));
            check("Métrica: o PC já em casa não", !Guard.NeedsPcOffPhone("servers", true, 12, 21, true));
            check("Métrica: opção desligada não", !Guard.NeedsPcOffPhone("servers", false, 21, 21, true));
            check("Métrica: sem telemóvel não", !Guard.NeedsPcOffPhone("servers", true, 21, -1, true));
        }

        // ---------------- O resto do PC fora do telemóvel: IgnoreDefaultRoutes (a métrica 900 sozinha não chegou no PC dele, 29 out.)
        {
            var phone = new Adapter { Index = 13, Name = "Ethernet 5", IsPhone = true, Up = true, LocalIp = "192.168.68.20", Gateway = "192.168.68.18" };
            var other = new Adapter { Index = 13, Name = "Ethernet", Up = true, LocalIp = "192.168.68.20", Gateway = "192.168.68.18" };
            Guard.PcOffPhoneStep N(bool opt = true, string user = "servers", string eff = "servers", Adapter p = null, bool home = true, int applied = -1, bool onPhone = true, double since = 1e9)
                => Guard.PcOffPhoneNext(opt, user, eff, p ?? phone, home, applied, onPhone, since);
            var A = Guard.PcOffPhoneStep.Apply; var U = Guard.PcOffPhoneStep.Undo; var Z = Guard.PcOffPhoneStep.None;
            check("Resto fora: Game only aplica", N() == A);
            check("Resto fora: No phone aplica", N(user: "home", eff: "home") == A);
            check("Resto fora: aplica mesmo com o PC ainda em casa (o Windows pode mudar depois)", N(onPhone: false) == A);
            check("Resto fora: já aplicado e o PC em casa não repete", N(applied: 13, onPhone: false) == Z);
            check("Resto fora: aplicado mas o PC no telemóvel repete só ao fim de 1 min", N(applied: 13, since: 30) == Z && N(applied: 13, since: 61) == A);
            check("Resto fora: tentativa falhada repete só ao fim de 1 min", N(since: 30) == Z && N(since: 61) == A);
            check("Resto fora: telemóvel noutra placa aplica logo", N(applied: 21, since: 5) == A);
            check("Resto fora: Whole PC desfaz", N(user: "all", eff: "all", applied: 13) == U && N(user: "all", eff: "all") == Z);
            check("Resto fora: CS2 aberto em game mode não mexe", N(eff: "all", applied: 13) == Z && N(eff: "all") == Z);
            check("Resto fora: opção desligada desfaz", N(opt: false, applied: 13) == U && N(opt: false) == Z);
            check("Resto fora: sem casa desfaz (o PC nunca fica sem rota por defeito)", N(home: false, applied: 13) == U && N(home: false) == Z);
            check("Resto fora: telemóvel desligado desfaz", Guard.PcOffPhoneNext(true, "servers", "servers", null, true, 13, false, 1e9) == U);
            check("Resto fora: placa que não é telemóvel nem Wi-Fi não", N(p: other) == Z && N(p: other, applied: 13) == U);
            check("Resto fora: hotspot por Wi-Fi também", N(p: new Adapter { Index = 7, IsWifi = true, Up = true, Gateway = "10.0.0.1" }) == A);
            var on = Guard.PcOffPhoneArgs(13, true);
            var off = Guard.PcOffPhoneArgs(13, false);
            check("Resto fora: liga IgnoreDefaultRoutes e métrica 900, só no ActiveStore", on.Contains("-IgnoreDefaultRoutes Enabled") && on.Contains("-InterfaceMetric 900") && on.Contains("-PolicyStore ActiveStore") && on.Contains("-ErrorAction Stop"), on);
            const string psOut = "NetTCPIP\\Set-NetIPInterface : Access is denied.\r\nAt line:1 char:92\r\n+ ... \r\n    + CategoryInfo : PermissionDenied";
            check("Resto fora: erro do PowerShell numa linha", Guard.PsError(psOut) == "Access is denied.", Guard.PsError(psOut));
            check("Resto fora: erro sem cmdlet fica igual", Guard.PsError("\r\nSomething failed\r\nmore") == "Something failed" && Guard.PsError(null) == "");
            check("Resto fora: repor volta à rota por defeito e métrica automática", off.Contains("-IgnoreDefaultRoutes Disabled") && off.Contains("-AutomaticMetric Enabled") && off.Contains("-PolicyStore ActiveStore"), off);
        }

        // ---------------- Gateway do telemóvel com IgnoreDefaultRoutes: o Windows deixa de o listar, lê-se do registo
        {
            var ip = System.Net.IPAddress.Parse("192.168.68.20");
            var m24 = System.Net.IPAddress.Parse("255.255.255.0");
            check("Gateway: mesma rede conta", Net.SameNetwork("192.168.68.18", ip, m24));
            check("Gateway: de outra rede (antigo) não conta", !Net.SameNetwork("192.168.1.254", ip, m24));
            check("Gateway: o próprio IP, vazio, lixo ou IPv6 não contam", !Net.SameNetwork("192.168.68.20", ip, m24) && !Net.SameNetwork(null, ip, m24)
                && !Net.SameNetwork("x", ip, m24) && !Net.SameNetwork("fe80::1", ip, m24));
            check("Gateway: máscara 0.0.0.0 não conta", !Net.SameNetwork("10.0.0.1", ip, System.Net.IPAddress.Any));
            check("Gateway: /28 do iPhone", Net.SameNetwork("172.20.10.1", System.Net.IPAddress.Parse("172.20.10.2"), System.Net.IPAddress.Parse("255.255.255.240"))
                && !Net.SameNetwork("172.20.10.17", System.Net.IPAddress.Parse("172.20.10.2"), System.Net.IPAddress.Parse("255.255.255.240")));
            check("Gateway: placa que não existe no registo", Net.SavedGateway("{00000000-0000-0000-0000-000000000000}", true, ip, m24) == null);
            // No PC com o telemóvel ligado: o registo dá o mesmo gateway que o Windows lista (com IgnoreDefaultRoutes desligado)
            var ph = Net.Scan().FirstOrDefault(a => a.IsPhone && a.Up && a.Gateway != null);
            if (ph != null)
            {
                var ni = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().First(n => n.Id == ph.Id);
                var u = ni.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                var saved = Net.SavedGateway(ph.Id, ni.GetIPProperties().GetIPv4Properties().IsDhcpEnabled, u.Address, u.IPv4Mask);
                check("Gateway: o do registo é o do Windows (telemóvel ligado)", saved == ph.Gateway, $"{saved} vs {ph.Gateway}");
            }
        }

        // ---------------- Modo sem telemóvel
        {
            check("Modo: No phone ganha ao Whole PC com o CS2 aberto", Guard.ModeFor("home", true, true) == "home");
            check("Modo: Game only", Guard.ModeFor("servers", false, true) == "servers");
            check("Modo: Whole PC while CS2 is open, CS2 aberto", Guard.ModeFor("servers", true, true) == "all");
            check("Modo: Whole PC while CS2 is open, CS2 fechado", Guard.ModeFor("servers", true, false) == "servers");
            check("Modo: Whole PC, always", Guard.ModeFor("all", false, false) == "all");
            check("Modo: valor desconhecido fica Game only", Guard.ModeFor("xyz", false, false) == "servers");
            var s = new Settings { Mode = "home", GameMode = true };
            check("Modo: No phone não é Whole PC (a qualidade não mexe)", s.NoPhone && !s.WholePc);
            check("Modo: por defeito continua como estava (Game only + Whole PC com o CS2)", !new Settings().NoPhone && new Settings().Mode == "servers" && new Settings().WholePc);

            // Migração: um settings.json antigo (sem "home") fica como estava; o No phone guarda-se e volta a ler-se.
            var dir = Path.Combine(Path.GetTempPath(), "srt-phone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var oldFile = Path.Combine(dir, "old.json");
                File.WriteAllText(oldFile, "{\"Mode\":\"all\",\"GameMode\":false,\"KeepPcOffPhone\":true}");
                var old = Store.Load(oldFile, () => new Settings());
                check("Modo: settings antigo lido como estava", old.Mode == "all" && !old.NoPhone && old.WholePc);
                var f = Path.Combine(dir, "home.json");
                Store.Save(f, new Settings { Mode = "home" });
                var text = File.ReadAllText(f);
                var back = Store.Load(f, () => new Settings());
                check("Modo: No phone guardado e lido de volta", back.NoPhone && !text.Contains("\"NoPhone\"") && !text.Contains("\"WholePc\""), back.Mode);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
