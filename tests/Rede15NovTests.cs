using System.IO;
using SRT;
using SRT.Core;

// 15 nov.: a sessão 1 do plano (rede e proteção) sobre a auditoria final de 13 nov. (publish/auditoria-final-2026-11-13, A e B).
// Cada correção tem aqui o seu teste, que falhava antes (o relatório está em publish/plano-2026-11-15/1-rede-protecao.md).
static class Rede15NovTests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        SemCasaSemAdb(check);
        CrashEmWholePc(check);
        NomesDasRegras(check);
        MigracaoDasRegras(check);
        GatewayGuardado(check);
        await RegraDosBanidos(check);
        HookDepoisDoBloqueio(check);
        CancelFalso(check);
        SerialEscondido(check);
    }

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));
    static string Repo(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo() }.Concat(p).ToArray()));

    // Bloqueador 1 (A-04) e A-09: Android por cabo, sem depuração USB, Wi-Fi do telemóvel na rede de casa, PC sem outra rede.
    // A app dizia "Protected" e os servidores viam o IP de casa.
    static void SemCasaSemAdb(Action<string, bool, string> check)
    {
        const string ip = "92.250.100.160";
        check("Bloqueador 1: sem rede de casa, um IP de casa medido por uma ligação de casa compara (igual = bloqueia)",
            MainWindow.SameAsHome(ip, ip, hasHome: false, homeIpFromHome: true) && !MainWindow.SameAsHome(ip, "1.2.3.4", false, true), "");
        check("Bloqueador 1: o caso do iPhone de 9 nov. continua (um IP guardado antes, que pode ser o do telemóvel, não compara)",
            !MainWindow.SameAsHome(ip, ip, hasHome: false) && !MainWindow.SameAsHome(ip, ip, false, false) && MainWindow.SameAsHome(ip, ip, true), "");

        var android = new Adapter { Index = 7, Name = "Ethernet 3", Description = "Remote NDIS based Internet Sharing Device", IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var iphone = new Adapter { Index = 8, Name = "Ethernet 4", Description = "Apple Mobile Device Ethernet", IsPhone = true, Up = true, LocalIp = "172.20.10.3", Gateway = "172.20.10.1" };
        var hotspot = new Adapter { Index = 9, Name = "Wi-Fi", Description = "Intel Wi-Fi", IsWifi = true, Up = true, LocalIp = "192.168.43.5", Gateway = "192.168.43.1" };
        var ncm = new Adapter { Index = 10, Name = "Ethernet 5", Description = "UsbNcm Host Device", IsPhone = true, Up = true, LocalIp = "192.168.1.20", Gateway = "192.168.1.1" };
        check("Bloqueador 1: Android por cabo, sem ADB, sem rede de casa e sem IP de casa medido: não pode dizer Protected",
            MainWindow.MobileUnconfirmed(false, android, iphone: false, adbReady: false, homeIpFromHome: false), "");
        check("Bloqueador 1: com o ADB pronto (a app lê o Wi-Fi do telemóvel), com rede de casa, ou com IP de casa medido em casa, confirma como antes",
            !MainWindow.MobileUnconfirmed(false, android, false, true, false) && !MainWindow.MobileUnconfirmed(true, android, false, false, false)
            && !MainWindow.MobileUnconfirmed(null, android, false, false, false) && !MainWindow.MobileUnconfirmed(false, android, false, false, true), "");
        check("Bloqueador 1: o iPhone (só partilha dados móveis) e o hotspot (o PC está na rede do telemóvel) não mudam",
            !MainWindow.MobileUnconfirmed(false, iphone, false, false, false) && !MainWindow.MobileUnconfirmed(false, android, true, false, false)
            && !MainWindow.MobileUnconfirmed(false, hotspot, false, false, false) && !MainWindow.MobileUnconfirmed(false, null, false, false, false), "");
        check("A-09: um adaptador NCM de casa tomado por telemóvel, sozinho e sem ADB, também não diz Protected",
            MainWindow.MobileUnconfirmed(false, ncm, false, false, false), "");
        var (title, sub) = MainWindow.UnconfirmedHero();
        check("Bloqueador 1: o aviso diz o que fazer (Wi-Fi do telemóvel, depuração USB) e não diz Protected",
            !title.Contains("Protected") && sub.Contains("Wi-Fi") && sub.Contains("USB debugging") && sub.Contains("Phone setup"), title + " | " + sub);

        var prot = Src("Views", "MainWindow.Protection.cs");
        var ui = prot[prot.IndexOf("void UpdateStatusUi(", StringComparison.Ordinal)..];
        check("Bloqueador 1: os dois Protected do Status passam pelo aviso (nenhum SetHero(\"good\" direto)",
            ui.Contains("var unconfirmed = MobileUnconfirmedNow(rep);") && ui.Contains("Good(Loc.T(\"Protected\"), ProtectedText(rep));")
            && ui.Contains("Good(_s.Mode == \"all\" ? Loc.T(\"Protected · whole PC\")") && ui[..ui.IndexOf("void Good(", StringComparison.Ordinal)].Split("SetHero(\"good\"").Length == 1
            && ui[ui.IndexOf("void Good(", StringComparison.Ordinal)..].Split("SetHero(\"good\"").Length == 2, "");
        check("Bloqueador 1: o IP de casa guardado diz se foi medido numa ligação de casa, e só esse compara sem rede de casa",
            prot.Contains("_s.LastHomeIp = h; _s.LastHomeIpFromHome = true;") && prot.Contains("SameAsHome(e, homeRef, rep?.HasHome, h != null || _s.LastHomeIpFromHome)")
            && new Settings().LastHomeIpFromHome == false, "");
    }

    // Bloqueador 2 (A-03): um crash em Whole PC deixava as regras do DNS e do IPv6; sem o telemóvel o PC ficava sem nomes.
    static void CrashEmWholePc(Action<string, bool, string> check)
    {
        var removed = new List<string>();
        var ok = Guard.RemoveOnCrash(TimeSpan.FromSeconds(2), n => { lock (removed) removed.Add(n); });
        check("Bloqueador 2: no crash saem as 3 regras do Whole PC (DNS UDP, DNS TCP, IPv6), e só essas",
            ok && removed.Count == 3 && removed.ToHashSet().SetEquals(Firewall.WholePcRules) && !removed.Contains(Firewall.RuleName) && !removed.Contains(Firewall.Cs2RuleName), string.Join(", ", removed));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var slow = Guard.RemoveOnCrash(TimeSpan.FromMilliseconds(300), _ => Thread.Sleep(3000));
        check("Bloqueador 2: nunca espera mais do que o limite (uma Firewall presa não segura o fecho)", !slow && sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
        check("Bloqueador 2: nada rebenta se a remoção lançar", !Guard.RemoveOnCrash(TimeSpan.FromSeconds(1), _ => throw new InvalidOperationException()), "");
        var app = Src("App.xaml.cs");
        var handler = app[app.IndexOf("AppDomain.CurrentDomain.UnhandledException", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("};", StringComparison.Ordinal)];
        check("Bloqueador 2: o handler do crash tira as regras do Whole PC com limite de 2 s, só como admin e fora do demo",
            handler.Contains("Guard.RemoveOnCrash(TimeSpan.FromSeconds(2))") && handler.Contains("Adb.Elevated") && handler.Contains("AppIds.Args.Demo")
            && handler.IndexOf("Crash.Write", StringComparison.Ordinal) < handler.IndexOf("RemoveOnCrash", StringComparison.Ordinal), handler);
        check("Bloqueador 2: o FAQ diz o que fazer depois de matar a app no Gestor de Tarefas ou de uma falta de luz",
            Repo("docs", "FAQ.md").Contains("ended in Task Manager or the PC loses power while Whole PC is on"), "");
    }

    // Bloqueador 3 (A-01): o nome das regras "ServerRouteTool v9" (e os sufixos em português) passa a "CS2 IP Changer".
    static void NomesDasRegras(Action<string, bool, string> check)
    {
        var names = new[] { Firewall.RuleName, Firewall.EchoRuleName, Firewall.Cs2RuleName, Firewall.DnsUdpRuleName, Firewall.DnsTcpRuleName,
            Firewall.Ipv6RuleName, Firewall.RegionsRuleName, Firewall.BannedIpRuleName };
        check("Bloqueador 3: o prefixo novo é o nome da app", AppIds.RulePrefix == "CS2 IP Changer" && AppIds.LegacyRulePrefixes.SequenceEqual(new[] { "ServerRouteTool v9" }), AppIds.RulePrefix);
        check("Bloqueador 3: as 8 regras começam pelo prefixo novo, nenhuma por um antigo, todas diferentes",
            names.All(n => n.StartsWith(AppIds.RulePrefix + " - ")) && !names.Any(n => AppIds.LegacyRulePrefixes.Any(p => n.StartsWith(p)))
            && names.Distinct().Count() == 8 && Firewall.AllNames.ToHashSet().SetEquals(names), "");
        check("Bloqueador 3: nenhum nome de regra em português", !names.Any(n => new[] { "teste", "saida", "bloquead", "regioes", "banido" }.Any(n.Contains)), string.Join(" | ", names));
        check("Bloqueador 3: nenhuma regra é o começo de outra (o RuleLeft e os temporários contam com isso)",
            !names.Any(a => names.Any(b => a != b && b.StartsWith(a))), "");
        check("Bloqueador 3: o grupo das regras no wf.msc também é o nome novo", Src("Core", "Network", "Firewall.cs").Contains("SetP(rule, \"Grouping\", AppIds.RulePrefix);"), "");
        check("Bloqueador 3: o restore.cmd tira os dois nomes", Repo("tools", "restore.cmd").Contains("Get-NetFirewallRule -DisplayName 'CS2 IP Changer - *','ServerRouteTool v9*'"), "");
        check("Bloqueador 3: os manifestos já não dizem ServerRouteTool",
            !Src("app.manifest").Contains("ServerRouteTool") && !Src("app.debug.manifest").Contains("ServerRouteTool"), "");
        var xaml = Src("MainWindow.xaml");
        check("Bloqueador 3: a tooltip do Open Windows Firewall e o fim do Remove dizem o nome novo",
            xaml.Contains("named 'CS2 IP Changer'.") && !xaml.Contains("named 'ServerRouteTool'")
            && !Src("Core", "Uninstall.cs").Contains("names start with ServerRouteTool") && Src("Core", "Uninstall.cs").Contains("(names start with {0}).\", AppIds.RulePrefix)"), "");
        foreach (var code in new[] { "es", "pt-BR", "ru" })
        {
            var json = Src("Lang", code + ".json");
            check($"Bloqueador 3: {code} sem as chaves com ServerRouteTool, com a tooltip nova traduzida",
                !json.Contains("ServerRouteTool") && json.Contains("\"The app's rules are under Outbound Rules, named 'CS2 IP Changer'.\": \"") && json.Contains("'CS2 IP Changer'.\","), "");
        }
        check("Bloqueador 3: a página de privacidade diz o nome novo (e o antigo, de onde vem)",
            Repo("docs", "PRIVACY-AND-SECURITY.md").Contains("outbound rules named `CS2 IP Changer - ...`"), "");
    }

    static void MigracaoDasRegras(Action<string, bool, string> check)
    {
        var old = new Dictionary<string, string>
        {
            ["ServerRouteTool v9 - kill switch"] = Firewall.RuleName, ["ServerRouteTool v9 - teste de IP"] = Firewall.EchoRuleName,
            ["ServerRouteTool v9 - CS2 so pela saida"] = Firewall.Cs2RuleName, ["ServerRouteTool v9 - DNS UDP so pela saida"] = Firewall.DnsUdpRuleName,
            ["ServerRouteTool v9 - DNS TCP so pela saida"] = Firewall.DnsTcpRuleName, ["ServerRouteTool v9 - IPv6 bloqueado"] = Firewall.Ipv6RuleName,
            ["ServerRouteTool v9 - CS2 regioes bloqueadas"] = Firewall.RegionsRuleName, ["ServerRouteTool v9 - IP banido no servidor"] = Firewall.BannedIpRuleName,
        };
        check("Bloqueador 3: cada nome da beta.13 traduz-se no nome novo, e o novo sabe o seu antigo",
            old.All(kv => Firewall.Current(kv.Key) == kv.Value && Firewall.LegacyName(kv.Value) == kv.Key), "");
        check("Bloqueador 3: um temporário antigo traduz-se com a cauda; outro nome fica igual",
            Firewall.Current("ServerRouteTool v9 - kill switch (updating 0123456789ab)") == Firewall.RuleName + " (updating 0123456789ab)"
            && Firewall.Current("Outra regra") == "Outra regra" && Firewall.Current(Firewall.RuleName) == Firewall.RuleName && Firewall.Current(null) == null, "");
        check("Bloqueador 3: IsAppRule vê o nome novo, o antigo e os temporários de ambos; não vê outras regras",
            Firewall.IsAppRule("ServerRouteTool v9 - kill switch") && Firewall.IsAppRule("ServerRouteTool v9 - kill switch (updating abc)")
            && Firewall.IsAppRule(Firewall.RuleName) && Firewall.IsAppRule(Firewall.RuleName + " (updating abc)")
            && !Firewall.IsAppRule("Outra regra") && !Firewall.IsAppRule("CS2 IP Changer Pro") && !Firewall.IsAppRule(null), "");
        check("Bloqueador 3: um bloqueio deixado pela beta.13 conta como bloqueio (o arranque com o CS2 aberto mantém a proteção)",
            Firewall.BlockLeft(new[] { "ServerRouteTool v9 - kill switch" }) && Firewall.BlockLeft(new[] { "ServerRouteTool v9 - CS2 so pela saida" })
            && !Firewall.BlockLeft(new[] { "ServerRouteTool v9 - teste de IP" }) && !Firewall.BlockLeft(new[] { "Outra regra" }), "");
        check("Bloqueador 3: a regra antiga das Regions conta (o Allow all tira-a)",
            Regions.RuleLeft(new[] { "ServerRouteTool v9 - CS2 regioes bloqueadas" }) && Regions.RuleLeft(new[] { "ServerRouteTool v9 - CS2 regioes bloqueadas (updating x)" })
            && !Regions.RuleLeft(new[] { "ServerRouteTool v9 - kill switch" }), "");
        check("Bloqueador 3: o What the app changed explica a regra antiga como a nova (não 'From an older version')",
            Firewall.Meaning("ServerRouteTool v9 - kill switch") == Firewall.Meaning(Firewall.RuleName) && Firewall.Meaning("ServerRouteTool v9 - kill switch") != Firewall.Meaning("x"), "");
        check("Bloqueador 3: Has encontra uma regra pelo nome antigo", Firewall.Has(new[] { "ServerRouteTool v9 - IPv6 bloqueado" }, Firewall.Ipv6RuleName) && !Firewall.Has(new[] { "x" }, Firewall.Ipv6RuleName), "");

        // A limpeza: um v9 sai quando o novo já está lá, ou quando nenhuma versão o escreve; senão fica para o Apply/Remove dessa regra
        var (remove, left) = Firewall.LegacyToRemove(new (string, string)[]
        {
            ("ServerRouteTool v9 - kill switch", "ServerRouteTool"), (Firewall.RuleName, AppIds.RulePrefix),
            ("ServerRouteTool v9 - kill switch (updating ab)", "ServerRouteTool"),
            ("ServerRouteTool v9 - CS2 regioes bloqueadas", "ServerRouteTool"),
            ("ServerRouteTool v9 - TESTE restore.cmd", "ServerRouteTool"),
            ("SRTv8 203.0.113.1", ""), ("Uma regra", "ServerRouteTool v7"), ("Outra regra", "Outro grupo"), (null, null),
        });
        check("Bloqueador 3: a limpeza tira o v9 já renomeado, o temporário dele, o v9 que ninguém escreve e as v6-v8; deixa o v9 sem o novo",
            remove.ToHashSet().SetEquals(new[] { "ServerRouteTool v9 - kill switch", "ServerRouteTool v9 - kill switch (updating ab)", "ServerRouteTool v9 - TESTE restore.cmd", "SRTv8 203.0.113.1", "Uma regra" })
            && left == 1, string.Join(" | ", remove) + " / left " + left);
        check("Bloqueador 3: nunca tira uma regra nova nem uma de outro programa",
            !remove.Contains(Firewall.RuleName) && !remove.Contains("Outra regra"), "");

        var fw = Src("Core", "Network", "Firewall.cs");
        var apply = fw[fw.IndexOf("public static void Apply(FwRule r)", StringComparison.Ordinal)..fw.IndexOf("static void RemoveTemps(", StringComparison.Ordinal)];
        check("Bloqueador 3: o Apply tira a regra antiga só depois de a nova estar no lugar (nunca menos bloqueio)",
            apply.IndexOf("SetP(added, \"Name\", name);", StringComparison.Ordinal) is var set and > 0
            && apply.IndexOf("RemoveAllNamed(rules, legacy); RemoveTemps(rules, legacy);", StringComparison.Ordinal) > set
            && apply.Contains("if (added == null) return;"), "");
        var rm = fw[fw.IndexOf("public static void Remove(string name = RuleName)", StringComparison.Ordinal)..];
        rm = rm[..rm.IndexOf("ProtectionRules", StringComparison.Ordinal)];
        check("Bloqueador 3: Remove (Turn off, No phone, --restore, Uninstall, Allow all) tira o nome novo e o antigo, e os temporários dos dois",
            rm.Contains("RemoveAllNamed(rules, name);") && rm.Contains("if (legacy != null) RemoveAllNamed(rules, legacy);") && rm.Contains("RemoveTemps(rules, name, legacy);"), "");
        check("Bloqueador 3: Remaining e Describe leem os dois prefixos (ordinal, sem cultura)",
            fw.Split("IsAppRule(n)").Length == 3 && !fw.Contains("n.StartsWith(AppIds.RulePrefix)"), "");
        var guard = Src("Core", "Network", "Guard.cs");
        check("Bloqueador 3: a limpeza já não depende só do LegacyCleaned (que a beta.13 já tinha a true)",
            guard.Contains("if (!s.LegacyCleaned || !s.RulesRenamed)") && guard.Contains("rulesRenamed = left == 0;")
            && Src("Views", "MainWindow.Protection.cs").Contains("if (rep.RulesRenamed && !s.RulesRenamed) { s.RulesRenamed = true; changed = true; }")
            && new Settings().RulesRenamed == false, "");
        check("Bloqueador 3: o Keep e o --restore veem as regras pelo nome antigo também",
            guard.Contains("Firewall.Has(rules, n)") && guard.Contains("Firewall.Has(rules, Firewall.BannedIpRuleName)") && guard.Contains("!Firewall.Current(n).StartsWith(Firewall.RegionsRuleName"), "");
    }

    // A-02: um telemóvel reconhecido só pelo gateway deixava de o ser quando o Save mobile data tirava o gateway (a cada minuto)
    static void GatewayGuardado(Action<string, bool, string> check)
    {
        var phoneIps = new[] { "192.168.42.129" };
        check("A-02: com os endereços USB do telemóvel conhecidos, um adaptador de nome desconhecido também procura o gateway no registo",
            Net.WantsSavedGateway(false, false, phoneIps) && !Net.WantsSavedGateway(false, false, Array.Empty<string>()) && !Net.WantsSavedGateway(false, false, null)
            && Net.WantsSavedGateway(true, false, null) && Net.WantsSavedGateway(false, true, null), "");
        check("A-02: o gateway do registo só conta nesse adaptador se for o do telemóvel (uma rede de casa sem gateway não ganha um)",
            Net.UseSavedGateway("192.168.42.129", false, false, phoneIps) == "192.168.42.129" && Net.UseSavedGateway("192.168.1.1", false, false, phoneIps) == null
            && Net.UseSavedGateway("192.168.1.1", true, false, null) == "192.168.1.1" && Net.UseSavedGateway("192.168.1.1", false, true, null) == "192.168.1.1"
            && Net.UseSavedGateway(null, true, true, phoneIps) == null, "");
        check("A-02: e esse adaptador continua a ser o telemóvel com o gateway do registo",
            Net.IsPhoneAdapter("Xyz Mobile Network Card", "Ethernet 5", false, Net.UseSavedGateway("192.168.42.129", false, false, phoneIps), phoneIps), "");
        var ad = Src("Core", "Network", "Adapters.cs");
        check("A-02: o Scan usa as duas funções", ad.Contains("WantsSavedGateway(named, wifi, _phoneUsbIps)") && ad.Contains("UseSavedGateway(SavedGateway("), "");
    }

    // A-08: duas mudanças seguidas do bloqueio dos servidores que baniram o IP corriam ao mesmo tempo (a regra ficava velha ou sumia)
    static async Task RegraDosBanidos(Action<string, bool, string> check)
    {
        var latest = new LatestOnly<int>();
        var running = 0;
        var most = 0;
        var written = new List<int>();
        var tasks = Enumerable.Range(1, 12).Select(i => latest.Run(i, v =>
        {
            var now = Interlocked.Increment(ref running);
            lock (written) { most = Math.Max(most, now); written.Add(v); }
            Thread.Sleep(15);
            Interlocked.Decrement(ref running);
        })).ToArray();
        await Task.WhenAll(tasks);
        check("A-08: as escritas da regra nunca correm ao mesmo tempo, e a última pedida é a que fica",
            most == 1 && written.Count > 0 && written[^1] == 12 && written.Count <= 12 && written.SequenceEqual(written.OrderBy(x => x)), string.Join(",", written) + $" (max {most})");
        check("A-08: no arranque, a regra deixada por um crash espera pelo IP (não sai antes da primeira medição)",
            MainWindow.BannedIpWaits(true, null) && !MainWindow.BannedIpWaits(true, "1.2.3.4") && !MainWindow.BannedIpWaits(false, null), "");
        var kick = Src("Views", "MainWindow.Kick.cs");
        var body = kick[kick.IndexOf("void ApplyBannedIpBlock()", StringComparison.Ordinal)..kick.IndexOf("void WarnBannedIpJoin(", StringComparison.Ordinal)];
        check("A-08: o ApplyBannedIpBlock escreve pelo LatestOnly, não por um Task.Run por mudança",
            body.Contains("_bannedIpWrite.Run(rule,") && !body.Contains("Task.Run(") && body.Contains("if (BannedIpWaits(first, ip)) return;"), "");
    }

    // A-13: o Loc.Hook() (tipos do WPF) corria antes do resolver de DLLs e do LockDllSearch
    static void HookDepoisDoBloqueio(Action<string, bool, string> check)
    {
        var p = Src("Program.cs");
        var main = p[p.IndexOf("static int Main(", StringComparison.Ordinal)..];
        check("A-13: o Loc.Hook() vem depois do SetDllImportResolver e do LockDllSearch, e só uma vez",
            main.IndexOf("Loc.Hook();", StringComparison.Ordinal) > main.IndexOf("var locked = LockDllSearch();", StringComparison.Ordinal)
            && main.IndexOf("NativeLibrary.SetDllImportResolver", StringComparison.Ordinal) < main.IndexOf("var locked = LockDllSearch();", StringComparison.Ordinal)
            && p.Split("Loc.Hook();").Length == 2, "");
    }

    // B-01: um Cancel antigo deixava o token cancelado e o Turn on do Phone setup dizia "Cancelled." sem ninguém cancelar
    static void CancelFalso(Action<string, bool, string> check)
    {
        check("B-01: fora de um comando nada está cancelado; dentro dele, só com o Cancel",
            !MainWindow.IsCancelled(false, true) && MainWindow.IsCancelled(true, true) && !MainWindow.IsCancelled(true, false) && !MainWindow.IsCancelled(false, false), "");
        check("B-01: o CommandCancelled passa pelo IsCancelled com o comando a correr",
            Src("Views", "MainWindow.Activity.cs").Contains("bool CommandCancelled => IsCancelled(_command != null, _commandCts?.IsCancellationRequested == true);"), "");
    }

    // B-02: o erro do adb passava por modelo do telemóvel, e o número de série chegava ao ecrã, ao Log e ao bug report
    static void SerialEscondido(Action<string, bool, string> check)
    {
        var err = Adb.ParseStatus("R58M12AB34C", "device", "error: device 'R58M12AB34C' not found");
        check("B-02: sem o '---' a resposta não é do telemóvel: nada vira modelo, operadora ou rede; o Wi-Fi fica desconhecido",
            err.Model == null && err.Carrier == null && err.NetType == null && err.Battery == null && err.WifiUnknown && err.Ready, err.Model);
        var ok = Adb.ParseStatus("R58M12AB34C", "device", "Pixel 8\no2\nLTE\ngoogle\n15\nlevel: 82\n---\n0\n");
        check("B-02: a resposta certa continua a ler-se igual", ok.Model == "Pixel 8" && ok.Carrier == "o2" && ok.NetType == "4G" && ok.Battery == 82 && !ok.WifiOn && !ok.WifiUnknown && ok.Brand == "google", ok.Model);
        check("B-02: o número de série sai da saída do adb (erro e tempo esgotado), o resto fica",
            Adb.MaskSerial("error: device 'R58M12AB34C' not found", "-s R58M12AB34C shell svc data disable") == "error: device '(phone)' not found"
            && Adb.MaskSerial("Timed out: adb.exe -s R58M12AB34C shell \"getprop\"", "-s R58M12AB34C shell \"getprop\"") == "Timed out: adb.exe -s (phone) shell \"getprop\""
            && Adb.MaskSerial("List of devices attached\nR58M12AB34C\tdevice", "devices") == "List of devices attached\nR58M12AB34C\tdevice"
            && Adb.MaskSerial(null, "-s X shell") == null, "");
        check("B-02: o telemóvel que desapareceu não 'recusou' nada",
            Adb.IsGone("error: device '(phone)' not found") && Adb.IsGone("error: no devices/emulators found") && Adb.IsGone("error: device offline")
            && !Adb.IsGone("/system/bin/sh: svc: not found") && !Adb.IsGone(null), "");
        var calls = new List<string>();
        var (e1, _) = Adb.Cycle("adb", "X", Adb.Method.MobileData, 0, shell: c => { calls.Add(c); return Task.FromResult(new ProcResult(1, "error: device '(phone)' not found")); }).Result;
        check("B-02: no Change IP com o cabo tirado diz que o telemóvel não está ligado e não tenta o modo de avião",
            e1 == Adb.Gone && calls.Count == 1, e1 + " / " + string.Join(", ", calls));
        var (e2, _) = Adb.Cycle("adb", "X", Adb.Method.MobileData, 0, allowAirplane: false, shell: c => Task.FromResult(new ProcResult(1, "Exception occurred"))).Result;
        check("B-02: uma recusa verdadeira continua a dizer 'refused'", e2?.StartsWith(Adb.Refused) == true, e2);
        check("B-02: o Run do adb esconde o número de série em todas as respostas",
            Src("Core", "Phone", "Adb.cs").Contains("return r with { Output = MaskSerial(r.Output, args) };"), "");
    }
}
