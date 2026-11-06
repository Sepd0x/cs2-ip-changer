using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área E: Guard, Firewall, rotas): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
// Nada aqui escreve na Firewall nem nas rotas do PC: o Guard só corre com a tabela de rotas e o PowerShell a fingir.
static class RevisaoETests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão E: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        Closing(check, Src);
        await GuardAndRoutes(check, Src);
    }

    static string OwnedFile(int metricIf, params string[] routes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "srt-revisaoe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { Routes = routes.ToList(), MetricIf = metricIf, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        return file;
    }

    static async Task GuardAndRoutes(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var guard = Src("Core", "Network", "Guard.cs");

        // E3. Telemóvel tirado com a métrica aplicada: sem PowerShell a cada sync, e o aviso uma vez
        check("Revisão E3: um adaptador que já não existe não tem métrica a devolver",
            Guard.MetricGone(7, new[] { new Adapter { Index = 5 } }) && !Guard.MetricGone(7, new[] { new Adapter { Index = 7 } }) && !Guard.MetricGone(-1, Array.Empty<Adapter>()), "");
        check("Revisão E3: a sync esquece-a sem PowerShell (o fecho, o Turn off e o --restore continuam a tentar)",
            Body(guard, "async Task KeepPcOffPhone(").Contains("if (step == PcOffPhoneStep.Undo && MetricGone(_metricAppliedTo, adapters))")
            && !Body(guard, "async Task RestoreMetric()").Contains("Net.Scan()"), "");
        var failing = new Guard(OwnedFile(7), (a, ms) => Task.FromResult(new ProcResult(1, "No MSFT_NetIPInterface objects found")));
        var warned = 0;
        void Seen(LogEntry e) { if (e.Text.StartsWith("Couldn't give the phone's adapter its default route back")) warned++; }
        Log.Added += Seen;
        try
        {
            await failing.ReleasePcOffPhone();
            Log.Info("Revisão E3: outra linha no meio (o Log junta linhas iguais seguidas)");
            await failing.ReleasePcOffPhone();
        }
        finally { Log.Added -= Seen; }
        check("Revisão E3: o mesmo erro de devolver a métrica vai ao Log uma vez", warned == 1, $"{warned} linhas");
        check("Revisão E3: e a métrica continua marcada para o próximo arranque ou o --restore", failing.PcOffPhoneApplied && failing.Owned().MetricIf == 7, "");

        // E5. Game only: o comportamento fica (o lado seguro), o texto deixa de prometer o CS2 inteiro pelo telemóvel
        check("Revisão E5: a opção Steam through the phone diz que o matchmaking precisa dela em Game only com o CS2 trancado",
            Src("MainWindow.xaml").Contains("Text=\"Avoids the 'No Steam logon' kick. In Game only with CS2 only through the phone on, matchmaking needs it. Steam downloads also use mobile data.\""), "");
        var docs = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "docs", "FEATURES.md"));
        var pages = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tools", "site-pages.mjs"));
        check("Revisão E5: o FEATURES e o guia do site dizem que um servidor fora da lista pode não ligar",
            docs.Contains("a community server that isn't on your list may not connect until you add it") && pages.Contains("a community server that isn't on your list may not connect until you add it"), "");
        check("Revisão E5: o comportamento não mudou (os blocos da Valve só com Steam through the phone)", guard.Contains("if (s.SteamOnEgress && egress != null)"), "");

        // E6. A regra do CS2 vai para o cs2.exe que corre, não para uma cópia antiga guardada
        check("Revisão E6: com o CS2 a correr, a regra vai para o cs2.exe que corre", Steam.PickCs2(@"C:\old\cs2.exe", true, new[] { @"D:\SteamLibrary\cs2.exe" }) == @"D:\SteamLibrary\cs2.exe", "");
        check("Revisão E6: com o CS2 fechado, o guardado vale enquanto existe", Steam.PickCs2(@"C:\old\cs2.exe", true, Array.Empty<string>()) == @"C:\old\cs2.exe"
            && Steam.PickCs2(@"C:\old\cs2.exe", false, Array.Empty<string>()) == null && Steam.PickCs2(@"C:\old\cs2.exe", true, new string[] { null }) == @"C:\old\cs2.exe", "");
        check("Revisão E6: o FindCs2 usa essa ordem", Body(Src("Core", "Steam", "SteamAccounts.cs"), "public static string FindCs2(").Contains("PickCs2(saved, saved != null && File.Exists(saved), running)"), "");

        // E7. O IP de casa nunca se mede por uma VPN (o telemóvel a partilhar o Wi-Fi de casa passava)
        var vpn = new Adapter { Index = 40, IsVpn = true, Up = true, Gateway = "10.8.0.1" };
        var home = new Adapter { Index = 18, IsWifi = true, Up = true, Gateway = "192.0.2.1" };
        check("Revisão E7: o IP de casa nunca se mede por uma VPN", MainWindow.HomeIf(new GuardReport { Adapters = new() { vpn, home }, PcDefaultIndex = 40 }, null) == -1, "");
        check("Revisão E7: pela rede de casa continua a medir-se", MainWindow.HomeIf(new GuardReport { Adapters = new() { vpn, home }, PcDefaultIndex = 18 }, null) == 18
            && MainWindow.HomeIf(new GuardReport { Adapters = new() { home }, PcDefaultIndex = 18, PcDefaultIsEgress = true }, null) == -1, "");
        check("Revisão E7: à espera do IP de casa com uma VPN, o Status manda desligar a VPN um momento",
            MainWindow.NoHomeSub("Servers and CS2", true).Contains("turn the VPN off for a moment") && !MainWindow.NoHomeSub("Servers and CS2", true).Contains("Close CS2")
            && MainWindow.NoHomeSub("Servers and CS2", false) == "Servers and CS2 stay blocked. Close CS2 (or pick Game only) for a moment so the app can read your home IP and compare it with the phone's.", "");

        // E8. Uma leitura falhada da tabela de rotas não é "não há rotas"
        check("Revisão E8: netsh com timeout não é uma tabela vazia", Net.ParseRoutes(new ProcResult(-2, "Timed out: netsh interface ipv4 show route")) == null
            && Net.ParseRoutes(new ProcResult(0, "")) == null, "");
        var table = Net.ParseRoutes(new ProcResult(0, "No       Manual    1    203.0.113.9/32             23  192.168.42.129\nNo       System    256  127.0.0.0/8                 1  Loopback Pseudo-Interface 1\n"));
        check("Revisão E8: uma tabela lida continua a dar as rotas", table?.Count == 2 && table[0] == new Net.Route("203.0.113.9/32", 23, "192.168.42.129"), "");
        var owned = new[] { new RouteKey("203.0.113.9/32", 23, "192.168.42.129") };
        check("Revisão E8: sem tabela nada se esquece; com ela, o que o Windows tirou", Guard.GoneFrom(owned, null).Count == 0 && Guard.GoneFrom(owned, new List<Net.Route>()).Count == 1
            && Guard.GoneFrom(owned, table).Count == 0, "");
        const string key = "203.0.113.9/32|23|192.168.42.129";
        var blind = new Guard(OwnedFile(-1, key), (a, ms) => Task.FromResult(new ProcResult(0, "")), new Guard.RouteTable(() => Task.FromResult<List<Net.Route>>(null), rt => Task.FromResult(new ProcResult(0, ""))));
        await blind.RemoveIps(new[] { "203.0.113.9" }, new Settings());
        check("Revisão E8: com a tabela por ler o Guard não lança nem esquece a rota", blind.Owned().Routes.Contains(key), "");
        check("Revisão E8: e o Turn off diz que não confirmou as rotas", Body(guard, "public Task<string> RestoreAll(").Contains("left.Add(\"routes (could not read the route table)\")"), "");

        // E9. Rotas do sistema no telemóvel (on-link) nunca são da app, e o next hop on-link lê-se como 0.0.0.0
        var phone = new[] { new Adapter { Index = 23, IsPhone = true, Up = true, LocalIp = "192.168.42.47", Gateway = "192.168.42.129" } };
        var none = new HashSet<string>();
        var sys = Net.ParseRouteLine("No       System    256  192.168.42.47/32           23  Ethernet 3");
        check("Revisão E9: on-link lê-se como 0.0.0.0", sys?.NextHop == "0.0.0.0" && sys.Prefix == "192.168.42.47/32" && sys.Index == 23, sys?.ToString() ?? "null");
        check("Revisão E9: uma rota do sistema no telemóvel nunca é da app (lida de qualquer forma)",
            !Guard.IsOurs(sys, none, phone, -1, none, anyHostOnPhone: true) && !Guard.IsOurs(new Net.Route("192.168.42.47/32", 23, "Ethernet"), none, phone, -1, none, anyHostOnPhone: true)
            && !Guard.IsOurs(new Net.Route("255.255.255.255/32", 23, "0.0.0.0"), none, phone, -1, none, anyHostOnPhone: true), "");
        check("Revisão E9: um /32 de servidor no telemóvel com o gateway continua a ser da app",
            Guard.IsOurs(new Net.Route("198.51.100.7/32", 23, "192.168.42.129"), none, phone, -1, none, anyHostOnPhone: true)
            && Guard.IsOurs(Net.ParseRouteLine("No       Manual    1    203.0.113.9/32             23  192.168.42.129"), none, phone, -1, new HashSet<string> { "203.0.113.9" }), "");
        check("Revisão E9: uma rota on-link da app (adaptador sem gateway) bate com o owned.json",
            Guard.IsOurs(Net.ParseRouteLine("No       Manual    1    203.0.113.9/32             31  Wi-Fi"), new HashSet<string> { "203.0.113.9/32|31|0.0.0.0" }, phone, -1, none), "");

        // E10. Proteção desligada: uma regra que não saiu tenta-se outra vez, uma vez por minuto
        var t0 = new DateTime(2026, 11, 6, 20, 0, 0);
        check("Revisão E10: depois de uma regra que ficou, espera um minuto", !Guard.HomeRewriteDue(false, true, t0, t0.AddSeconds(60)) && Guard.HomeRewriteDue(false, true, t0.AddSeconds(61), t0.AddSeconds(60)), "");
        check("Revisão E10: sem nada a sobrar, como antes (muda, reescreve; pedido, reescreve)",
            Guard.HomeRewriteDue(false, true, t0, default) && !Guard.HomeRewriteDue(false, false, t0, default) && Guard.HomeRewriteDue(true, false, t0, t0.AddSeconds(60)), "");
        check("Revisão E10: a assinatura do modo desligado só fica sem bloqueio a sobrar (lido, sem contar um Firewall ilegível)",
            Body(guard, "async Task<GuardReport> HomeMode(").Contains("if (fwProblem == null && Firewall.BlockLeft(Firewall.Remaining(), unreadableCounts: false))")
            && Body(guard, "async Task<GuardReport> HomeMode(").Contains("errors.Add(\"a protection rule is still there\");"), "");

        // E11. Ao acordar do sono o telemóvel é medido outra vez
        var main = Src("MainWindow.xaml.cs");
        check("Revisão E11: ao acordar do sono o telemóvel é medido outra vez",
            main.Contains("SystemEvents.PowerModeChanged += OnPowerModeChanged;") && Body(main, "void OnPowerModeChanged(").Contains("PowerModes.Resume")
            && Body(main, "void OnPowerModeChanged(").Contains("_verifiedKey = null;") && Body(main, "void OnPowerModeChanged(").Contains("Dispatcher.BeginInvoke"), "");
        check("Revisão E11: e o evento sai ao fechar", Body(Src("Views", "MainWindow.Window.cs"), "protected override void OnClosed(").Contains("SystemEvents.PowerModeChanged -= OnPowerModeChanged;"), "");

        // E12. Sem a permissão de localização do Windows, o nome do Wi-Fi não se sabe (não é "não ligado ao hotspot")
        const string guid = "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0";
        var located = Net.SsidsFrom($"There is 1 interface on the system:\r\n\r\n    Name                   : Wi-Fi\r\n    GUID                   : {guid}\r\n    State                  : connected\r\n    SSID                   : Ana's phone\r\n    BSSID                  : 00:11:22:33:44:55\r\n");
        check("Revisão E12: com o netsh a responder, o SSID de cada adaptador", !located.Unreadable && located.ByGuid[guid] == "Ana's phone", "");
        var disconnected = Net.SsidsFrom($"There is 1 interface on the system:\r\n\r\n    Name                   : Wi-Fi\r\n    GUID                   : {guid}\r\n    State                  : disconnected\r\n");
        check("Revisão E12: desligado é sem rede, não por saber", !disconnected.Unreadable && disconnected.ByGuid[guid] == null, "");
        var refused = Net.SsidsFrom("Network shell commands need location permission to access WLAN information. Turn on Location services on the Location page in Privacy & security settings.\r\n");
        check("Revisão E12: sem GUID nenhum, o nome não se sabe", refused.Unreadable && refused.ByGuid.Count == 0, "");
        var wifi = new Adapter { Index = 12, Id = "{" + guid + "}", IsWifi = true, Up = true, LocalIp = "172.20.10.2", Gateway = "172.20.10.1", SsidUnknown = true };
        Net.PickEgress(new List<Adapter> { wifi }, wifi.Id, "Ana's phone", out var why);
        check("Revisão E12: o Status manda ligar a localização em vez de dizer que o PC não está no hotspot", why == Net.SsidLocationText && why.Contains("Location"), why ?? "");
        check("Revisão E12: e escolher o hotspot diz o mesmo", Src("Views", "MainWindow.Protection.cs").Contains("Toast(a.SsidUnknown ? Net.SsidLocationText : \"First connect the PC's Wi-Fi"), "");
    }

    static string Body(string src, string start)
    {
        var i = src.IndexOf(start);
        if (i < 0) return "";
        var end = src.IndexOf("\n    }\n", i);
        return end < 0 ? src[i..] : src[i..end];
    }

    static void Closing(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var win = Src("Views", "MainWindow.Window.cs");
        var guard = Src("Core", "Network", "Guard.cs");
        var app = Src("App.xaml.cs");
        var closing = Body(win, "async void OnClosing(");
        var keep = Body(win, "async Task KeepAndClose()");
        var onClose = Body(guard, "public Task KeepOnClose(bool cs2Running)");

        // E1. Whole PC: fechar com Keep tira as regras do DNS e do IPv6 (sobrevivem a reiniciar e deixam o PC sem nomes sem o telemóvel)
        check("Revisão E1: as regras do Whole PC são o DNS e o IPv6, e só essas",
            Firewall.WholePcRules.OrderBy(n => n).SequenceEqual(new[] { Firewall.DnsUdpRuleName, Firewall.DnsTcpRuleName, Firewall.Ipv6RuleName }.OrderBy(n => n))
            && !Firewall.WholePcRules.Contains(Firewall.RuleName) && !Firewall.WholePcRules.Contains(Firewall.Cs2RuleName) && !Firewall.WholePcRules.Contains(Firewall.EchoRuleName), "");
        check("Revisão E1: o ramo do Keep tira-as sempre, também sem a métrica aplicada",
            closing.Contains("if (!_s.CleanupOnExit && _protOn)\n        {\n            e.Cancel = true;\n            await KeepAndClose();") && !closing.Contains("if (!_guard.PcOffPhoneApplied) return;")
            && keep.Contains("await _guard.KeepOnClose(Steam.Cs2Running);"), "");
        check("Revisão E1: o Keep tira o DNS e o IPv6 e as rotas 0/1 e 128/1 só com o CS2 fechado (o jogo aberto precisa delas)",
            onClose.Contains("foreach (var n in Firewall.WholePcRules) if (rules.Error != null || rules.Contains(n)) Firewall.Remove(n);")
            && onClose.Contains("if (!cs2Running) await RemoveSplitRoutes(Net.Scan());") && onClose.Contains("await RestoreMetric();")
            && !onClose.Contains("Firewall.Remove(Firewall.RuleName)") && !onClose.Contains("RemoveAll("), "");
        check("Revisão E1: o kill switch que fica ainda mantém o jogo no arranque seguinte com o CS2 aberto",
            Guard.OnAtStart(false, Array.Empty<string>(), false, leftOn: Firewall.BlockLeft(new[] { Firewall.RuleName }), cs2Running: true, noPhone: false), "");

        // E2. "Keep protection and close" com o CS2 aberto devolve a rota por defeito ao telemóvel, e o Quit do aviso depois de um update também
        var cs2 = closing[closing.IndexOf("if (Steam.Cs2Running && !_restored && _protOn")..];
        check("Revisão E2: fechar com o CS2 aberto passa pelo Keep (rota por defeito do telemóvel e regras do Whole PC)",
            cs2.IndexOf("await KeepAndClose();") is var i && i > 0 && i < cs2.IndexOf("_closingDone = true;"), "");
        var quit = Body(app, "static void UndoAfterUpdateQuit()");
        check("Revisão E2: o Quit depois de um update com o CS2 aberto faz o mesmo, com a pasta pronta antes do Guard",
            quit.IndexOf("Paths.Ensure();") is var p && p > 0 && p < quit.IndexOf("if (Steam.Cs2Running)") && quit.Contains("new Guard().KeepOnClose(cs2Running: true).GetAwaiter().GetResult();")
            && !quit.Contains("if (Steam.Cs2Running) return;"), "");

        // E4. Turn off and close: o que o RestoreAll não tirou diz-se antes de a app fechar
        check("Revisão E4: o que ficou mostra-se", MainWindow.CloseLeftText("rule \"x\"", false) == "rule \"x\"" && MainWindow.CloseLeftText(null, false) == null, "");
        check("Revisão E4: nunca com o Windows a encerrar (nada pode segurar o fim de sessão)", MainWindow.CloseLeftText("rule \"x\"", true) == null, "");
        check("Revisão E4: o fecho já não engole o resultado nem a exceção",
            !win.Contains("keepRegions: true); } catch { }") && closing.Contains("left = await _guard.RestoreAll(") && closing.Contains("catch (Exception ex) { left = ex.Message;")
            && closing.Contains("if (CloseLeftText(left, _sessionEnding) is string what)") && closing.Contains("App.RestoredWindow(what, false).ShowAndWait();"), "");

        // X1. A pergunta "CS2 is still open" nunca aparece com o Windows a encerrar: segue como Keep
        check("Revisão X1: com o Windows a encerrar o CS2 aberto fica como Keep, sem pergunta",
            cs2.IndexOf("if (!_sessionEnding)") is var s && s > 0 && s < cs2.IndexOf("\"CS2 is still open\""), "");

        // X2. Depois do Keep nenhuma sync volta a pôr as regras ou a métrica (nem uma que já estava na fila)
        check("Revisão X2: o Keep e o fim de sessão marcam o Guard como solto antes de esperar pelo _gate",
            onClose.IndexOf("_released = true;") is var r && r > 0 && r < onClose.IndexOf("await _gate.WaitAsync();"), "");
        var sync = Body(guard, "public Task<GuardReport> Sync(");
        check("Revisão X2: uma sync depois disso não escreve nada", sync.IndexOf("if (_released)") is var x && x > 0 && x < sync.IndexOf("SyncCore("), "");
        check("Revisão X2: o Keep também pára as syncs da janela, e a resposta de uma que já ia a meio não se mostra",
            keep.Contains("_restored = true;") && keep.Contains("_tick.Stop();") && Src("Views", "MainWindow.Protection.cs").Contains("if (!_restored && !_sessionEnding)\n            {\n                if (FromSync(_s, rep)) SaveSettings();"), "");
    }
}
