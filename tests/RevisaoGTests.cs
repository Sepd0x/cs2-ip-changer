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
