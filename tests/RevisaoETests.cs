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
        await Task.CompletedTask;
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
