using System.IO;
using System.Reflection;
using SRT;
using SRT.Core;

// 1.0.0 (bloqueador 4 da auditoria final): as estatísticas já não são obrigatórias na beta. Ligadas por omissão, com um interruptor
// (Settings > Send usage stats, e Turn off no aviso do 1.º arranque) que as desliga sem fechar a app. Desligado: nada sai, por
// nenhum caminho de envio. Cada verificação falha com o código de antes.
static class Estatisticas1_0Tests
{
    static readonly BindingFlags Priv = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    static void Call(string name, params object[] args)
    {
        var m = typeof(Telemetry).GetMethod(name, Priv) ?? throw new Exception("sem " + name);
        if (m.Invoke(null, args) is Task t) t.Wait(TimeSpan.FromSeconds(10));
    }

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var tmp = Path.Combine(Path.GetTempPath(), "srt-stats-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        var sent = new List<string>();
        try
        {
            Gate(check, tmp);
            Aviso5(check, tmp, repo);
            Sending(check, tmp, sent);
            Revisao(check, tmp);
            Texts(check, repo);
        }
        finally
        {
            Reset(tmp);
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>
    /// Back to "not answered", so later tests start from nothing. Off first: Start alone never turns off stats that a check which
    /// blew up left on, and the rest of the suite would run with them on and without the hook (a real report at a sleep or sign-out).
    /// </summary>
    static void Reset(string tmp)
    {
        Telemetry.SetOff(true);
        Telemetry.TestSend = null;
        Telemetry.Start(false, () => (null, null, new()), false, Path.Combine(tmp, "reset.json"));
    }

    static void Set(string field, object value) => (typeof(Telemetry).GetField(field, Priv) ?? throw new Exception("sem " + field)).SetValue(null, value);

    /// <summary>
    /// Revisão de 25 nov. (publish/plano-1-0-0/revisao-25nov-estatisticas.md, achados 1, 7, 8, 9, 10 e 12): o interruptor desliga
    /// mesmo quando o settings.json não se deixa escrever, desligado não conta nada (quedas do telemóvel, duração da sessão), a cópia
    /// de segurança das definições não volta a ligar, um relatório a caminho não sai a dobrar, e sem ID não há nada para apagar.
    /// </summary>
    static void Revisao(Action<string, bool, string> check, string tmp)
    {
        var sent = new List<(string Path, string Body)>();
        var gate = new object();
        Action<string> during = null;
        Telemetry.TestSend = (url, body) =>
        {
            var path = url[url.LastIndexOf('/')..];
            lock (gate) sent.Add((path, body));
            during?.Invoke(path);
            return Task.FromResult(true);
        };
        string Reports() { lock (gate) return string.Join("\n", sent.Where(x => x.Path == "/v1").Select(x => x.Body)); }
        (string, string, Dictionary<string, int>) Now() => ("cs2open", "usb", new());
        var tf = Path.Combine(tmp, "rev-telemetry.json");
        Telemetry.Start(false, Now, true, tf, off: false);
        Telemetry.Count("probe_rev");
        Set("_started", DateTime.UtcNow.AddHours(-3));   // a app está aberta há 3 horas

        // 1: o settings.json não se deixa escrever (antivírus, disco cheio) no momento em que se desliga
        Exception err = null;
        var threw = false;
        try { err = MainWindow.SwitchStats(true, () => throw new IOException("settings.json is locked")); } catch { threw = true; }
        Telemetry.Count("probe_after_fail");
        Call("Send"); Call("SendLive"); Call("SendAtExit");
        check("Revisão 25 nov. (1): com a gravação a falhar o interruptor desliga na mesma e devolve o erro em vez de rebentar",
            !threw && err is IOException && !Telemetry.Running && sent.Count == 0, $"threw={threw} on={Telemetry.Running} sent={sent.Count}");
        Telemetry.SetOff(true);
        lock (gate) sent.Clear();

        // 8: desligado, as quedas do telemóvel não contam, e a sessão que sai no fecho não cobre o tempo desligado
        Telemetry.PhoneDrop(); Telemetry.PhoneDrop(); Telemetry.PhoneDrop();
        Telemetry.SetOff(false);
        Telemetry.PhoneDrop();
        lock (gate) sent.Clear();
        Call("SendAtExit");
        var close = Reports();
        check("Revisão 25 nov. (8): as quedas do telemóvel com as estatísticas desligadas não contam (só a de depois de ligar)",
            close.Contains("\"drop_1\"") && !close.Contains("drop_3p"), close);
        check("Revisão 25 nov. (8): a duração da sessão não cobre o tempo em que estiveram desligadas",
            close.Contains("\"sess_lt5\"") && !close.Contains("sess_gt2h"), close);

        // 10: um relatório a caminho quando se desliga: o servidor recebeu-o, por isso sai do que fica guardado para enviar
        Telemetry.Count("probe_flight");
        during = p => { if (p == "/v1") Telemetry.SetOff(true); };
        Call("Send");
        during = null;
        check("Revisão 25 nov. (10): um relatório recebido enquanto se desligava não volta a sair ao ligar de novo",
            !Telemetry.Running && File.Exists(tf) && !File.ReadAllText(tf).Contains("probe_flight"), "");
        // ...e a guarda da beta.13 fica: entregue à cópia nova (BeforeRestart), a resposta atrasada não grava por cima do ficheiro dela
        Telemetry.SetOff(false);
        Telemetry.Count("probe_handover");
        during = p => { if (p == "/v1") { Set("_on", false); Set("_handedOver", true); } };
        Call("Send");
        during = null;
        var kept = File.ReadAllText(tf).Contains("probe_handover");
        Set("_handedOver", false);
        Set("_on", true);
        Telemetry.SetOff(true);
        check("Revisão 25 nov. (10): depois do BeforeRestart a resposta atrasada continua a não gravar", kept && !Telemetry.Running, "");

        // 9: desligar grava duas vezes, por isso a cópia de segurança (settings.json.prev) também diz desligado
        var sf = Path.Combine(tmp, "rev-settings.json");
        var s = new Settings { StatsAccepted = true, StatsNotice = Telemetry.NoticeVersion };
        Store.Save(sf, s);
        Store.Save(sf, s);
        s.StatsOff = true;
        var saveErr = MainWindow.SwitchStats(true, () => Store.Save(sf, s));
        File.WriteAllText(sf, "{ \"StatsAccepted\": tr");   // estragado antes da gravação seguinte
        var asked = 0;
        var again = App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, sf);
        var back = Store.Load(sf, () => new Settings());
        check("Revisão 25 nov. (9): um settings.json estragado logo a seguir a desligar não volta a ligar as estatísticas sozinho",
            saveErr == null && again.Go && asked == 0 && back.StatsAccepted && back.StatsOff, $"asked={asked} off={back.StatsOff}");

        // 7: quem nunca ligou não tem ID, por isso Delete my stats não tem nada para apagar (nem diz que há um ID novo)
        var never = Path.Combine(tmp, "rev-never.json");
        Telemetry.Start(false, Now, true, never, off: true);
        check("Revisão 25 nov. (7): quem nunca ligou não tem ID para apagar, e perguntar não cria nada", !Telemetry.HasId() && !File.Exists(never), "");
        Telemetry.SetOff(false);
        var has = Telemetry.HasId();
        Telemetry.SetOff(true);
        check("Revisão 25 nov. (7): depois de ligar há ID, e continua a haver com as estatísticas desligadas", has && Telemetry.HasId(), "");

        // 12: o fim deste teste desliga mesmo que uma verificação rebente com as estatísticas ligadas
        Telemetry.Start(false, Now, true, Path.Combine(tmp, "rev-reset.json"), off: false);
        var wasOn = Telemetry.Running;
        Reset(tmp);
        check("Revisão 25 nov. (12): o fim do teste deixa as estatísticas desligadas e sem o gancho, mesmo que tenham ficado ligadas",
            wasOn && !Telemetry.Running && Telemetry.TestSend == null, $"wasOn={wasOn} on={Telemetry.Running}");
    }

    /// <summary>The notice: Accept and Turn off both open the app; the choice is kept with the settings and survives a restart.</summary>
    static void Gate(Action<string, bool, string> check, string tmp)
    {
        var file = Path.Combine(tmp, "settings.json");
        var asked = 0;
        var accept = App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, file);
        check("Estatísticas 1.0: Accept abre a app e fica ligado", accept is { Go: true, JustAccepted: true, Off: false } && asked == 1, accept.ToString());
        var turnOff = App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, file);
        check("Estatísticas 1.0: Turn off (ou Escape) também abre a app, nunca a fecha", turnOff is { Go: true, JustAccepted: true, Off: true } && turnOff.FirstStart, turnOff.ToString());

