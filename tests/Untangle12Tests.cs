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
        PhoneLinkState(check);
        AppNames(check);
        await Cs2OpenAndClose(check);
        TypedValues(check);
    }

    // B6: a app decide por valores tipados, não por cópias dos textos do ecrã; os textos ficam iguais
    static void TypedValues(Action<string, bool, string> check)
    {
        var t = new DateTime(2026, 11, 6, 20, 0, 0);
        const string On = "[Client] CL:  Connected to '203.0.113.10:27015'";
        static string Code(string c, int signOn = 6) => $"[Client] Disconnection during connection phase. Sign-on state: {signOn} (x). Disconnect reason: NETWORK_DISCONNECT_{c}.";
        Disconnect Take(params string[] lines)
        {
            var k = new DisconnectTracker();
            foreach (var l in lines) k.Feed(l, t);
            return k.Take(t);
        }
        var slot = Take(On, "You were kicked to make room for a reserved slot", Code("KICKED"));
        var slotCode = Take(On, Code("REJECT_SERVERFULL", 6));
        check("B6: slot reservado (dito pelo servidor ou pelo código já no jogo) tem o seu motivo, com o texto de sempre",
            slot?.Why == KickReason.ReservedSlot && slotCode?.Why == KickReason.ReservedSlot && slot.Reason == "kicked to make room for a reserved slot" && slotCode.Reason == slot.Reason, $"{slot} {slotCode}");
        var full = Take(On, Code("REJECT_SERVERFULL", 2));
        check("B6: cheio a entrar não é slot reservado", full?.Why == KickReason.Other && full.Reason == "the server is full", $"{full}");
        var never = Take(On, "[Client] Disconnected from server: NETWORK_DISCONNECT_CONNECT_REQUEST_TIMEDOUT");
        var lost = Take(On, "[Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT");
        check("B6: nunca respondeu é um motivo, perder a ligação não", never?.Why == KickReason.NeverAnswered && never.Reason == "the server never answered" && lost?.Why == KickReason.Other, $"{never} {lost}");
        var ban = Take(On, Code("KICKBANADDED"));
        check("B6: ban só pelo código diz \"banned\" e não o repete entre parênteses", ban?.Why == KickReason.Banned && ban.Reason == "banned" && ban.ReasonInBrackets == "", $"{ban}");
        var vote = Take(On, Code("KICKED_VOTEDOFF"));
        check("B6: outro motivo vai entre parênteses", vote?.Why == KickReason.Other && vote.ReasonInBrackets == " (kicked by a vote)", $"{vote}");
        check("B6: um motivo escrito à mão igual ao texto não engana a app", new Disconnect(KickKind.Full, "", null, "kicked to make room for a reserved slot").Why == KickReason.Other
            && new Disconnect(KickKind.Ban, "", null, Why: KickReason.Banned).Reason == "banned", "");

        var vdf = "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-condebug -conclearlog\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";
        var already = Cs2LaunchOptions.Patch(vdf);
        var broken = Cs2LaunchOptions.Patch("\"users\"\n{\n}\n");
        check("B6: opções já lá é um resultado, não um texto", already.Already && !already.Changed && !broken.Already && !broken.Changed, already.Why);
        check("B6: só as contas deixadas por um motivo contam como falha",
            !new Cs2LaunchOptions.AccountResult("1", false, "x", "x", "already there", Already: true).Left && new Cs2LaunchOptions.AccountResult("2", false, null, null, "already there").Left
            && !new Cs2LaunchOptions.AccountResult("3", true, null, null, null).Left, "");

        var row = new ProfileRow(new AccountItem(new LocalAccount("76561198000000001", "l", "Ana", false, 0), new AccountMeta()));
        row.Session = "Sign-in expired";
        var byText = row.SessionLevelNow;
        row.Expired = true;
        check("B6: o âmbar da sessão expirada vem do estado, não do texto", byText == "none" && row.SessionLevelNow == "warn", byText);

        check("B6: os modos dos configs guardam-se com os mesmos valores", new Settings().ConfigMode == "off" && string.Join(",", CfgMode.All) == "off,main,previous,latest", "");
        check("B6: o gatilho \"closed\" só copia em Latest change wins",
            Cs2Config.Plan(CfgMode.Main, "a", null, null, CfgTrigger.Closed, null, new[] { "a", "b" }, new HashSet<string>(), new[] { "binds" }).Count == 0
            && Cs2Config.Plan(CfgMode.Latest, null, null, "a", CfgTrigger.Closed, null, new[] { "a", "b" }, new HashSet<string>(), new[] { "binds" }).Count == 1, "");

        check("B6: o motivo de desconfiança vem da janela como valor",
            MainWindow.DistrustNow(true, true, true) == Distrust.SameIp && MainWindow.DistrustNow(false, true, true) == Distrust.PhoneWifi
            && MainWindow.DistrustNow(false, false, true) == Distrust.NoHome && MainWindow.DistrustNow(false, false, false) == Distrust.None, "");
        var wifi = new GuardReport { Distrust = Distrust.PhoneWifi };
        check("B6: o relatório diz o motivo e o texto de sempre", wifi.Distrusted && wifi.DistrustReason == "The phone's Wi-Fi is on, so it may be sharing your home network instead of mobile data."
            && !new GuardReport().Distrusted && new GuardReport().DistrustReason == null
            && DistrustText.Of(Distrust.Unchecked) == "The phone connection's public IP hasn't been checked yet.", wifi.DistrustReason);

        check("B6: o tempo esgotado do sign-in tem nome", new SteamWebException("x", eresult: 9).SignInTimedOut && new SteamWebException("x", eresult: 27).SignInTimedOut
            && !new SteamWebException("x", eresult: 5).SignInTimedOut, "");

        // O bug: um Firewall que não se lê virava um nome de regra falso e "não sobrou nada"
        var unread = new FirewallRules { Error = "Access is denied." };
        check("B6: Firewall ilegível não é um nome de regra", unread.Count == 0 && unread.ErrorText == "(could not read the Firewall: Access is denied.)", "");
        check("B6: Firewall ilegível nunca é \"não sobrou nada\" (o arranque mantém a proteção com o CS2 aberto)", Firewall.BlockLeft(unread), "");
        check("B6: na janela de Install, ilegível não inventa um bloqueio", !Firewall.BlockLeft(unread, unreadableCounts: false), "");
        check("B6: lido e vazio é mesmo nada", !Firewall.BlockLeft(new FirewallRules()) && Firewall.BlockLeft(new FirewallRules { Firewall.Cs2RuleName }), "");
    }

    // B5: abrir e fechar o CS2 num só ajudante: os argumentos, o carimbo de "fechado pela app" com o motivo, o console.log
    static async Task Cs2OpenAndClose(Action<string, bool, string> check)
    {
        check("B5: o Steam abre o CS2 com o console.log ligado", Cs2Launch.SteamArgs == "-applaunch 730 -condebug -conclearlog", Cs2Launch.SteamArgs);
        check("B5: as opções que a app junta no Steam são as mesmas com que ela abre o CS2",
            Cs2LaunchOptions.Has(Cs2Launch.SteamArgs) && Cs2LaunchOptions.Add("-novid") == "-novid " + string.Join(" ", Cs2Launch.ConsoleFlags), Cs2LaunchOptions.Add("-novid"));
        check("B5: o Steam reaberto leva -silent à frente se for o caso",
            Cs2Launch.SteamRestartArgs(true) == "-silent -applaunch 730 -condebug -conclearlog" && Cs2Launch.SteamRestartArgs(false) == Cs2Launch.SteamArgs, "");

        var now = new DateTime(2026, 11, 6, 20, 0, 0);
        int kills = 0, polls = 0;
        var cs2 = new Cs2Launch(() => now, () => kills++, () => ++polls < 3);
        check("B5: sem nada fechado pela app, uma saída do CS2 é dele", !cs2.ClosedByAppRecently && cs2.ClosedReason == null, "");
        await cs2.Close("the Steam restart");
        check("B5: fechar o CS2 carimba sempre, com o motivo", kills == 1 && cs2.ClosedByAppRecently && cs2.ClosedReason == "the Steam restart", cs2.ClosedReason);
        check("B5: fechar espera que o CS2 saia", polls == 3, polls.ToString());
        now = now.AddSeconds(59);
        check("B5: ainda dentro do minuto conta como da app", cs2.ClosedByAppRecently, "");
        now = now.AddSeconds(2);
        check("B5: passado o minuto uma saída volta a ser do CS2", !cs2.ClosedByAppRecently && cs2.ClosedReason == null, "");
        cs2.Closing("the launch options change");
        check("B5: o Steam a fechar leva o CS2: carimba sem matar nada", kills == 1 && cs2.ClosedReason == "the launch options change", "");

        check("B5: o console.log fica em game\\csgo ao lado do cs2.exe guardado",
            Cs2Launch.ConsoleLog(new Settings { Cs2Path = @"D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe" })
            == @"D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log", "");
        check("B5: o menu do CS2 é o loopback, um servidor não",
            Cs2Console.IsMenu("CL:  Connected to 'loopback:0'") && !Cs2Console.IsMenu("CL:  Connected to '203.0.113.9:27015'") && !Cs2Console.IsMenu(null), "");

        var dir = Path.Combine(Path.GetTempPath(), "srt-b5-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var log = Path.Combine(dir, "console.log");
            File.WriteAllText(log, "CL:  Connected to 'loopback:0'\nold line\n");
            var tail = new ConsoleTail();
            tail.StartAt(log);
            check("B5: o que já estava no console.log antes de abrir o CS2 não conta", tail.ReadNew(log).Count == 0, "");
            File.AppendAllText(log, "CL:  Connected to 'loopback:0'\n");
            check("B5: o menu escrito depois de abrir o CS2 conta", tail.ReadNew(log).Any(Cs2Console.IsMenu), "");

            File.WriteAllText(log, new string('x', 200) + "\n");
            tail.StartAt(log);
            File.WriteAllText(log, "CL:  Connected to 'loopback:0'\n");   // -conclearlog: CS2 starts the file again, shorter
            check("B5: um console.log começado de novo lê-se do início", tail.ReadNew(log).Any(Cs2Console.IsMenu), "");

            var later = Path.Combine(dir, "later.log");
            tail.StartAt(later);
            File.WriteAllText(later, "CL:  Connected to 'loopback:0'\n");
            check("B5: sem console.log ao abrir, o que vier lê-se todo", tail.ReadNew(later).Any(Cs2Console.IsMenu), "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // B3: os nomes da app (exe, pastas, ficheiros, argumentos, regras, User-Agent) num só sítio, com os valores de sempre
    static void AppNames(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var csproj = File.ReadAllText(Path.Combine(repo, "src", "ServerRouteTool.csproj"));
        var names = System.Text.RegularExpressions.Regex.Matches(csproj, "<AssemblyName>([^<]+)</AssemblyName>").Select(m => m.Groups[1].Value).ToList();
        check("B3: o exe publicado chama-se como o AssemblyName do csproj", names.Count > 0 && names[0] + ".exe" == AppIds.ExeName, string.Join(", ", names));
        check("B3: o build Recording tem outro nome e não se atualiza sozinho", names.Skip(1).All(n => n != AppIds.Name) && names.Contains(AppIds.Name + "-Recording"), string.Join(", ", names));
        check("B3: o update usa .new.exe e .old com o mesmo nome", AppIds.NewExeName == "CS2IPChanger.new.exe" && AppIds.ExeName + AppIds.OldSuffix == "CS2IPChanger.exe.old", "");

        var cmd = File.ReadAllText(Path.Combine(repo, "tools", "restore.cmd"));
        check("B3: o restore.cmd apaga as regras com o mesmo prefixo da app", cmd.Contains($"-DisplayName '{AppIds.RulePrefix}*'"), "");
        check("B3: todas as regras da app começam pelo prefixo", new[] { Firewall.RuleName, Firewall.EchoRuleName, Firewall.Cs2RuleName, Firewall.DnsUdpRuleName,
            Firewall.DnsTcpRuleName, Firewall.Ipv6RuleName, Firewall.RegionsRuleName }.All(n => n.StartsWith(AppIds.RulePrefix + " - ")) && Firewall.RuleName == "ServerRouteTool v9 - kill switch", "");

        check("B3: a cópia nova recebe o estado da proteção depois de --updated",
            AppIds.Args.ForUpdate(AppIds.Args.UpdateState(true)) == "--updated --on" && AppIds.Args.ForUpdate(AppIds.Args.UpdateState(false)) == "--updated --off"
            && AppIds.Args.ForUpdate(AppIds.Args.Standby) == "--updated --standby", "");
        check("B3: um estado que a cópia nova não conhece não passa", AppIds.Args.ForUpdate(null) == "--updated" && AppIds.Args.ForUpdate("--demo") == "--updated", "");
        check("B3: --restore conta escrito à mão em qualquer caixa, os outros não",
            AppIds.Args.IsRestore(new[] { "--RESTORE" }) && !AppIds.Args.IsRestore(new[] { "--demo", "restore" }) && SRT.Program.DllSearchProblem(false, true, new[] { "--Restore" }) == null, "");
        check("B3: o comando de desfazer é o exe com --restore", AppChanges.RestoreCommand(@"C:\x\" + AppIds.ExeName) == "\"C:\\x\\CS2IPChanger.exe\" --restore", "");
        check("B3: a pasta de instalação é a do Installer, em Program Files",
            Installer.Target == AppIds.InstallDir && AppIds.InstallDir.EndsWith(@"\CS2 IP Changer")
            && SRT.Program.FolderProblem(Path.GetTempPath(), Path.GetTempPath())?.Contains($"create the folder {AppIds.InstallDir}, copy CS2IPChanger.exe into it") == true, "");
        check("B3: a pasta do ProgramData é a mesma para o Uninstall e o Install", Uninstall.Root == AppIds.ProgramDataDir && Installer.StageDir == Path.Combine(AppIds.ProgramDataDir, "install"), "");
        check("B3: os pedidos da app levam o User-Agent de sempre", AppIds.UserAgent == "CS2IPChanger/0.1", "");

        // O Install deixa de fora o que o Store deixa ao lado de um ficheiro estragado ou a meio
        var dir = Path.Combine(Path.GetTempPath(), "srt-b3-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, AppIds.Files.Settings);
            File.WriteAllText(file, "{ cortado");
            Store.Load(file, () => new Settings());
            var left = Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n != AppIds.Files.Settings).ToList();
            check("B3: o Install não copia o ficheiro estragado que o Store guardou", left.Count == 1 && left.All(Installer.Skip), string.Join(", ", left));
            check("B3: o Install não copia o .tmp de um save, o owned.json nem o running.flag",
                Installer.Skip(AppIds.Files.Servers + AppIds.Files.TmpSuffix) && Installer.Skip(AppIds.Files.Owned) && Installer.Skip(AppIds.Files.RunningFlag) && !Installer.Skip(AppIds.Files.Servers), "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // B2: um só estado da ligação do telemóvel (hotspot, Wi-Fi do telemóvel, internet de casa, modo efetivo) para toda a app
    static void PhoneLinkState(Action<string, bool, string> check)
    {
        var onHome = new PhoneInfo("S1", "device", "Pixel", "MEO", "5G", 80, WifiOn: true, WifiSsid: "Casa", WifiConnected: true);
        check("B2: Wi-Fi do telemóvel numa rede, por cabo: não é de confiança", PhoneLink.WifiOn(onHome, null) && PhoneLink.WifiOn(onHome, false), "");
        check("B2: no hotspot (a ligação é Wi-Fi) o Wi-Fi do telemóvel não conta", !PhoneLink.WifiOn(onHome, true), "");
        check("B2: Wi-Fi ligado sem rede (a procurar) não conta", !PhoneLink.WifiOn(onHome with { WifiConnected = false }, null), "");
        check("B2: Android antigo que não diz se está numa rede conta", PhoneLink.WifiOn(onHome with { WifiConnected = null }, null), "");
        check("B2: telemóvel por autorizar ou sem telemóvel não conta", !PhoneLink.WifiOn(onHome with { State = "unauthorized" }, null) && !PhoneLink.WifiOn(null, null), "");

        var phone = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var hotspot = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "172.20.10.2", Gateway = "172.20.10.1" };
        var home = new Adapter { Index = 5, Up = true, LocalIp = "192.168.1.40", Gateway = "192.168.1.1" };
        check("B2: com a rede de casa ao lado há internet de casa", PhoneLink.HasHome(new[] { phone, home }, phone) && PhoneLink.HasHome(new[] { home }, null), "");
        check("B2: o hotspot e outro telemóvel não são internet de casa", !PhoneLink.HasHome(new[] { hotspot, phone }, hotspot), "");
        check("B2: um adaptador em baixo ou sem gateway não é internet de casa",
            !PhoneLink.HasHome(new[] { phone, new Adapter { Index = 5, Up = false, Gateway = "192.168.1.1" }, new Adapter { Index = 6, Up = true } }, phone), "");

        check("B2: proteção desligada é \"off\" em Game only e Whole PC", PhoneLink.EffectiveMode(false, "servers", true, true) == "off" && PhoneLink.EffectiveMode(false, "all", false, false) == "off", "");
        check("B2: No phone continua \"home\" com a proteção desligada", PhoneLink.EffectiveMode(false, "home", false, false) == "home", "");
        check("B2: ligada, o modo é o de sempre", PhoneLink.EffectiveMode(true, "servers", true, true) == "all" && PhoneLink.EffectiveMode(true, "servers", false, true) == "servers", "");

        check("B2: a ligação do telemóvel num relatório é a protegida ou a que está a ser verificada",
            PhoneLink.Exit(new GuardReport { Candidate = hotspot }) == hotspot && PhoneLink.Exit(new GuardReport { Egress = phone, Candidate = phone }) == phone && PhoneLink.Exit(null) == null, "");
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
