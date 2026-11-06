using System.IO;
using SRT.Core;

// beta.10: contas novas sem reiniciar, nomes do Steam, a proteção que começa desligada (uma só lógica), Configs com muitas contas
// e o Phone setup com passos
static class Beta10Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        // ---------------- 1. Conta nova em todas as páginas sem reiniciar
        LocalAccount A(string id) => new(id, "login" + id[^2..], "nome" + id[^2..], false, 0);
        const string a = "76561198000000001", b = "76561198000000002", c = "76561198000000003";
        check("Contas: a lista igual ao loginusers.vdf não recarrega", !Steam.AccountsDiffer(new[] { a, b }, new[] { A(b), A(a) }, _ => false), "");
        check("Contas: uma conta nova no ficheiro recarrega", Steam.AccountsDiffer(new[] { a, b }, new[] { A(a), A(b), A(c) }, _ => false), "");
        check("Contas: uma conta que saiu do Steam recarrega", Steam.AccountsDiffer(new[] { a, b }, new[] { A(a) }, _ => false), "");
        check("Contas: uma conta escondida de propósito não conta como nova", !Steam.AccountsDiffer(new[] { a }, new[] { A(a), A(b) }, id => id == b), "");
        // A causa: o Turn off protection parava o tick, e é o tick que vê o loginusers.vdf (e o Steam, a consola do CS2, os dados)
        var repo = SecurityTests.Repo();
        var settings = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Settings.cs"));
        check("Contas: desligar a proteção já não para o tick", !settings.Contains("_tick.Stop()"), "");
        var acc = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Accounts.cs"));
        check("Contas: depois de ler as contas, Profiles e Configs atualizam se estiverem abertas",
            acc.Contains("if (PageProfiles.IsVisible) ShowProfiles();") && acc.Contains("if (PageConfigs.IsVisible) ShowConfigs();"), "");
        check("Contas: uma leitura falhada (Steam a escrever) não esvazia a lista", acc.Contains("if (_accounts.Count > 0) return;"), "");

        // ---------------- Nomes do Steam: o XML do perfil traz entidades HTML; um perfil por configurar mostra o SteamID64
        check("Nome: &lt; blank &gt; aparece como < blank >", Steam.WebText("&lt; blank &gt;") == "< blank >", Steam.WebText("&lt; blank &gt;"));
        check("Nome: &amp; e acentos", Steam.WebText("Tom &amp; Jerry") == "Tom & Jerry" && Steam.WebText("João") == "João" && Steam.WebText(null) == null, "");
        check("Nome: um SteamID64 não é nome", Steam.ShownName("76561198000000123") == null && Steam.ShownName("  ") == null && Steam.ShownName("8") == "8", "");
        var meta = new SRT.AccountMeta { Profile = new SteamProfile { Persona = "&lt; blank &gt;" } };
        check("Nome: um perfil guardado antes da beta.10 também sai decodificado",
            new SRT.AccountItem(new LocalAccount(a, "login1", "outro", false, 0), meta).Name == "< blank >", "");
        var idName = new SRT.AccountItem(new LocalAccount(a, "login1", "nightfall", false, 0), new SRT.AccountMeta { Profile = new SteamProfile { Persona = "76561198000000123" } });
        check("Nome: perfil por configurar mostra o nome do loginusers.vdf, não o ID", idName.Name == "nightfall", idName.Name);
        var noName = new SRT.AccountItem(new LocalAccount(a, "login1", "76561198000000001", false, 0), new SRT.AccountMeta());
        check("Nome: sem nome nenhum fica o login", noName.Name == "login1", noName.Name);
        var look = CommunitySession.ParseLook("<profile><steamID64>76561198000000001</steamID64><steamID><![CDATA[&lt;3 a &amp; b]]></steamID><realname><![CDATA[Ana &amp; Rui]]></realname></profile>");
        check("Nome: o Make all look like copia o nome decodificado", look.PersonaName == "<3 a & b" && look.RealName == "Ana & Rui", look.PersonaName);

        // ---------------- 4. Proteção: ligada só quando a ligas
        string[] none = Array.Empty<string>();
        check("Proteção: cada arranque começa desligada", !Guard.OnAtStart(false, none, onAtStart: false, leftOn: false, cs2Running: false, noPhone: false), "");
        check("Proteção: com a opção, liga sozinha no arranque (também com o Windows)", Guard.OnAtStart(false, none, true, false, false, false), "");
        check("Proteção: CS2 aberto com o bloqueio da última vez (Keep ou crash) fica ligada", Guard.OnAtStart(false, none, false, leftOn: true, cs2Running: true, noPhone: false), "");
        check("Proteção: o bloqueio da última vez sem CS2 aberto não a liga (desligada é desligada)", !Guard.OnAtStart(false, none, false, leftOn: true, cs2Running: false, noPhone: false), "");
        check("Proteção: No phone nunca liga", !Guard.OnAtStart(false, none, true, true, true, noPhone: true) && !Guard.OnAtStart(true, new[] { "--updated", Guard.OnArg }, false, false, false, true), "");
        check("Atualização: ligada continua ligada, desligada continua desligada",
            Guard.OnAtStart(true, new[] { "--updated", Guard.UpdateArg(true) }, false, false, false, false) && !Guard.OnAtStart(true, new[] { "--updated", Guard.UpdateArg(false) }, true, false, false, false), "");
        check("Atualização vinda da beta.9: sem nada estava protegida, --standby ainda esperava (fica desligada), --off desligada",
            Guard.OnAtStart(true, new[] { "--updated" }, false, false, false, false) && !Guard.OnAtStart(true, new[] { "--updated", "--standby" }, false, false, false, false)
            && !Guard.OnAtStart(true, new[] { "--updated", "--off" }, false, false, false, false), "");
        check("Atualização: os argumentos que a cópia nova recebe", Guard.UpdateArg(true) == "--on" && Guard.UpdateArg(false) == Updater.OffArg && Updater.StandbyArg == "--standby", "");
        check("Desligada: o resto do PC fica em casa também em Whole PC (mede o IP de casa, achado 7)",
            Guard.PcOffPhoneNext(true, "servers", "off", new Adapter { Index = 21, IsPhone = true }, true, -1, true, 999) == Guard.PcOffPhoneStep.Apply
            && Guard.NeedsPcOffPhone("off", true, 21, 21, true), "");
        check("Desligada e No phone não protegem nada", new GuardReport { EffectiveMode = "off" }.Unprotected && new GuardReport { EffectiveMode = "home" }.Unprotected
            && !new GuardReport { EffectiveMode = "servers" }.Unprotected && !new GuardReport { EffectiveMode = "all" }.Unprotected, "");
        var prot = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Protection.cs"));
        check("Proteção: uma queda do telemóvel com ela ligada bloqueia, não a desliga", !prot.Contains("_protOn = false"), "");
        check("Status: um só botão principal (Turn on quando desligada; depois o rejoin ou o Change IP)",
            SRT.MainWindow.StatusPrimary(false, false, true) == ("Btn", "Btn") && SRT.MainWindow.StatusPrimary(true, false, true) == ("Primary", "Btn")
            && SRT.MainWindow.StatusPrimary(true, false, false) == ("Btn", "Primary") && SRT.MainWindow.StatusPrimary(false, true, false) == ("Btn", "Primary"), "");
        var win = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Window.cs"));
        check("Fechar com a proteção desligada limpa a rota de teste mesmo com Keep", win.Contains("if (!_s.CleanupOnExit && _protOn)"), "");
        var servers = File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Servers.cs"));
        check("Join com a proteção desligada pergunta e liga antes de entrar", servers.Contains("\"Turn on and join\"") && servers.Contains("TurnProtectionOn(ask: false)"), "");
        var xaml = File.ReadAllText(Path.Combine(repo, "src", "MainWindow.xaml"));
        check("Status tem o botão Turn on protection e Settings a opção de ligar no arranque",
            xaml.Contains("x:Name=\"TurnOnBtn\"") && xaml.Contains("Click=\"TurnOn_Click\"") && xaml.Contains("x:Name=\"ProtectOnStartBox\""), "");
        check("Proteção: a opção de ligar no arranque vem desligada", !new SRT.Settings().ProtectOnStart, "");

        // Regions não depende do cabo: o ping mede-se em casa com a proteção desligada, em No phone ou sem telemóvel ligado
        check("Regions: com a proteção ligada o ping é pelo telemóvel (nunca do IP de casa)", !SRT.MainWindow.PingOnHome(false, true), "");
        check("Regions: desligada ou No phone, mede em casa (nada desativado por falta de cabo)", SRT.MainWindow.PingOnHome(false, false) && SRT.MainWindow.PingOnHome(true, true), "");
        // Revisão: sem servidores na lista só há o lock do CS2 e as regras de Whole PC; contam como bloqueio deixado da última vez
        check("Arranque: o lock do CS2 ou o Whole PC da última vez contam como bloqueio (não só o kill switch)",
            Firewall.BlockLeft(new[] { Firewall.Cs2RuleName }) && Firewall.BlockLeft(new[] { Firewall.Ipv6RuleName }) && Firewall.BlockLeft(new[] { Firewall.RuleName })
            && !Firewall.BlockLeft(new[] { Firewall.EchoRuleName }) && !Firewall.BlockLeft(new[] { Firewall.RegionsRuleName }) && !Firewall.BlockLeft(null), "");
        var guardSrc = File.ReadAllText(Path.Combine(repo, "src", "Core", "Network", "Guard.cs"));
        check("Desligada com a Firewall do Windows desligada: as regras antigas saem quando ela volta", guardSrc.Contains("(fwProblem != null ? \"|fw-off\" : \"\")"), "");

        // ---------------- 2. Configs com muitas contas: iguais numa linha, a main e a conta em uso à parte, pesquisa sem grupos
        var next = 1000;
        SRT.CfgAccountRow Row(string n, string key, bool main = false, bool active = false, bool sync = true, bool pending = false)
        {
            var item = new SRT.AccountItem(new LocalAccount("7656119800000" + next++, n + "_login", n, active, 0), new SRT.AccountMeta()) { Active = active };
            return new SRT.CfgAccountRow { Acc = item, GroupKey = key, IsMain = main, Pending = pending, Sync = sync };
        }
        var xh = "length 11 · gap 5 · thick 3 · #00FF00 · outline";
        var rows = new List<SRT.CfgAccountRow> { Row("main", xh, main: true), Row("emuso", xh, active: true), Row("outra", "size 4"), Row("parada", xh, sync: false) };
        for (var i = 0; i < 26; i++) rows.Add(Row("alt" + i, xh));
        rows.Add(Row("nova1", SRT.CfgAccountRow.NoConfig)); rows.Add(Row("nova2", SRT.CfgAccountRow.NoConfig)); rows.Add(Row("nova3", SRT.CfgAccountRow.NoConfig));
        var shown = SRT.MainWindow.CfgDisplay(rows, "", new HashSet<string>(), "main");
        var groups = shown.OfType<SRT.CfgGroupRow>().ToList();
        check("Configs: 26 contas com a mira da main numa linha só", groups.Any(g => g.Rows.Count == 26 && g.Title == "26 accounts with the same crosshair as main"), string.Join(" | ", groups.Select(g => g.Title)));
        check("Configs: a main e a conta em uso vêm primeiro e à parte", shown[0] is SRT.CfgAccountRow m0 && m0.IsMain && shown[1] is SRT.CfgAccountRow m1 && m1.Active, "");
        check("Configs: as que diferem (outra mira, Sync desligado) ficam à vista", shown.OfType<SRT.CfgAccountRow>().Any(r => r.Name == "outra") && shown.OfType<SRT.CfgAccountRow>().Any(r => r.Name == "parada"), "");
        check("Configs: as sem config num grupo, depois das miras", groups.Last().IsEmptyGroup && groups.Last().Rows.Count == 3, "");
        check("Configs: 35 contas cabem em 6 linhas", shown.Count == 6, shown.Count.ToString());
        var opened = SRT.MainWindow.CfgDisplay(rows, "", new HashSet<string> { xh }, "main");
        check("Configs: Show abre o grupo com cada conta", opened.Count == 6 + 26 && opened.OfType<SRT.CfgGroupRow>().First(g => g.Key == xh).ToggleText == "Hide", opened.Count.ToString());
        var found = SRT.MainWindow.CfgDisplay(rows, "ALT1", new HashSet<string>(), "main");
        check("Configs: a pesquisa mostra as contas, sem grupos", found.All(o => o is SRT.CfgAccountRow) && found.Count == 11, found.Count.ToString());
        check("Configs: 2 iguais não fazem grupo", SRT.MainWindow.CfgDisplay(new[] { Row("a", "x"), Row("b", "x") }, "", new HashSet<string>(), null).All(o => o is SRT.CfgAccountRow), "");

        // ---------------- 3. Propostas da passagem visual
        // P1 (Servers) e P2 (Accounts) não aprovadas: ficam como na beta.9
        check("Regions: Only this region aparece na linha por baixo do rato ou na clicada, e o cabeçalho diz como",
            xaml.Contains("Just one region? Point at it: Only this region") && xaml.Contains("{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"), "");
        check("Profiles: a linha da sessão e o Sign out no mesmo sítio, o tooltip guarda a linha",
            xaml.Contains("ToolTip=\"{Binding SignInTip}\"") && new SRT.ProfileRow(new SRT.AccountItem(new LocalAccount(a, "l", "n", false, 0), new SRT.AccountMeta())) { SignedIn = true, Session = "Signed in · 9 d left" }.SignInTip.StartsWith("Signed in · 9 d left."), "");
        check("Profiles: sessão cinzenta se está bem, âmbar a acabar (menos de 14 d) ou expirada",
            SRT.ProfileRow.SessionLevel(true, 187, false) == "ok" && SRT.ProfileRow.SessionLevel(true, 9, false) == "warn" && SRT.ProfileRow.SessionLevel(false, null, true) == "warn"
            && SRT.ProfileRow.SessionLevel(false, null, false) == "none", "");
        LogEntry L(LogLevel lv, string t) => new() { Level = lv, Text = t };
        check("Log: filtro por tipo e pesquisa", SRT.MainWindow.LogShows(L(LogLevel.Warn, "x"), "warn", "") && !SRT.MainWindow.LogShows(L(LogLevel.Info, "x"), "warn", "")
            && SRT.MainWindow.LogShows(L(LogLevel.Error, "x"), "error", "") && !SRT.MainWindow.LogShows(L(LogLevel.Warn, "x"), "error", "")
            && SRT.MainWindow.LogShows(L(LogLevel.Info, "Steam dropped"), "all", "steam") && !SRT.MainWindow.LogShows(L(LogLevel.Info, "Config"), "all", "steam"), "");

        // ---------------- 5. Phone setup: passos que se verificam sozinhos, verde ou vermelho, com o botão quando a app consegue
        var nada = PhoneSetup.Steps(new PhoneSetupState { AdbTool = false, AdbState = "none" });
        check("Setup: sem a ferramenta, o 1.º passo é instalá-la (com botão)", PhoneSetup.Current(nada) is { Step: PhoneStep.NoAdbTool, Action: "Install" }, "");
        var cabo = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none" });
        check("Setup: sem cabo, o cabo a vermelho e a depuração à espera (cinzenta)",
            PhoneSetup.Current(cabo)?.Step == PhoneStep.NoUsb && cabo.First(i => i.Step == PhoneStep.NoUsbDebugging).State == PhoneSetup.StepState.Waiting, "");
        var dep = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new PhoneUsbState(true, null, "Google") });
        check("Setup: cabo ligado sem depuração USB, os passos dela", PhoneSetup.Current(dep) is { Step: PhoneStep.NoUsbDebugging, Action: null } c1 && c1.Detail.Contains("Build number"), "");
        var aut = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "unauthorized", Usb = new PhoneUsbState(true, null, "Google") });
        check("Setup: à espera de Allow no telemóvel", PhoneSetup.Current(aut)?.Step == PhoneStep.Unauthorized, "");
        var teth = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device" });
        check("Setup: tethering desligado, a app liga-o (botão Turn on)", PhoneSetup.Current(teth) is { Step: PhoneStep.TetherOff, Action: "Turn on" }, "");
        var wifi = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true, WifiOn = true });
        check("Setup: Wi-Fi do telemóvel ligado, a app desliga-o (botão Turn off)", PhoneSetup.Current(wifi) is { Step: PhoneStep.WifiOn, Action: "Turn off" }, "");
        var pronto = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true });
        check("Setup: tudo verde = pronto", PhoneSetup.Current(pronto) == null && pronto.All(i => i.State == PhoneSetup.StepState.Ok) && pronto.Count == 6, "");
        var pc = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true, PcOnPhone = true });
        check("Setup: o resto do PC no telemóvel aparece com o botão", PhoneSetup.Current(pc) is { Step: PhoneStep.PcOnPhone, Action: "Keep it home" }, "");
        var hs = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true }, ssid: "Pixel");
        check("Setup: hotspot sem cabo não pede tethering nem Wi-Fi desligado", PhoneSetup.Current(hs) == null
            && hs.First(i => i.Step == PhoneStep.TetherOff).State == PhoneSetup.StepState.Ok && hs.First(i => i.Step == PhoneStep.WifiOn).State == PhoneSetup.StepState.Ok, "");
        var drv = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new PhoneUsbState(true, 28, "Samsung") }, samsung: true);
        check("Setup: driver falhado diz o código e o driver da Samsung", PhoneSetup.Current(drv) is { Step: PhoneStep.UsbDriverError } d1 && d1.Detail.Contains("code 28") && d1.Detail.Contains("Samsung USB Driver"), "");
        check("Setup: os botões da app são só passos que ela sabe fazer",
            new[] { nada, cabo, dep, aut, teth, wifi, pc }.SelectMany(x => x).Where(i => i.Action != null).All(i => i.Step is PhoneStep.NoAdbTool or PhoneStep.AdbFailed or PhoneStep.TetherOff or PhoneStep.WifiOn or PhoneStep.PcOnPhone), "");
        check("Setup: o painel tem um botão por passo e lê de 2 em 2 s",
            xaml.Contains("x:Name=\"SetupPane\"") && xaml.Contains("Click=\"SetupAction_Click\"") && File.ReadAllText(Path.Combine(repo, "src", "Views", "MainWindow.Phone.cs")).Contains("TimeSpan.FromSeconds(2)"), "");
    }
}
