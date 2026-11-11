using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área H: os textos fixos da UI): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoHTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        var xaml = Src("MainWindow.xaml");
        check("Revisão H: as fontes leem-se", xaml.Length > 0, "");
        Protection(check, Src, xaml);
        Names(check, Src, xaml, repo);
        Accounts(check, Src, xaml);
        Toggles(check, Src, xaml);
    }

    static void Protection(Action<string, bool, string> check, Func<string[], string> src, string xaml)
    {
        string Src(params string[] p) => src(p);

        // H1. Game only não promete que só entras nos teus servidores sem o CS2 lock
        check("Revisão H1: Game only não diz 'You can only join those' sem falar do CS2 lock",
            !xaml.Contains("You can only join those.") && xaml.Contains("Other servers are blocked while Settings > Protection > CS2 only through the phone is on."), "");
        check("Revisão H1: a opção do CS2 lock diz o que acontece desligada", xaml.Contains("Off: servers not on your Servers page can see your home IP."), "");
        var noLock = MainWindow.LeakResult(new List<string>(), lockCs2: false, weak: false, okText: "No leaks. Servers see 198.51.100.7.");
        check("Revisão H1: sem o CS2 lock o Leak test não diz No leaks", !noLock.Ok && !noLock.Text.Contains("No leaks") && noLock.Text.Contains("CS2 only through the phone is off"), noLock.Text);
        var fine = MainWindow.LeakResult(new List<string>(), lockCs2: true, weak: false, okText: "No leaks. Servers see 198.51.100.7.");
        check("Revisão H1: com o lock e nada a vermelho continua No leaks", fine.Ok && fine.Text == "No leaks. Servers see 198.51.100.7.", fine.Text);

        // H9. Só as fugas falham o Leak test
        CheckItem C(string t) => new() { Title = t, Ok = false };
        check("Revisão H9: uma ligação fraca não é fuga", MainWindow.LeakFailures(new[] { C("Connection quality") }, wholePc: false).Count == 0, "");
        check("Revisão H9: as regiões desligadas não são fuga", MainWindow.LeakFailures(new[] { C("Matchmaking regions") }, wholePc: true).Count == 0, "");
        check("Revisão H9: em Game only, Other apps a vermelho não é fuga", MainWindow.LeakFailures(new[] { C("Other apps") }, wholePc: false).Count == 0, "");
        check("Revisão H9: em Whole PC, Other apps a vermelho continua a falhar", MainWindow.LeakFailures(new[] { C("Other apps") }, wholePc: true).SequenceEqual(new[] { "Other apps" }), "");
        check("Revisão H9: uma fuga a sério continua a falhar", MainWindow.LeakFailures(new[] { C("Windows Firewall"), C("Connection quality"), new CheckItem { Title = "Phone connection", Ok = true } }, false).SequenceEqual(new[] { "Windows Firewall" }), "");
        var weak = MainWindow.LeakResult(new List<string>(), true, weak: true, okText: "No leaks. Servers see 198.51.100.7.");
        check("Revisão H9: a ligação fraca vai numa frase à parte", weak.Ok && weak.Text == "No leaks. Servers see 198.51.100.7. The connection is weak: see Checks.", weak.Text);
        var red = MainWindow.LeakResult(new List<string> { "Windows Firewall" }, true, false, "No leaks.");
        check("Revisão H9: com uma fuga diz qual", !red.Ok && red.Text == "Warning: Windows Firewall. See Checks.", red.Text);
        var cs = Src("MainWindow.xaml.cs");
        check("Revisão H9: os títulos que o Leak test deixa de fora são os de Checks",
            cs.Contains("Title = Loc.T(\"Connection quality\")") && cs.Contains("Title = Loc.T(\"Other apps\")") && cs.Contains("Title = Loc.T(\"Matchmaking regions\")"), "");
        var prot = Src("Views", "MainWindow.Protection.cs");
        check("Revisão H9: o Leak test usa as duas regras", prot.Contains("LeakFailures(_checks, _last?.EffectiveMode == \"all\")") && prot.Contains("LeakResult(bad, _s.LockCs2,")
            && !prot.Contains("var bad = _checks.Where(c => c.Ok == false)"), "");

        // H2. O tooltip das estatísticas vem do aviso
        check("Revisão H2: o tooltip das estatísticas não é uma lista à parte do aviso", !xaml.Contains("Sent: the app and Windows versions, your country as two letters, the mode") && xaml.Contains("x:Name=\"StatsLine\""), "");
        check("Revisão H2: o tooltip vem do aviso", Src("Views", "MainWindow.Settings.cs").Contains("StatsLine.ToolTip = Loc.T(Telemetry.NoticeSent) + \" \" + Loc.T(\"Details in the README, under Telemetry.\");"), "");
        check("Revisão H2: o aviso continua igual, com a mesma frase Sent", Telemetry.NoticeShort.Contains(".\n\n" + Telemetry.NoticeSent + "\nNever sent:") && Telemetry.NoticeSent.StartsWith("Sent: a random ID"), "");
    }

    static void Names(Action<string, bool, string> check, Func<string[], string> src, string xaml, string repo)
    {
        string Src(params string[] p) => src(p);

        // H3, H4, H6, H10: nomes que não existem ou que dizem outra coisa
        check("Revisão H3: sem 'under Steps'", !xaml.Contains("under Steps") && xaml.Contains("The steps ticked in this group, in order."), "");
        check("Revisão H3: o rejoin só muda o IP quando pode", xaml.Contains("it changes the IP when it can and rejoins"), "");
        check("Revisão H4: sem o nome antigo Remove everything nos textos", !xaml.Contains("Remove everything") && xaml.Contains("Turning this off, Settings > Remove the app and --restore delete the task."), "");
        check("Revisão H6: sem 'Wait for a slot on the Servers page'", !Src("Views", "MainWindow.Kick.cs").Contains("Wait for a slot on the Servers page"), "");
        check("Revisão H10: o atalho do rejoin funciona em qualquer lado", !xaml.Contains("Works while CS2 is in front") && xaml.Contains("Works anywhere, also in game."), "");

        // H5. O Show da chave aponta para Settings > Privacy
        check("Revisão H5: o Show da chave aponta para Settings > Privacy", MainWindow.ApiKeyShowTip(hide: true, optOn: true, show: false) == "Hide usernames is on (Settings > Privacy): the key stays hidden.", "");
        check("Revisão H5: só com o OBS aberto diz isso", MainWindow.ApiKeyShowTip(true, false, false) == "OBS or Streamlabs is open (Settings > Privacy): the key stays hidden.", "");
        check("Revisão H5: sem esconder fica o Show e o Hide", MainWindow.ApiKeyShowTip(false, false, true) == "Hides the key" && MainWindow.ApiKeyShowTip(false, true, false) == "Shows the key", "");
        check("Revisão H5: o (Accounts) saiu", !Src("Views", "MainWindow.Settings.cs").Contains("(Accounts): the key stays hidden"), "");

        // H8. A opção dos jogadores seguidos pergunta sempre
        check("Revisão H8: o nome da opção não promete entrar sozinho", !xaml.Contains("Join automatically when a followed player moves") && xaml.Contains("Ask to join when a followed player moves"), "");
        check("Revisão H8: o FEATURES diz o mesmo", !File.ReadAllText(Path.Combine(repo, "docs", "FEATURES.md")).Contains("Joining them automatically"), "");

        // H11. Os nomes do ecrã, iguais em todo o lado
        foreach (var (f, t) in new[] { ("MainWindow.xaml", "Allow all on Regions"), ("Views/MainWindow.Settings.cs", "Allow all on Regions"), ("MainWindow.xaml.cs", "(Allow all)."),
                     ("MainWindow.xaml", "same network"), ("Views/MainWindow.Kick.cs", "of the same network"), ("Views/MainWindow.Servers.cs", "on the same network as"),
                     ("Views/MainWindow.Servers.cs", "Networks usually share"), ("Views/MainWindow.Accounts.cs", "this server's network"), ("MainWindow.xaml", "Colour:") })
            check($"Revisão H11: sem \"{t}\" em {f}", !Src(f.Split('/')).Contains(t), "");
        check("Revisão H11: o Allow all tem o nome do botão", xaml.Contains("Regions > Allow all (block off) lifts them") && Src("Views", "MainWindow.Settings.cs").Contains("(Regions > Allow all (block off) lifts them)"), "");
        check("Revisão H11: o mesmo IP sugere o modo avião só quando ajuda", MainWindow.SameIpToast(airplane: false, hotspot: false).Contains("Try Airplane mode in Settings > Change IP > Method"), MainWindow.SameIpToast(false, false));
        check("Revisão H11: já em modo avião, ou no hotspot, não o sugere", !MainWindow.SameIpToast(true, false).Contains("irplane") && !MainWindow.SameIpToast(false, true).Contains("irplane"), MainWindow.SameIpToast(true, false));
        check("Revisão H11: o toast do mesmo IP usa a regra", Src("Views", "MainWindow.Phone.cs").Contains("Toast(SameIpToast(method == Adb.Method.Airplane, hotspot), false);"), "");

        // H13. Frases pequenas que dizem o que o código faz
        check("Revisão H13: os backups guardam também o Original", xaml.Contains("The 20 newest per account are kept, and the first one (Original) for good."), "");
        check("Revisão H13: a imagem pode ter qualquer forma", !xaml.Contains("Square, 184 px or more.") && xaml.Contains("184×184 px or more. It's cropped square."), "");
        check("Revisão H13: All e None dizem o que marcam", xaml.Contains("ToolTip=\"Ticks every account shown, except the one you copy from\"") && xaml.Contains("ToolTip=\"Unticks every account, also the ones the search hides\""), "");
        check("Revisão H13: o Import diz os mesmos tipos nos dois sítios", !xaml.Contains(".txt or .cfg file") && !xaml.Contains(".txt, .csv or .cfg file") && xaml.Split(".txt, .csv, .cfg or .log").Length == 3, "");
        check("Revisão H13: Open CS2 diz quando o comando abre o CS2", !xaml.Contains("New IP + next account always opens it.") && xaml.Contains("New IP + next account opens CS2 whatever this is set to, when it logs in to another account or rejoins."), "");

        // H14. O README não diz "banned or not"
        check("Revisão H14: o README não diz 'banned or not'", !File.ReadAllText(Path.Combine(repo, "README.md")).Contains("banned or not"), "");
    }

    static void Accounts(Action<string, bool, string> check, Func<string[], string> src, string xaml)
    {
        string Src(params string[] p) => src(p);

        // H7. Uma conta escondida não é Ready
        var hid = new AccountItem(new LocalAccount("76561198000000009", "l9", "n9", false, 0), new AccountMeta { Hidden = true });
        var ok = new AccountItem(new LocalAccount("76561198000000010", "l10", "n10", false, 0), new AccountMeta());
        check("Revisão H7: uma conta escondida não está Ready", !hid.Usable && ok.Usable, "");
        check("Revisão H7: Ready first põe-na com as que não estão em rotação", MainWindow.ReadyRank(hid) == 2 && MainWindow.ReadyRank(ok) == 0, "");
        check("Revisão H7: com Show hidden, Not in rotation mostra as escondidas", Src("Views", "MainWindow.Accounts.cs").Contains("if (FOut.IsChecked == true) list = list.Where(a => !a.InRotation);"), "");
    }

    static void Toggles(Action<string, bool, string> check, Func<string[], string> src, string xaml)
    {
        string Src(params string[] p) => src(p);

        // H12. O tooltip muda com o nome do botão
        check("Revisão H12: Show all e Show less têm o seu tooltip", ChecksView.ToggleTip(false) == "Every check, also the ones that are fine" && ChecksView.ToggleTip(true) == "Only the checks that need you", "");
        check("Revisão H12: o tooltip de Checks segue o botão", Src("Views", "MainWindow.Checks.cs").Contains("ChecksToggle.ToolTip = ChecksView.ToggleTip(all);"), "");
        check("Revisão H12: o Show e o Hide de um grupo de configs têm o seu tooltip",
            new CfgGroupRow { Open = true }.ToggleTip == "Back to one line for this group" && new CfgGroupRow().ToggleTip == "Show each account of this group (Sync on or off one by one)"
            && xaml.Contains("Content=\"{Binding ToggleText}\" Click=\"CfgGroupToggle_Click\"") && xaml.Contains("ToolTip=\"{Binding ToggleTip}\""), "");
        var act = Src("Views", "MainWindow.Activity.cs");
        check("Revisão H12: o Cancel da pílula tem tooltip também a meio de uma espera",
            !act.Contains("then nothing else starts.\" : null;") && act.Contains("Stops what the app is waiting for. Nothing that already happened is undone."), "");
    }
}
