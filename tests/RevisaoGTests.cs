using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área G: ciclo de vida, arranque e fecho): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoGTests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão G: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        await Closing(check, Src);
        Lifecycle(check, Src);
    }

    static void Lifecycle(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);

        // G4. Remove com a rede por limpar não apaga a app: o --restore e o Remove continuam lá para tentar outra vez
        check("Revisão G4: com a rede por limpar a app fica", Uninstall.KeepsApp("rule \"x\"") && !Uninstall.KeepsApp(null), "");
        var run = Body(Src("Core", "Uninstall.cs"), "public static async Task<Outcome> Run(");
        check("Revisão G4: o Remove volta antes de apagar a entrada em Installed apps e os ficheiros",
            run.IndexOf("if (KeepsApp(network)) return") is var k && k > 0 && k < run.IndexOf("Installer.Unregister()") && k < run.IndexOf("RemoveFiles(") && k < run.IndexOf("Paths.ReadOnly = true"), "");
        var kept = Uninstall.Result(new Uninstall.Outcome("rule \"x\"", null, null, new Uninstall.Files(false, false, false, new Uninstall.Leftovers()), true, false), @"C:\d\SRT_Dados");
        check("Revisão G4: o resultado diz que a app ficou e como tentar outra vez",
            kept.Problem && kept.Text.Contains("The app and its files stayed") && kept.Text.Contains(AppIds.Args.Restore) && !kept.Text.Contains("Kept: "), kept.Text);
        var line = Uninstall.LogLine(new Uninstall.Outcome("rule \"x\"", null, null, new Uninstall.Files(false, false, false, new Uninstall.Leftovers()), true, false));
        check("Revisão G4: e o Log não diz que os ficheiros saíram", line.Contains("the app and its files stayed to try again") && !line.Contains("files gone"), line);

        // G5. Sem o ficheiro principal (corte a meio de um save) lê-se o .tmp ou o .prev, com uma linha no Log, em vez dos valores por defeito
        check("Revisão G5: a escolha da cópia: o principal, o .tmp (o mais novo), o .prev, os valores por defeito",
            Store.LoadFrom(true, true, true) == "main" && Store.LoadFrom(false, true, true) == "tmp" && Store.LoadFrom(false, false, true) == "prev" && Store.LoadFrom(false, false, false) == "default", "");
        var dir = Path.Combine(Path.GetTempPath(), "srt-revisaog-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var p1 = Path.Combine(dir, "settings.json");
        File.WriteAllText(p1 + ".prev", "{\"Mode\":\"all\"}");
        var said = 0;
        void Seen(LogEntry e) { if (e.Text.StartsWith("settings.json was missing")) said++; }
        Log.Added += Seen;
        try
        {
            check("Revisão G5: sem o principal lê o .prev", Store.Load(p1, () => new Settings()).Mode == "all", "");
            check("Revisão G5: e diz no Log porquê", said == 1, $"{said} linhas");
        }
        finally { Log.Added -= Seen; }
        var p2 = Path.Combine(dir, "servers.json");
        File.WriteAllText(p2 + AppIds.Files.TmpSuffix, "{\"Mode\":\"home\"}");
        File.WriteAllText(p2 + ".prev", "{\"Mode\":\"all\"}");
        check("Revisão G5: o .tmp que lê ganha ao .prev (foi escrito e gravado no disco antes da troca)", Store.Load(p2, () => new Settings()).Mode == "home", "");
        File.WriteAllText(p2 + AppIds.Files.TmpSuffix, "{\"Mode\":\"ho");
        check("Revisão G5: um .tmp cortado não conta", Store.Load(p2, () => new Settings()).Mode == "all", "");
        check("Revisão G5: o Peek (aviso das estatísticas antes da janela) lê o mesmo", Store.Peek<Settings>(p1)?.Mode == "all", "");
        check("Revisão G5: sem cópia nenhuma, os valores por defeito", Store.Load(Path.Combine(dir, "none.json"), () => new Settings()).Mode == "servers", "");

        // G6. Depois de um update, a cópia nova tenta outra vez os eventos Show e Uninstall enquanto a antiga ainda fecha
        check("Revisão G6: depois de um update tenta outra vez de 1 em 1 s, até 15 s", MainWindow.EventRetry(true, 0) && MainWindow.EventRetry(true, 14) && !MainWindow.EventRetry(true, 15), "");
        check("Revisão G6: sem update, uma vez só (um evento de outro programa nunca se usa)", !MainWindow.EventRetry(false, 0), "");
        var tray = Src("Views", "MainWindow.Tray.cs");
        check("Revisão G6: o Show e o Uninstall usam essa espera",
            Body(tray, "async void ListenForShow()").Contains("await ListenFor(App.ShowEventName") && Body(tray, "async void ListenForUninstall()").Contains("await ListenFor(App.UninstallEventName")
            && Body(tray, "async Task<bool> ListenFor(").Contains("if (!EventRetry(_justUpdated, tries)) return false;"), "");

        // G7. Start with Windows: a tarefa de outro utilizador do Windows não conta como ligada para ti
        const string exe = @"C:\Program Files\CS2 IP Changer\CS2IPChanger.exe", sidA = "S-1-5-21-1-2-3-1001", sidB = "S-1-5-21-1-2-3-1002";
        check("Revisão G7: a tarefa de outro utilizador não conta como ligada", !Autostart.IsOnFor(Autostart.TaskXml(exe, sidB), exe, sidA, @"PC\Ana"), "");
        check("Revisão G7: a tua conta, pelo SID ou pelo nome que o Windows escreve", Autostart.IsOnFor(Autostart.TaskXml(exe, sidA), exe, sidA, @"PC\Ana")
            && Autostart.IsOnFor(Autostart.TaskXml(exe, @"PC\Ana"), exe, sidA, @"PC\Ana"), "");
        check("Revisão G7: outro exe continua a não contar", !Autostart.IsOnFor(Autostart.TaskXml(@"C:\x\CS2IPChanger.exe", sidA), exe, sidA, @"PC\Ana"), "");

        // G8. --tray só esconde a janela com o ícone mesmo lá
        check("Revisão G8: o arranque escondido só esconde com o ícone posto", Body(tray, "void StartHiddenIfAsked(").Contains("if (_tray?.Shown == true) Hide();")
            && !tray.Contains("if (_tray != null) Hide();"), "");
        check("Revisão G8: o ícone que o NIM_ADD não confirmou conta quando o NIM_MODIFY passa",
            Src("Core", "Tray.cs").Contains("_added = Notify(NimAdd, NifMessage | NifIcon | NifTip) || Notify(NimModify, NifMessage | NifIcon | NifTip);"), "");

        // G9. Uma cópia aberta noutra sessão do Windows: não manda procurar na barra de tarefas
        check("Revisão G9: noutra sessão diz outro utilizador", App.RunningText(true).Contains("another Windows user") && !App.RunningText(true).Contains("taskbar"), "");
        check("Revisão G9: na mesma sessão continua a mandar à barra de tarefas", App.RunningText(false).Contains("look in the taskbar"), "");
        check("Revisão G9: o arranque vê a sessão da outra cópia", Src("App.xaml.cs").Contains("AlreadyRunningWindow(OpenInOtherSession()).ShowAndWait();"), "");

        // G10. O comentário da migração dos sign-ins diz o que acontece (os de outro utilizador do Windows não passam e saem com a pasta)
        check("Revisão G10: o comentário já não diz que ficam com a pasta antiga", !Src("Core", "Steam", "ProfileSwitcher.cs").Contains("left behind with the old folder"), "");
    }

    static string Body(string src, string start)
    {
        var i = src.IndexOf(start);
        if (i < 0) return "";
        var end = src.IndexOf("\n    }\n", i);
        return end < 0 ? src[i..] : src[i..end];
    }

    static async Task Closing(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var win = Src("Views", "MainWindow.Window.cs");
        var tray = Src("Views", "MainWindow.Tray.cs");
        var app = Src("App.xaml.cs");
        var adb = Src("Core", "Phone", "Adb.cs");
        var closing = Body(win, "async void OnClosing(");

        // G1. Um segundo X, Alt+F4 ou Quit durante o restauro (ou o Keep, ou o Remove) espera por ele em vez de fechar a meio
        check("Revisão G1: um segundo X espera pelo fecho que está a correr", MainWindow.CloseWaits(true, false), "");
        check("Revisão G1: sem fecho a correr, ou com o Windows a encerrar, o X fecha", !MainWindow.CloseWaits(false, false) && !MainWindow.CloseWaits(true, true), "");
        check("Revisão G1: o OnClosing vê isso antes de tudo, antes do _closingDone que deixa fechar",
            closing.Contains("if (CloseWaits(_closeBusy, _sessionEnding)) { e.Cancel = true; return; }")
            && closing.IndexOf("CloseWaits(_closeBusy") < closing.IndexOf("if (_closingDone || _demo) return;"), "");
        check("Revisão G1: Turn off and close marca o fecho a correr antes do primeiro await e tira-o mesmo antes do Close()",
            closing.IndexOf("_closeBusy = true;") is var b && b > 0 && b < closing.IndexOf("await _regionGate.WaitAsync")
            && closing.IndexOf("_closeBusy = false;\n        Close();") > closing.IndexOf("RestoreAll("), "");
        var keep = Body(win, "async Task KeepAndClose()");
        check("Revisão G1: o Keep também (a rota do telemóvel e as regras do Whole PC não ficam a meio)",
            keep.IndexOf("_closeBusy = true;") is var k && k > 0 && k < keep.IndexOf("await _guard.KeepOnClose") && keep.Contains("_closeBusy = false;\n        Close();"), "");
        var remove = Body(tray, "async void RemoveAll_Click(");
        check("Revisão G1: e o Remove everything, nos dois Close() do fim",
            remove.Contains("_removing = _closingDone = _restored = _closeBusy = true;")
            && remove.Split("_closeBusy = false;\n            Close();").Length == 2 && remove.Contains("_closeBusy = false;\n        Close();"), "");

        // G2. Fim de sessão do Windows: o WPF fecha a janela sem esperar, por isso as regras da Firewall saem logo no SessionEnding
        check("Revisão G2: com Turn off when closing e o CS2 fechado, o fim de sessão tira a proteção", MainWindow.CleanAtSessionEnd(true, true, false), "");
        check("Revisão G2: com Keep, sem proteção ou com o CS2 aberto, não tira tudo",
            !MainWindow.CleanAtSessionEnd(false, true, false) && !MainWindow.CleanAtSessionEnd(true, false, false) && !MainWindow.CleanAtSessionEnd(true, true, true), "");
        var ending = Body(win, "void OnSessionEnding()");
        check("Revisão G2: o SessionEnding chama o restauro síncrono", win.Contains("SessionEnding += (_, _) => OnSessionEnding();") && ending.Contains("_sessionEnding = true;"), "");
        check("Revisão G2: primeiro a Firewall (o que sobrevive a reiniciar), com limite curto, e não as rotas",
            ending.Contains("_guard.RemoveRulesNow(all ? Firewall.RulesToRemove(keepRegions: true) : Firewall.WholePcRules, TimeSpan.FromSeconds(2))")
            && !ending.Contains("RestoreAll(") && !ending.Contains("FromSeconds(8)"), "");
        check("Revisão G2: nunca num update (a cópia nova fica com as regras) nem com um fecho já a correr",
            ending.Contains("if (_demo || !_protOn || _restored || _closingDone) return;"), "");
        var guard = Src("Core", "Network", "Guard.cs");
        var now = Body(guard, "public bool RemoveRulesNow(");
        check("Revisão G2: o Guard tira as regras com o _gate e um limite, e nenhuma sync escreve depois",
            now.Contains("_released = true;") && now.Contains("_gate.WaitAsync(limit / 2)") && now.Contains("work.Wait(limit)"), "");

        // G3. Fechar a meio de um Change IP manda o "on" que faltava, para o telemóvel não ficar sem dados ou em modo de avião
        var seen = new List<string>();
        Adb.PendingCommand during = null;
        var (err, _) = await Adb.Cycle("adb.exe", "SER1", Adb.Method.MobileData, 0, shell: cmd =>
        {
            seen.Add(cmd);
            if (cmd.EndsWith("enable")) during = Adb.PendingOn;
            return Task.FromResult(new ProcResult(0, ""));
        });
        check("Revisão G3: entre o off e o on fica guardado o comando que volta a ligar os dados",
            err == null && during is { Serial: "SER1", On: "svc data enable", Adb: "adb.exe" }, during?.ToString() ?? "null");
        check("Revisão G3: com o on feito já não há nada pendente", Adb.PendingOn == null && seen.SequenceEqual(new[] { "svc data disable", "svc data enable" }), string.Join(", ", seen));
        var (err2, _) = await Adb.Cycle("adb.exe", "SER1", Adb.Method.MobileData, 0, shell: cmd => Task.FromResult(new ProcResult(1, "error: device offline")));
        check("Revisão G3: um off que não passou não deixa nada pendente", err2 != null && Adb.PendingOn == null, err2 ?? "");
        check("Revisão G3: o Exit manda o on pendente antes de parar o servidor do ADB", app.Contains("Exit += (_, _) => { Crash.Ended(); Adb.SendPendingOn(); Adb.KillOwnServer(); };"), "");
        check("Revisão G3: fora da thread da janela e com limite", Body(adb, "public static void SendPendingOn()").Contains("Task.Run(() => Run(p.Adb, $\"-s {p.Serial} shell {p.On}\", 5000)).Wait(6000)"), "");

        // N1. O kill-server do Exit não espera sempre os 4 s inteiros (o Proc.Run voltava à thread da janela, presa no Wait)
        check("Revisão G N1: o kill-server corre fora da thread da janela", Body(adb, "public static void KillOwnServer()").Contains("Task.Run(() => Run(_used, \"kill-server\", 3000)).Wait(4000)")
            && Body(adb, "public static void StopServer()").Contains("Task.Run(() => Run(copy, \"kill-server\", 3000)).Wait(4000)"), "");
    }
}
