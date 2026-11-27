#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 27 nov.: o que o dono viu ao usar a 1.0.0 no PC dele (publish/plano-1-0-0/26-app-uso-e-rename.md), um teste por correção.
static class Uso27NovTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    public static void Run(Action<string, bool, string> check)
    {
        // 1. Ctrl+Shift+F9: o Cloud Conflict da Steam não deixou o CS2 abrir e a app mostrou "Opening CS2…" 2 min sem dizer porquê
        check("Uso 1: sem CS2, aos 20 s diz que a Steam pode estar à espera, só uma vez",
            MainWindow.Cs2OpenNext(19, null, false) == MainWindow.Cs2OpenStep.Wait
            && MainWindow.Cs2OpenNext(20, null, false) == MainWindow.Cs2OpenStep.SteamAsks
            && MainWindow.Cs2OpenNext(21, null, false) == MainWindow.Cs2OpenStep.Wait, "");
        check("Uso 1: sem CS2 desiste aos 150 s, não aos 120 (tempo para responder à Steam)",
            MainWindow.Cs2OpenNext(120, null, false) == MainWindow.Cs2OpenStep.Wait && MainWindow.Cs2OpenNext(150, null, false) == MainWindow.Cs2OpenStep.GiveUp, "");
        check("Uso 1: o CS2 que abre tarde (aos 100 s) tem 90 s para o menu a contar daí",
            MainWindow.Cs2OpenNext(150, 100, false) == MainWindow.Cs2OpenStep.Wait && MainWindow.Cs2OpenNext(189, 100, false) == MainWindow.Cs2OpenStep.Wait
            && MainWindow.Cs2OpenNext(190, 100, false) == MainWindow.Cs2OpenStep.Join && MainWindow.Cs2OpenNext(30, 5, true) == MainWindow.Cs2OpenStep.Join, "");
        var text = MainWindow.Cs2NotOpenText("EU #1");
        check("Uso 1: o aviso diz o que a Steam pede (Cloud Conflict, Cloud Save ou Local Save) e como parar, sem escolher por ti",
            text.Contains("Cloud Conflict") && text.Contains("Cloud Save or Local Save") && text.Contains("Answer it in Steam") && text.Contains("Cancel") && text.Contains("EU #1"), text);
        var servers = Src("Views", "MainWindow.Servers.cs");
        var wait = servers.Substring(servers.IndexOf("async Task<bool> ConnectWhenMenuReady"), 3600);
        check("Uso 1: a espera continua a deixar outro comando tomar o lugar (CommandWait) e mostra a Steam com steam://open/main",
            wait.Contains("using var wait = CommandWait();") && wait.Contains("\"Show Steam\"") && wait.Contains("steam://open/main") && !wait.Contains("i < 120"), "");

        // 2. o retângulo azul depois de Alt+Tab: o anel de foco só depois de Tab ou de uma seta, e na barra segue o mosaico
        check("Uso 2: Tab e as setas mostram o anel; Alt+Tab (Key.System), Enter e Escape não",
            MainWindow.MovesFocus(System.Windows.Input.Key.Tab) && MainWindow.MovesFocus(System.Windows.Input.Key.Down)
            && !MainWindow.MovesFocus(System.Windows.Input.Key.System) && !MainWindow.MovesFocus(System.Windows.Input.Key.Enter) && !MainWindow.MovesFocus(System.Windows.Input.Key.Escape), "");
        var theme = Src("Theme.xaml");
        var feedback = Src("Views", "MainWindow.Feedback.cs");
        check("Uso 2: os anéis seguem FocusRingShown, que a janela esconde ao ser ativada e ao clique",
            theme.Contains("<Visibility x:Key=\"FocusRingShown\">Visible</Visibility>") && Count(theme, "Visibility=\"{DynamicResource FocusRingShown}\"") == 2
            && feedback.Contains("override void OnActivated") && feedback.Contains("override void OnPreviewMouseDown") && feedback.Contains("if (MovesFocus(e.Key)) FocusRingShown(true);"), "");
        var nav = theme.Substring(theme.IndexOf("<Style x:Key=\"Nav\" TargetType=\"RadioButton\">"), 200);
        // 27 nov. à noite: a barra passou a 104 px e o mosaico a 8 px dos lados (NavTile), o anel segue o mesmo recurso
        check("Uso 2: na barra o anel é o do mosaico (o mesmo NavTile, cantos de 10), não um retângulo da linha toda",
            nav.Contains("{StaticResource NavFocusRing}") && theme.Contains("<Border Margin=\"{DynamicResource NavTile}\" CornerRadius=\"10\" BorderBrush=\"{StaticResource Accent}\""), "");

        // 3. Settings: no fim do scroll a lista da esquerda ficava em Privacy com App, Maintenance e Remove à vista (captura 3)
        var tops = new double[] { 0, 400, 800, 1200, 1600, 2000, 2300, 2700, 2950, 3060 };   // os 4 últimos nunca chegam ao topo
        double scroll = 2400, view = 720;
        check("Uso 3: no topo e a meio escolhe o grupo cujo título passou a linha de 40 px",
            MainWindow.SettingsGroupAt(0, scroll, view, tops) == 0 && MainWindow.SettingsGroupAt(1180, scroll, view, tops) == 3, "");
        check("Uso 3: no fim do scroll escolhe o último grupo, e pelo caminho cada um dos últimos tem a sua vez",
            MainWindow.SettingsGroupAt(scroll, scroll, view, tops) == tops.Length - 1
            && Enumerable.Range(0, 49).Select(k => MainWindow.SettingsGroupAt(scroll - view + k * view / 48, scroll, view, tops)).Distinct().Count() >= 4, "");
        check("Uso 3: uma página curta (menos de um ecrã de scroll) começa no primeiro grupo",
            MainWindow.SettingsGroupAt(0, 300, view, new double[] { 0, 380, 700 }) == 0 && MainWindow.SettingsGroupAt(300, 300, view, new double[] { 0, 380, 700 }) == 2, "");

        // 4. as settings que ninguém encontra: Find a setting, e os avisos com "Settings > ..." abrem-na
        var items = new List<(string, string, string, string)>
        {
            ("Turn on protection when the app starts", "Protection", "Also with Windows. It opens in the tray.", ""),
            ("Start with Windows", "App", "Opens in the notification area when you sign in.", "startup boot"),
            ("Close to the notification area", "App", "The X hides the window.", "tray"),
            ("Command sounds", "Hotkeys and sounds", "A sound when a hotkey starts and ends", ""),
            ("Atualizações automáticas", "App", "Uma vez por dia", ""),
        };
        check("Uso 4: procura pelo nome, por palavras do que faz e pelas palavras extra (startup, tray)",
            MainWindow.FindSettings("start", items).FirstOrDefault() == 1 && MainWindow.FindSettings("hotkey sound", items).SequenceEqual(new[] { 3 })
            && MainWindow.FindSettings("startup", items).SequenceEqual(new[] { 1 }), "");
        check("Uso 4: a palavra extra vem antes de uma linha que só a diz de passagem (tray: Close to the notification area primeiro)",
            MainWindow.FindSettings("tray", items).SequenceEqual(new[] { 2, 0 }), string.Join(",", MainWindow.FindSettings("tray", items)));
        check("Uso 4: sem acentos nem maiúsculas (atualizacoes), e nada quando não há",
            MainWindow.FindSettings("ATUALIZACOES", items).SequenceEqual(new[] { 4 }) && MainWindow.FindSettings("zzz", items).Count == 0 && MainWindow.FindSettings("  ", items).Count == 0, "");
        check("Uso 4: o nome que começa pela procura vem antes do que só a tem no texto",
            MainWindow.FindSettings("sound", items).First() == 3, string.Join(",", MainWindow.FindSettings("sound", items)));
        check("Uso 4: a palavra procurada tem de começar uma palavra do texto (ban não encontra urban)",
            MainWindow.FindSettings("ban", new List<(string, string, string, string)> { ("Urban map", "App", "", ""), ("Bans", "Accounts", "", "") }).SequenceEqual(new[] { 1 }), "");
        foreach (var en in new[] { "Start with Windows", "Close to the notification area", "Check for updates", "Hotkeys and sounds", "Remove the app" })
            check($"Uso 4: '{en}' tem palavras extra para quem não sabe o nome", MainWindow.SettingWords.ContainsKey(en), "");
        var names = new[] { "Privacy", "App", "Steam", "Use the app with protection off", "Hide usernames" };
        check("Uso 4: um aviso com Settings > Privacy aponta para Privacy; o nome mais comprido ganha",
            MainWindow.SettingsLinkIn("Hide usernames is on (Settings > Privacy): the key stays hidden.", names) == "Privacy"
            && MainWindow.SettingsLinkIn("Turn on Settings > Use the app with protection off.", names) == "Use the app with protection off"
            && MainWindow.SettingsLinkIn("Settings > Steam has a button that does it now", names) == "Steam", "");
        check("Uso 4: as Definições do Windows e do telemóvel não abrem as da app",
            MainWindow.SettingsLinkIn("Open Windows Settings > Privacy & security > Location.", names) == null
            && MainWindow.SettingsLinkIn("On the phone: Settings > About phone.", names) == null && MainWindow.SettingsLinkIn("Settings > Apparel", names) == null
            && MainWindow.SettingsLinkIn(null, names) == null, "");
        var xaml = Src("MainWindow.xaml");
        var find = Src("Views", "MainWindow.SettingsFind.cs");
        check("Uso 4: a caixa está sobre a lista de grupos, a lista continua lá, e Ctrl+F leva à caixa",
            xaml.Contains("<TextBox x:Name=\"SetFind\" Style=\"{StaticResource FieldBox}\" Tag=\"Find a setting\"") && xaml.Contains("<StackPanel x:Name=\"SetNav\"")
            && Count(xaml, "Style=\"{StaticResource SetNavItem}\" Click=\"SetNav_Click\"") == 12 && Src("Views", "MainWindow.Feedback.cs").Contains("e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control"), "");
        check("Uso 4: o aviso só ganha Open Settings quando não tem já um botão",
            find.Contains("if (ToastActionBtn.Visibility == Visibility.Visible || !IsLoaded) return;") && Src("Views", "MainWindow.Feedback.cs").Contains("LinkToSettings(text);"), "");

        // 5. depois de fechar, difícil de reabrir sem o .exe da instalação: menu Iniciar (sempre) e ambiente de trabalho (ao instalar)
        var tmp = Path.Combine(Path.GetTempPath(), "srt-links-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var target = Path.Combine(tmp, "Program Files", "CS2 IP Changer");
            Directory.CreateDirectory(target);
            var exe = Path.Combine(target, "CS2IPChanger.exe");
            File.WriteAllText(exe, "");
            var programs = Path.Combine(tmp, "Programs");
            var desktop = Path.Combine(tmp, "Desktop");
            check("Uso 5: o menu Iniciar ganha Pickroute.lnk para o exe instalado, e outra cópia não mexe em nada",
                Shortcuts.KeepStartMenu(exe, programs, target) == null && File.Exists(Path.Combine(programs, "Pickroute.lnk"))
                && Shortcuts.SameFile(Shortcuts.Target(Path.Combine(programs, "Pickroute.lnk")), exe)
                && Shortcuts.KeepStartMenu(Path.Combine(tmp, "Downloads", "CS2IPChanger.exe"), Path.Combine(tmp, "P2"), target) == null && !Directory.Exists(Path.Combine(tmp, "P2")), "");
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(programs, "Pickroute.lnk"));
            File.Delete(Path.Combine(programs, "Pickroute.lnk"));
            check("Uso 5: o link do menu Iniciar volta no arranque seguinte se faltar", Shortcuts.KeepStartMenu(exe, programs, target) == null && File.Exists(Path.Combine(programs, "Pickroute.lnk")), stamp.ToString());
            check("Uso 5: o ambiente de trabalho ganha um link ao instalar", Shortcuts.AddDesktop(exe, desktop) == null && Shortcuts.SameFile(Shortcuts.Target(Path.Combine(desktop, "Pickroute.lnk")), exe), "");
            // um Pickroute.lnk de outro programa e um CS2 IP Changer.lnk nosso
            var other = Path.Combine(tmp, "Other");
            Directory.CreateDirectory(other);
            Shortcuts.Make(Path.Combine(other, "Pickroute.lnk"), Path.Combine(tmp, "other.exe"));
            Shortcuts.Make(Path.Combine(other, "CS2 IP Changer.lnk"), exe);
            var gone = Shortcuts.RemoveAll(exe, new[] { programs, desktop, other });
            check("Uso 5: Remove tira os links que abrem a app (também com o nome antigo) e deixa o de outro programa",
                gone == 3 && !File.Exists(Path.Combine(programs, "Pickroute.lnk")) && !File.Exists(Path.Combine(desktop, "Pickroute.lnk"))
                && !File.Exists(Path.Combine(other, "CS2 IP Changer.lnk")) && File.Exists(Path.Combine(other, "Pickroute.lnk")), gone.ToString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        var app = Src("App.xaml.cs");
        var program = Src("Program.cs");
        check("Uso 5: o arranque da cópia instalada refaz o link, Install põe os dois e diz que põe, Remove tira-os",
            app.Contains("Shortcuts.KeepStartMenu(Environment.ProcessPath)") && program.Contains("Shortcuts.AddDesktop(r.Exe)")
            && SRT.Program.InstallText(@"C:\Users\x\Downloads", @"C:\Program Files\CS2 IP Changer", false).Contains("Start menu")
            && Src("Core", "Uninstall.cs").Contains("if (installed) Shortcuts.RemoveAll(exe);"), "");

        // 8. "como é que a pessoa sabe que a opção existe": Settings for this page ao lado do título de cada uma das 6 páginas
        var mw = Src("MainWindow.xaml");
        var tags = System.Text.RegularExpressions.Regex.Matches(mw, "Style=\"\\{StaticResource PageSettingsLink\\}\" Tag=\"(\\w+)\" Content=\"Settings for this page\" Click=\"PageSettingsLink_Click\"")
            .Select(m => m.Groups[1].Value).ToList();
        check("Uso 8: as 6 páginas têm o link, cada uma com o seu Tag, e o Log e as Settings não",
            tags.SequenceEqual(new[] { "net", "servers", "regions", "accounts", "configs", "profiles" }) && tags.All(MainWindow.PageSettingsGroups.ContainsKey), string.Join(",", tags));
        var headers = System.Text.RegularExpressions.Regex.Matches(mw, "<HeaderedContentControl (?:x:Name=\"\\w+\" )?Style=\"\\{StaticResource Group\\}\" Header=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var unknown = MainWindow.PageSettingsGroups.SelectMany(kv => kv.Value.Where(g => !headers.Contains(g)).Select(g => $"{kv.Key}: {g}")).ToList();
        check("Uso 8: cada grupo para onde um link aponta existe nas Settings", unknown.Count == 0 && headers.Count == 12, string.Join(" | ", unknown) + " / " + headers.Count);
        check("Uso 8: Connection leva à proteção, aos dados móveis, ao Change IP e às teclas; Accounts ao New IP + next account",
            MainWindow.PageSettingsGroups["net"].SequenceEqual(new[] { "Protection", "Mobile data", "Change IP", "Hotkeys and sounds" })
            && MainWindow.PageSettingsGroups["accounts"][0] == "New IP + next account", "");
        var finder = Src("Views", "MainWindow.SettingsFind.cs");
        check("Uso 8: a lista da página mostra as opções (não só os grupos), tem All groups e some ao sair das Settings",
            finder.Contains("Loc.F(\"Settings for {0}\"") && finder.Contains("Loc.T(\"All groups\")") && finder.Contains("void PageSettings_VisibleChanged")
            && mw.Contains("IsVisibleChanged=\"PageSettings_VisibleChanged\""), "");
        check("Uso 8: os passos numerados (1 a 4) contam pelo nome, e o Show e o Get key da chave não são opções",
            finder.Contains("!t.All(char.IsDigit)") && finder.Contains("Below(row).Any(x => x is TextBox or PasswordBox or ComboBox)"), "");
        foreach (var code in new[] { "pt-BR", "es", "ru", "tr" })
        {
            var t = Loc.Load(code);
            check($"Uso 8: {code} traduz o link, a linha de cima e All groups",
                t.ContainsKey("Settings for this page") && t.ContainsKey("Settings for {0}") && t.ContainsKey("All groups") && t["Settings for {0}"].Contains("{0}"), "");
        }

        // 7. tamanhos e escalas: a barra lateral numa janela baixa, a lista das Settings, e as Settings numa janela larga
        check("Uso 7: a 660 de altura a barra fica como o dono a quis (linhas de 66, 24 por baixo do logótipo)", MainWindow.NavFit(658) == (66, 24) && MainWindow.NavFit(900) == (66, 24), MainWindow.NavFit(658).ToString());
        foreach (var (h, label) in new[] { (574.0, "1366x768 a 125 %"), (514.0, "1080p a 200 %") })
        {
            var (row, gap) = MainWindow.NavFit(h);
            check($"Uso 7: em {label} as oito linhas cabem (Profiles não fica por baixo de Log)", 74 + gap + 8 * row + 25 <= h && row >= 48, $"{row} {gap}");
        }
        check("Uso 7: o mosaico segue a linha (4 px em cima e em baixo), não 58 fixos",
            Src("Theme.xaml").Contains("<Border x:Name=\"b\" Margin=\"{DynamicResource NavTile}\" Background=\"Transparent\" CornerRadius=\"10\"/>") && Src("Theme.xaml").Contains("<Thickness x:Key=\"NavTile\">8,4</Thickness>") && Src("MainWindow.xaml").Contains("SizeChanged=\"SideBar_SizeChanged\""), "");
        check("Uso 7: a lista das Settings faz scroll numa janela baixa", Src("MainWindow.xaml").Contains("<ScrollViewer x:Name=\"SetSide\" VerticalScrollBarVisibility=\"Auto\""), "");
        check("Uso 7: numa janela larga a lista e os grupos ficam juntos ao meio; enquanto enchem a página, nada muda",
            MainWindow.SettingsPad(1072) == 0 && MainWindow.SettingsPad(1148) == 0 && MainWindow.SettingsPad(1538) == 195 && MainWindow.SettingsPad(2392) == 622, MainWindow.SettingsPad(1538).ToString());

        // 6. o rename para Pickroute (fase 0): as cópias com esta versão continuam a atualizar depois de o repo mudar de nome
        static string Rel(string repo, string tag, params string[] names) =>
            "[{\"tag_name\":\"" + tag + "\",\"html_url\":\"https://github.com/" + repo + "/releases/tag/" + tag + "\",\"assets\":["
            + string.Join(",", names.Select(n => "{\"name\":\"" + n + "\",\"size\":5,\"browser_download_url\":\"https://github.com/" + repo + "/releases/download/" + tag + "/" + n + "\"}"))
            + "]}]";
        var both = Updater.PickLatest(Rel("Sepd0x/pickroute", "v1.0.1", "Pickroute.exe", "Pickroute.exe.sha256", "Pickroute.exe.sha256.sig", "CS2IPChanger.exe", "CS2IPChanger.exe.sha256", "CS2IPChanger.exe.sha256.sig"));
        check("Rename 0: do repo com o nome novo, com os dois exe, escolhe Pickroute.exe com o seu .sha256 e .sig",
            both is { Asset: "Pickroute.exe", Version: "1.0.1" } && both.ExeUrl.EndsWith("/Pickroute.exe") && both.ShaUrl.EndsWith("/Pickroute.exe.sha256")
            && both.SigUrl.EndsWith("/Pickroute.exe.sha256.sig") && both.PageUrl.Contains("/Sepd0x/pickroute/"), both?.ToString() ?? "");
        var old = Updater.PickLatest(Rel("Sepd0x/cs2-ip-changer", "v1.0.0", "CS2IPChanger.exe", "CS2IPChanger.exe.sha256", "CS2IPChanger.exe.sha256.sig"));
        check("Rename 0: uma release de hoje (só CS2IPChanger.exe, repo antigo) continua igual",
            old is { Asset: "CS2IPChanger.exe" } && old.ExeUrl.EndsWith("/cs2-ip-changer/releases/download/v1.0.0/CS2IPChanger.exe"), old?.ToString() ?? "");
        var half = Updater.PickLatest(Rel("Sepd0x/pickroute", "v1.0.2", "Pickroute.exe", "CS2IPChanger.exe", "CS2IPChanger.exe.sha256", "CS2IPChanger.exe.sha256.sig"));
        check("Rename 0: Pickroute.exe sem a assinatura não ganha ao CS2IPChanger.exe assinado", half is { Asset: "CS2IPChanger.exe" } && half.SigUrl != null, half?.ToString() ?? "");
        var alheio = Updater.PickLatest(Rel("Someone/pickroute", "v9.0.0", "Pickroute.exe", "Pickroute.exe.sha256", "Pickroute.exe.sha256.sig"));
        check("Rename 0: links de outro repo continuam recusados", alheio is { ExeUrl: null, SigUrl: null }, alheio?.ToString() ?? "");
        var redirect = Updater.FromLatestRedirect(new Uri("https://github.com/Sepd0x/pickroute/releases/tag/v1.0.1"));
        check("Rename 0: o releases/latest que já aponta para o repo novo dá links no repo novo",
            redirect is { Version: "1.0.1" } && redirect.ExeUrl == "https://github.com/Sepd0x/pickroute/releases/download/v1.0.1/CS2IPChanger.exe"
            && Updater.FromLatestRedirect(new Uri("https://github.com/Other/pickroute/releases/tag/v1.0.1")) == null, redirect?.ToString() ?? "");
        check("Rename 0: o .sha256 lê-se com o nome do exe escolhido",
            Updater.ParseSha256(new string('a', 64) + "  Pickroute.exe", "Pickroute.exe") == new string('a', 64) && Updater.ParseSha256(new string('a', 64) + "  Pickroute.exe") == null, "");
    }

    static int Count(string s, string part) { var n = 0; for (var i = s.IndexOf(part); i >= 0; i = s.IndexOf(part, i + part.Length)) n++; return n; }
}
