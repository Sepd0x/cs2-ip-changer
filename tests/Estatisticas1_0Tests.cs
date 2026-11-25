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
            Sending(check, tmp, sent);
            Texts(check, repo);
        }
        finally
        {
            Telemetry.TestSend = null;
            Telemetry.Start(false, () => (null, null, new()), false, Path.Combine(tmp, "reset.json"));   // back to "not answered": later tests start from nothing
            try { Directory.Delete(tmp, true); } catch { }
        }
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
        check("Estatísticas 1.0: quem aceitou na beta (sem o campo) fica ligado", !Store.Load(WriteJson(tmp, "beta.json", "{ \"StatsAccepted\": true, \"StatsNotice\": 4 }"), () => new Settings()).StatsOff
            && !new Settings().StatsOff, "");
        check("Estatísticas 1.0: o demo não pergunta nem desliga", App.StatsGate(new[] { "--demo" }, () => { asked++; return false; }, Path.Combine(tmp, "x.json")) is { Go: true, Off: false } && asked == 0, "");
    }

    static string WriteJson(string dir, string name, string json) { var p = Path.Combine(dir, name); File.WriteAllText(p, json); return p; }

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
            && settings.Contains("StatsBox.IsChecked = !_s.StatsOff;") && tel.Contains("_s.StatsOff = StatsBox.IsChecked != true;") && tel.Contains("SaveSettings();") && tel.Contains("Telemetry.SetOff(_s.StatsOff);")
            && main.Contains("Telemetry.Start(_demo, TelemetryNow, _s.StatsAccepted, off: _s.StatsOff);") && main.Contains("_s.StatsOff = stats.Off;"), "");
        var all = new[] { "src/Core/Telemetry.cs", "src/MainWindow.xaml", "src/Views/MainWindow.Telemetry.cs", "src/App.xaml.cs" }.Select(f => Read(f.Split('/'))).Aggregate((a, b) => a + b);
        check("Estatísticas 1.0: nenhum texto da app diz obrigatório ou em beta nas estatísticas", !all.Contains("required while") && !all.Contains("required in the beta") && !all.Contains("They are required") && !all.Contains("This beta sends"), "");
        foreach (var lang in new[] { "es", "pt-BR", "ru", "tr" })
        {
            var j = Read("src", "Lang", lang + ".json");
            check($"Estatísticas 1.0: {lang} sem frases obrigatórias nas estatísticas",
                !j.Contains("\"This beta sends usage stats") && !j.Contains("required while the app is in beta") && !j.Contains("(they are required in the beta)")
                && !j.Contains("Quit closes the app. Nothing on your PC changes.") && j.Contains("\"Send usage stats\"") && j.Contains("\"Turn off\""), lang);
        }
    }
}