        // o que a janela principal guarda e a escolha sobrevive a reiniciar
        var s = new Settings { StatsAccepted = true, StatsNotice = Telemetry.NoticeVersion, StatsOff = turnOff.Off };
        Store.Save(file, s);
        var again = Store.Load(file, () => new Settings());
        asked = 0;
        var next = App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, file);
        check("Estatísticas 1.0: desligar sobrevive a reiniciar e o aviso não volta", again.StatsOff && again.StatsAccepted && next.Go && asked == 0 && !next.JustAccepted, "");
        check("Estatísticas 1.0: quem aceitou na beta não tem o campo, e isso lê-se como ligado (o aviso 5 pergunta-lhe uma vez)",!Store.Load(WriteJson(tmp, "beta.json", "{ \"StatsAccepted\": true, \"StatsNotice\": 4 }"), () => new Settings()).StatsOff
            && !new Settings().StatsOff, "");
        check("Estatísticas 1.0: o demo não pergunta nem desliga", App.StatsGate(new[] { "--demo" }, () => { asked++; return false; }, Path.Combine(tmp, "x.json")) is { Go: true, Off: false } && asked == 0, "");
    }

    static string WriteJson(string dir, string name, string json) { var p = Path.Combine(dir, name); File.WriteAllText(p, json); return p; }

    /// <summary>
    /// Aviso 5 (25 nov., achado 2 da revisão, decidido pelo coordenador): quem aceitou o aviso 4 nas betas 11 a 14, quando as
    /// estatísticas eram obrigatórias, vê o aviso uma vez ao atualizar, com o que mudou (já não são obrigatórias, e onde se desligam),
    /// e fica com o que escolher. Quem já as tem desligadas não envia nada e não é perguntado outra vez.
    /// </summary>
    static void Aviso5(Action<string, bool, string> check, string tmp, string repo)
    {
        check("Aviso 5: NoticeVersion é 5", Telemetry.NoticeVersion == 5, Telemetry.NoticeVersion.ToString());
        var beta = WriteJson(tmp, "a5-beta.json", "{ \"StatsAccepted\": true, \"StatsNotice\": 4 }");
        var asked = 0;
        var accept = App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, beta);
        check("Aviso 5: quem aceitou o aviso 4 (betas 11 a 14) vê o aviso ao atualizar, e não conta como 1.º arranque",
            asked == 1 && accept is { Go: true, JustAccepted: true, AskedAgain: true, Off: false } && !accept.FirstStart, accept.ToString());
        var esc = App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, beta);
        check("Aviso 5: Turn off, Escape ou fechar o aviso desligam e a app abre, como no 1.º arranque",
            asked == 2 && esc is { Go: true, JustAccepted: true, AskedAgain: true, Off: true }, esc.ToString());
        var mudou = Telemetry.NoticeChangedFor(4);
        var corpo = App.StatsNoticeBody(4);
        check("Aviso 5: a quem aceitou o 4 o aviso diz que já não são obrigatórias e onde se desligam, e só isso",
            mudou != "" && corpo == Telemetry.NoticeShort + "\n\n" + mudou && mudou.StartsWith("Changed since you last accepted: ") && mudou.Contains("no longer required")
            && mudou.Contains("(Turn off)") && mudou.Contains("Settings > Send usage stats") && !mudou.Contains("shorter") && !mudou.Contains("every 30 minutes") && mudou.Length < 200, mudou);
        check("Aviso 5: quem aceitou um aviso mais antigo lê isso e o resto que mudou",
            new[] { 1, 2, 3 }.All(h => Telemetry.NoticeChangedFor(h) is var m && m.Contains("no longer required") && m.Contains("Settings > Send usage stats") && m.Contains("shorter")), Telemetry.NoticeChangedFor(3));
        // o que a janela principal grava ao responder (o aviso de agora e a escolha): não volta a perguntar, e a escolha fica
        var s = Store.Load(beta, () => new Settings());
        s.StatsNotice = Telemetry.NoticeVersion;
        s.StatsOff = esc.Off;
        Store.Save(beta, s);
        asked = 0;
        check("Aviso 5: respondido uma vez, não volta a aparecer e fica o que se escolheu",
            App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, beta) is { Go: true, JustAccepted: false } && asked == 0 && Store.Load(beta, () => new Settings()).StatsOff, "");
        var five = WriteJson(tmp, "a5-five.json", "{ \"StatsAccepted\": true, \"StatsNotice\": 5 }");
        check("Aviso 5: com o aviso 5 aceite não aparece",
            App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, five) is { Go: true, JustAccepted: false } && asked == 0 && App.StatsNoticeBody(5) == Telemetry.NoticeShort, "");
        // já desligadas no interruptor com o aviso 4 guardado (uma build de teste da 1.0.0): nada é enviado, por isso não se pergunta outra vez
        var off = WriteJson(tmp, "a5-off.json", "{ \"StatsAccepted\": true, \"StatsNotice\": 4, \"StatsOff\": true }");
        var gateOff = App.StatsGate(Array.Empty<string>(), () => { asked++; return true; }, off);
        check("Aviso 5: com as estatísticas já desligadas o aviso não aparece, e continuam desligadas",
            gateOff is { Go: true, JustAccepted: false } && asked == 0 && Store.Load(off, () => new Settings()) is { StatsAccepted: true, StatsOff: true }, gateOff.ToString());
        // o primeiro arranque continua igual: sem "o que mudou"
        check("Aviso 5: o 1.º arranque não leva a frase do que mudou", App.StatsNoticeBody(0) == Telemetry.NoticeShort, "");

        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo }.Concat(p).ToArray()));
        foreach (var lang in new[] { "es", "pt-BR", "ru", "tr" })
        {
            var j = Read("src", "Lang", lang + ".json");
            check($"Aviso 5: {lang} tem as duas frases do que mudou",
                j.Contains("\"Changed since you last accepted: the stats are no longer required. Turn them off here (Turn off) or any time in Settings > Send usage stats.\": \"")
                && j.Contains("\"The stats are also no longer required: turn them off here (Turn off) or any time in Settings > Send usage stats.\": \""), lang);
        }
        var readme = Read("README.md");
        var log = Read("CHANGELOG.md");
        var unreleased = log[log.IndexOf("## [Unreleased]", StringComparison.Ordinal)..log.IndexOf("## [0.1.0-beta.14]", StringComparison.Ordinal)];
        check("Aviso 5: o README e o CHANGELOG dizem que quem aceitou numa beta vê o aviso uma vez (já não \"stays on\")",
            !readme.Contains("anyone who accepted in the beta stays on") && readme.Contains("Anyone who accepted in a beta sees the notice once more after updating")
            && readme.Contains("and after 1.0.0, because the stats are no longer required")
            && !unreleased.Contains("accepted in the beta stays on") && unreleased.Contains("Anyone who accepted in a beta sees the notice once after updating"), "");
    }

    /// <summary>Every way a report can leave the app, on and off.</summary>
    static void Sending(Action<string, bool, string> check, string tmp, List<string> sent)
    {
        var tf = Path.Combine(tmp, "telemetry.json");
        var lockObj = new object();
        Telemetry.TestSend = (url, body) => { lock (lockObj) sent.Add(url.Substring(url.LastIndexOf('/'))); return Task.FromResult(true); };
        Telemetry.LiveFrom(() => ("servers", true, true, false));
        Telemetry.WindowFrom(() => (1240, 1.0));
        Telemetry.Start(false, () => ("cs2open", "usb", new()), true, tf, off: false);
        check("Estatísticas 1.0: ligadas, a app conta", Telemetry.Running, "");
        Telemetry.Count("probe_on");

        // ligado: cada caminho envia (sem isto, "desligado não envia" passava por não haver caminho nenhum a testar)
        void AllPaths()
        {
            Call("Send");
            Call("SendLive");
            Call("SendEnv", true);
            Call("SendBeforeSleep");
            Call("SendAtExit");
        }
        Telemetry.Count("probe_more");
        AllPaths();
        Telemetry.Count("probe_kept");
        var on = sent.ToArray();
        check("Estatísticas 1.0: ligadas, saem o relatório, o sinal de 5 min e o de ambiente", on.Contains("/v1") && on.Contains("/live") && on.Contains("/env"), string.Join(",", on));
        var id = IdIn(tf);
        check("Estatísticas 1.0: o ID da instalação existe", id != "", File.Exists(tf) ? "" : "sem ficheiro");

        // desligar
        sent.Clear();
        Telemetry.SetOff(true);
        var before = File.ReadAllText(tf);
        Telemetry.Count("probe_off"); Telemetry.Click("Some_Click"); Telemetry.Error(new InvalidOperationException("x")); Telemetry.Fatal(new Exception("y"));
        Telemetry.Screen("servers"); Telemetry.Data(1000);
        AllPaths();
        Call("OnTick");
        Call("Resumed");
        check("Estatísticas 1.0: desligadas, não sai nada (relatório, 5 min, ambiente, sono, fecho)", !Telemetry.Running && sent.Count == 0, string.Join(",", sent));
        check("Estatísticas 1.0: desligadas, nada é contado nem guardado (nem erros)", File.ReadAllText(tf) == before && !File.ReadAllText(tf).Contains("probe_off")
            && !File.ReadAllText(tf).Contains("Some_Click") && !File.ReadAllText(tf).Contains("InvalidOperationException") && File.ReadAllText(tf).Contains("probe_kept"), "");
        Telemetry.Count("probe_off");

        // Delete my stats continua a funcionar com elas desligadas
        var deleted = Telemetry.Forget().GetAwaiter().GetResult();
        check("Estatísticas 1.0: Delete my stats funciona com as estatísticas desligadas (pedido do utilizador, vai ao servidor)", deleted && sent.Contains("/forget") && IdIn(tf) != id && IdIn(tf) != "", string.Join(",", sent));
        sent.Clear();

        // voltar a ligar: continua com o mesmo ID (o de depois do Delete), e envia outra vez
        var idOff = IdIn(tf);
        Telemetry.SetOff(false);
        check("Estatísticas 1.0: ligar de novo retoma e mantém o ID", Telemetry.Running && IdIn(tf) == idOff, "");
        Telemetry.Count("probe_back");
        AllPaths();
        check("Estatísticas 1.0: ligadas de novo, volta a enviar", sent.Contains("/v1") && sent.Contains("/live"), string.Join(",", sent));
        Telemetry.SetOff(true);
        sent.Clear();

        // arrancar já desligado (a escolha guardada): nada de ID, nada de ficheiro, nada enviado
        var tf2 = Path.Combine(tmp, "start-off.json");
        Telemetry.Start(false, () => ("cs2open", "usb", new()), true, tf2, off: true);
        Telemetry.Count("probe_start_off");
        AllPaths();
        check("Estatísticas 1.0: a app arranca desligada sem ID, sem ficheiro e sem enviar", !Telemetry.Running && !File.Exists(tf2) && sent.Count == 0, string.Join(",", sent));
        check("Estatísticas 1.0: Delete my stats sem nunca ter ligado não envia nem cria ID", Telemetry.Forget().GetAwaiter().GetResult() && sent.Count == 0, string.Join(",", sent));
        // sem "ligadas" por engano: o demo e a build de gravação não ligam mesmo com tudo aceite
        Telemetry.Start(true, () => ("cs2open", "usb", new()), true, Path.Combine(tmp, "demo.json"), off: false);
        check("Estatísticas 1.0: o demo não liga as estatísticas", !Telemetry.Running, "");
    }

    static string IdIn(string file)
    {
        if (!File.Exists(file)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), "\"Id\": \"([0-9a-f]{32})\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>The words: nothing says the stats are required, and the switch is where the settings are.</summary>
    static void Texts(Action<string, bool, string> check, string repo)
    {
        string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo }.Concat(p).ToArray()));
        var xaml = Read("src", "MainWindow.xaml");
        var settings = Read("src", "Views", "MainWindow.Settings.cs");
        var tel = Read("src", "Views", "MainWindow.Telemetry.cs");
        var main = Read("src", "MainWindow.xaml.cs");
        check("Estatísticas 1.0: o Settings tem o interruptor Send usage stats, ligado por omissão e guardado com as outras definições",
            xaml.Contains("x:Name=\"StatsBox\"") && xaml.Contains("Text=\"Send usage stats\"") && xaml.Contains("Click=\"StatsSwitch_Click\"")
            && settings.Contains("StatsBox.IsChecked = !_s.StatsOff;") && tel.Contains("_s.StatsOff = StatsBox.IsChecked != true;") && tel.Contains("SwitchStats(_s.StatsOff, SaveSettings)") && tel.Contains("Telemetry.SetOff(off);")
            && main.Contains("Telemetry.Start(_demo, TelemetryNow, _s.StatsAccepted, off: _s.StatsOff);") && main.Contains("_s.StatsOff = stats.Off;"), "");
        // revisão de 25 nov.: o interruptor avisa quando a escolha não ficou guardada (1), o Log não diz "accepted" a quem desligou (4),
        // e Delete my stats sem ID não diz que há um ID novo (7)
        var click = tel[tel.IndexOf("void StatsSwitch_Click(", StringComparison.Ordinal)..];
        click = click[..click.IndexOf("async void DeleteStats_Click(", StringComparison.Ordinal)];
        check("Revisão 25 nov. (1): o interruptor diz ao utilizador quando a escolha não ficou guardada",
            click.Contains("is Exception") && click.Contains("Log.Warn(") && click.Contains("Toast(Loc.T(\"Couldn't save your usage stats choice."), "");
        check("Revisão 25 nov. (4): quem escolhe Turn off no aviso não lê \"Usage stats accepted\" no Log",
            main.Contains("Log.Info(stats.Off ? \"Usage stats turned off in the notice. Nothing is counted or sent.\" : \"Usage stats accepted. README > Telemetry lists what is sent.\");")
            && !main.Contains("the usage stats notice was accepted"), "");
        var del = tel[tel.IndexOf("async void DeleteStats_Click(", StringComparison.Ordinal)..];
        del = del[..del.IndexOf("void TelemetryHook()", StringComparison.Ordinal)];
        check("Revisão 25 nov. (7): Delete my stats sem ID diz que não há nada para apagar, antes de perguntar e sem falar num ID novo",
            del.IndexOf("if (!Telemetry.HasId())", StringComparison.Ordinal) is var no && no > 0 && no < del.IndexOf("await Ask(", StringComparison.Ordinal)
            && del.Contains("Loc.T(\"Nothing to delete: there is no stats ID on this PC. One is only made when the stats are on.\")"), "");
        // (5): o aviso não diz a base legal (decisão do dono); o comentário não pode dizer que diz, e a base está onde ele aponta
        var telemetry = Read("src", "Core", "Telemetry.cs");
        var doc = telemetry[telemetry.IndexOf("/// The notice at the first start", StringComparison.Ordinal)..telemetry.IndexOf("public static string NoticeShort", StringComparison.Ordinal)];
        check("Revisão 25 nov. (5): o comentário do aviso não diz que a linha Kept by dá a base legal, e diz onde ela está",
            !doc.Contains("why, the legal basis, where") && doc.Contains("The legal basis is not in the notice") && doc.Contains("README > Telemetry") && doc.Contains("docs/PRIVACY-AND-SECURITY.md"), doc);
        check("Revisão 25 nov. (5): a base legal está no README e no PRIVACY, a mesma nos dois",
            Read("README.md").Contains("legitimate interest in finding and fixing bugs (GDPR art. 6(1)(f))") && Read("docs", "PRIVACY-AND-SECURITY.md").Contains("legitimate interest in finding and fixing bugs (GDPR art. 6(1)(f))")
            && !Telemetry.NoticeShort.Contains("because you accept") && !Telemetry.NoticeShort.Contains("consent"), "");
        // (13): comentários do tempo em que o aviso tinha Quit
        check("Revisão 25 nov. (13): nenhum comentário fala do Quit do aviso, que já não existe",
            !Read("src", "Views", "NoticeWindow.xaml.cs").Contains("Accept and Quit") && !Read("src", "Core", "Network", "Guard.cs").Contains("Quit after an update")
            && !Read("tests", "SecurityTests.cs").Contains("(Accept ou Quit)") && !Read("tests", "SecurityTests.cs").Contains("// Quit (ou Escape)"), "");
        var all = new[] { "src/Core/Telemetry.cs", "src/MainWindow.xaml", "src/Views/MainWindow.Telemetry.cs", "src/App.xaml.cs" }.Select(f => Read(f.Split('/'))).Aggregate((a, b) => a + b);
        check("Estatísticas 1.0: nenhum texto da app diz obrigatório ou em beta nas estatísticas", !all.Contains("required while") && !all.Contains("required in the beta") && !all.Contains("They are required") && !all.Contains("This beta sends"), "");
        foreach (var lang in new[] { "es", "pt-BR", "ru", "tr" })
        {
            var j = Read("src", "Lang", lang + ".json");
            check($"Estatísticas 1.0: {lang} sem frases obrigatórias nas estatísticas",
                !j.Contains("\"This beta sends usage stats") && !j.Contains("required while the app is in beta") && !j.Contains("(they are required in the beta)")
                && !j.Contains("Quit closes the app. Nothing on your PC changes.") && j.Contains("\"Send usage stats\"") && j.Contains("\"Turn off\""), lang);
            check($"Revisão 25 nov.: {lang} tem os dois textos novos (a escolha que não ficou guardada, nada para apagar)",
                j.Contains("\"Couldn't save your usage stats choice. It holds while the app is open; check this switch after the next start.\": \"")
                && j.Contains("\"Nothing to delete: there is no stats ID on this PC. One is only made when the stats are on.\": \""), lang);
        }
    }
}
