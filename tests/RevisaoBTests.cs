using System.IO;
using System.Windows.Input;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área B: comandos e contas): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoBTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão B: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        var kick = Src("Views", "MainWindow.Kick.cs");
        var acc = Src("Views", "MainWindow.Accounts.cs");
        var xaml = Src("MainWindow.xaml");

        // B1. No phone sem o passo da conta: o clique já não tenta trocar o IP (o botão diz "Rejoin" e o clique fazia só um erro de IP)
        var ipRejoin = new MainWindow.SwitchSteps(true, false, true, true);
        check("Revisão B1: em No phone, sem conta, o switch não tenta trocar o IP", !MainWindow.QuickSteps(ipRejoin, noPhone: true, iphoneByHand: false).Ip, "");
        check("Revisão B1: em No phone, com conta, também não", !MainWindow.QuickSteps(new(true, true, true, true), true, false).Ip, "");
        check("Revisão B1: com o telemóvel, o IP fica", MainWindow.QuickSteps(ipRejoin, false, false).Ip && MainWindow.QuickSteps(new(true, true, true, true), false, false).Ip, "");
        check("Revisão B1: os outros passos ficam como estão", MainWindow.QuickSteps(new(true, true, true, false), true, false) == new MainWindow.SwitchSteps(false, true, true, false), "");
        check("Revisão B1: o nome e o clique usam os mesmos passos", acc.Contains("var c = QuickChoice;") && acc.Contains("var steps = QuickChoice;")
            && acc.Contains("RunSwitch(steps, from, ipLeftOut: SwitchChoice.Ip && !steps.Ip)") && !acc.Contains("RunSwitch(SwitchChoice, from)"), "");
        check("Revisão B1: o rejoin do botão Status em No phone continua desligado de propósito",
            MainWindow.RejoinTip(true, "1.2.3.4:27015", true, false, false, "F10") == "Off in the no phone mode: there is no IP to change."
            && !MainWindow.RejoinEnabled(false, true, true, true, true), "");

        // B2. iPhone: o nome do switch tira o IP com a conta (o clique só troca a conta) e o rejoin fica ativo pelo clique
        check("Revisão B2: iPhone com a conta, o nome não promete New IP", !MainWindow.QuickSteps(new(true, true, true, true), false, iphoneByHand: true).Ip, "");
        check("Revisão B2: iPhone sem a conta, o IP fica (Change IP guiado)", MainWindow.QuickSteps(ipRejoin, false, iphoneByHand: true).Ip, "");
        check("Revisão B2: o toast do iPhone diz para usar Change IP", kick.Contains("\"The IP stays the same. The iPhone changes its IP by hand: use Change IP.\""), "");
        check("Revisão B2: o rejoin fica ativo com o iPhone ligado", kick.Contains("_phone?.Ready == true || IphoneByHand && _last?.Egress != null")
            && MainWindow.RejoinEnabled(busy: false, lastInList: true, noPhone: false, canChangeIp: true, homeNoBan: false), "");

        // B3. O nome do comando no topo e no Log é o do botão; o rejoin sem IP chama-se Rejoin
        check("Revisão B3: o switch corre com o nome do botão", acc.Contains("return RunCommand(_quickName, \"starting…\"") && !acc.Contains("RunCommand(\"New IP + next account\""), "");
        check("Revisão B3: o nome inteiro, sem o corte do botão", acc.Contains("_quickName = head + (who ?? \"\") + tail;"), "");
        check("Revisão B3: rejoin pela ligação de casa chama-se Rejoin", MainWindow.RejoinName(noPhone: false, protOn: false, useWithOff: true) == "Rejoin", "");
        check("Revisão B3: com a proteção ligada é Change IP and rejoin", MainWindow.RejoinName(false, true, true) == "Change IP and rejoin" && MainWindow.RejoinName(false, false, false) == "Change IP and rejoin", "");
        check("Revisão B3: o Change IP and rejoin usa esse nome", kick.Contains("RunCommand(RejoinName(_s.NoPhone, _protOn, _s.UseWithProtectionOff), \"starting…\"") && !kick.Contains("RunCommand(\"Change IP and rejoin\""), "");
        check("Revisão B3: com menos de 2 contas, o aviso não fala do New IP", acc.Contains("\"Switching account needs at least 2 saved accounts. Add one on Accounts.\"")
            && !acc.Contains("\"New IP + next account needs at least 2"), "");

        // B4. Proteção desligada com a opção: o rejoin está ativo sem telemóvel e o tooltip não se contradiz
        check("Revisão B4: pela ligação de casa, ativo sem telemóvel", MainWindow.RejoinEnabled(false, true, false, canChangeIp: false, homeNoBan: true), "");
        check("Revisão B4: sem telemóvel e sem casa, desligado como antes", !MainWindow.RejoinEnabled(false, true, false, false, false), "");
        var homeTip = MainWindow.RejoinTip(false, "1.2.3.4:27015", true, protOn: false, useWithOff: true, "F10");
        check("Revisão B4: o tooltip em casa não diz que muda o IP", homeTip.StartsWith("Joins 1.2.3.4:27015 again on your home connection") && !homeTip.Contains("Changes the IP"), homeTip);
        check("Revisão B4: com a proteção ligada, o tooltip de sempre", MainWindow.RejoinTip(false, "1.2.3.4:27015", true, true, true, "F10").StartsWith("Changes the IP, then joins 1.2.3.4:27015 again"), "");
        check("Revisão B4: depois de um ban em casa, o botão segue as regras do clique", kick.Contains("OnHome && !BannedOnLast()"), "");

        // B5. Último servidor fora da lista: o rejoin pára antes de mudar o IP (o switch com o IP continua a mudá-lo)
        check("Revisão B5: o rejoin desliga-se sem o servidor na lista", !MainWindow.RejoinEnabled(false, lastInList: false, false, true, true), "");
        var gone = MainWindow.RejoinTip(false, "1.2.3.4:27015", false, true, false, "F10");
        check("Revisão B5: o tooltip diz que o servidor saiu da lista", gone.StartsWith("1.2.3.4:27015 is no longer in your list") && !gone.Contains("Changes the IP"), gone);
        var cir = kick[kick.IndexOf("Task ChangeIpAndRejoin(string from)")..];
        check("Revisão B5: Change IP and rejoin procura o servidor antes de começar",
            cir.IndexOf("if (!_servers.Any(x => x.Key == _s.LastServer))") is var i5 && i5 > 0 && i5 < cir.IndexOf("RunCommand("), "");
        check("Revisão B5: o switch com o IP e sem servidor continua a trocar o IP", kick.Contains("if (!steps.Ip && !steps.Account) { Toast(why, false); return false; }"), "");
        check("Revisão B5: o tooltip do switch só promete o rejoin com servidor", acc.Contains("if (rejoin) steps.Add($\"joins {_s.LastServer} again\");") && !acc.Contains("if (c.Rejoin) steps.Add("), "");
        check("Revisão B5: o porquê do rejoin que não há", MainWindow.NoRejoinWhy(null).StartsWith("No last server yet") && MainWindow.NoRejoinWhy("x:1") == "The last server (x:1) is no longer in your list.", "");

        // B6. Log in com o CS2: com a proteção desligada (sem a opção) não abre, esteja o CS2 lock ligado ou não
        check("Revisão B6: proteção desligada, CS2 lock ligado: não abre", !MainWindow.OpensCs2OnLogin(protOn: false, lockCs2: true, onHome: false, egress: false, distrust: false), "");
        check("Revisão B6: proteção desligada, CS2 lock desligado: não abre", !MainWindow.OpensCs2OnLogin(false, false, false, false, false), "");
        check("Revisão B6: em casa de propósito, abre", MainWindow.OpensCs2OnLogin(false, true, true, false, false) && MainWindow.OpensCs2OnLogin(false, false, true, false, false), "");
        check("Revisão B6: proteção ligada com o lock, abre; sem lock, só com o telemóvel de confiança",
            MainWindow.OpensCs2OnLogin(true, true, false, false, false) && MainWindow.OpensCs2OnLogin(true, false, false, true, false)
            && !MainWindow.OpensCs2OnLogin(true, false, false, false, false) && !MainWindow.OpensCs2OnLogin(true, false, false, true, true), "");
        check("Revisão B6: o aviso sai depois do login, não antes",
            acc.IndexOf("if (noCs2 != null) Toast(") > acc.IndexOf("if (state != Steam.LoginState.Target)") && !acc.Contains("Switched account but did not open CS2"), "");

        // B7. A conta em uso escondida: o ban marca-se nela e um kick não recarrega a lista
        var metaB7 = new Dictionary<string, AccountMeta> { ["1"] = new AccountMeta(), ["2"] = new AccountMeta { Hidden = true } };
        var localsB7 = new[] { new LocalAccount("1", "a", "A", false, 0), new LocalAccount("2", "b", "B", false, 0) };
        var hiddenB7 = new List<AccountItem>();
        var shownB7 = MainWindow.AccountItems(localsB7, metaB7, "2", false, out _, hiddenB7);
        check("Revisão B7: a escondida em uso é a conta ativa", MainWindow.ActiveIn("2", shownB7, hiddenB7)?.SteamId == "2", "");
        check("Revisão B7: Steam numa conta que não está em lado nenhum: nenhuma", MainWindow.ActiveIn("3", shownB7, hiddenB7) == null, "");
        check("Revisão B7: sem id, a marcada em uso", MainWindow.ActiveIn(null, shownB7, hiddenB7)?.SteamId == "2", "");
        check("Revisão B7: ActiveAccount procura também nas escondidas", kick.Contains("ActiveIn(_demo ? null : Steam.InUseSteamId(), _accounts, _accHiddenItems)"), "");
        check("Revisão B7: as escondidas continuam fora da rotação", acc.Contains("return _accounts.Where(a => a.Usable && a.SteamId != active"), "");

        // B8. O toast do ban só promete o que a tecla faz agora
        check("Revisão B8: o toast de sempre fica igual", MainWindow.BanToast(" for 30 min", "F10", "atlas") == "Banned for 30 min. Press F10 to rejoin with a new IP as atlas.", "");
        var unmarked = MainWindow.BanToast(" for 30 min", "F10", "atlas", marked: false, ipChanges: true, asksProtection: false);
        check("Revisão B8: sem marca, não diz que volta como outra conta", !unmarked.Contains("as atlas") && unmarked.Contains("would rejoin with it"), unmarked);
        var sameIp = MainWindow.BanToast("", "F10", "atlas", marked: true, ipChanges: false);
        check("Revisão B8: sem IP novo (No phone), não promete IP novo", !sameIp.Contains("new IP") && sameIp.Contains("as atlas"), sameIp);
        var asks = MainWindow.BanToast("", "F10", "atlas", marked: true, ipChanges: true, asksProtection: true);
        check("Revisão B8: proteção desligada, diz para a ligar primeiro", asks.StartsWith("Banned. Turn on protection in the app, then press F10"), asks);
        check("Revisão B8: o toast usa a marca, o IP e a proteção de agora",
            kick.Contains("marked: acc != null, ipChanges: SwitchChangesIp,") && kick.Contains("asksProtection: SwitchChangesIp && IpStep(_protOn, _s.UseWithProtectionOff, afterBan: true) != IpWhenOff.Change"), "");
        var askNp = MainWindow.BanQuestion(false, "x:1", false, "", "atlas", home: true, ipChanges: false);
        check("Revisão B8: a pergunta em No phone não fala de IP novo", askNp.EndsWith("Log in to atlas and rejoin?") && !askNp.Contains("Get a new IP"), askNp);
        check("Revisão B8: a pergunta com o telemóvel fica igual", MainWindow.BanQuestion(false, "x:1", false, " for 1 h", "atlas", false, true)
            == "CS2 says you were banned from x:1 for 1 h. The ban is on your IP and on the account, so a new IP alone is not enough.\n\nGet a new IP, log in to atlas and rejoin?", "");
        check("Revisão B8: uma só regra para o IP no switch com conta", kick.Contains("if (ip && steps.Account && !SwitchChangesIp)")
            && kick.Contains("bool SwitchChangesIp => _demo || !_s.NoPhone && _adb != null && _phone?.Ready == true;"), "");

        // B9. Em casa de propósito (No phone, ou proteção desligada com a opção), os textos não falam do telemóvel
        check("Revisão B9: No Steam logon em casa", !MainWindow.NoSteamLogonToast(true).Contains("phone") && MainWindow.NoSteamLogonToast(false).Contains("through the phone"), "");
        check("Revisão B9: timeout em casa", !MainWindow.TimeoutToast(never: false, home: true).Contains("phone") && MainWindow.TimeoutToast(false, true).EndsWith("check your internet.")
            && MainWindow.TimeoutToast(false, false).Contains("phone's signal"), "");
        check("Revisão B9: bloqueio de VPN em casa fala do IP de casa", MainWindow.VpnToast(true, false).StartsWith("The server blocked your home IP") && !MainWindow.VpnToast(true, true).Contains("New IP")
            && MainWindow.VpnToast(true, true).Contains("Game only") && MainWindow.VpnToast(false, false).Contains("the phone's IP"), "");
        check("Revisão B9: a pergunta da VPN em casa", MainWindow.BanQuestion(true, "x:1", false, "", "atlas", home: true, ipChanges: true).StartsWith("x:1 took your home IP for a VPN"), "");
        check("Revisão B9: servidor novo em casa não promete o telemóvel", !MainWindow.AddJoinedText("1.2.3.4:27015", home: true).Contains("phone")
            && MainWindow.AddJoinedText("1.2.3.4:27015").Contains("through the phone"), "");
        check("Revisão B9: o Join banido em casa não diz que muda o IP", !MainWindow.BanJoinTip("F9", true).Contains("changes the IP") && MainWindow.BanJoinTip("F9", false).Contains("changes the IP"), "");
        check("Revisão B9: os textos usam OnHome", kick.Contains("Toast(NoSteamLogonToast(OnHome)") && kick.Contains("Toast(TimeoutToast(never, OnHome)") && kick.Contains("VpnToast(OnHome, _s.NoPhone)")
            && Src("Views", "MainWindow.Servers.cs").Contains("BanJoinTip(_s.HotkeySwitch, OnHome)"), "");
        check("Revisão B9: Settings já não diz que o rejoin só entra com a proteção confirmada", !xaml.Contains("Text=\"Only joins when protection is confirmed.")
            && xaml.Contains("on your home connection in No phone or with Use the app with protection off"), "");
        check("Revisão B9: Settings já não diz que só a conta muda", !xaml.Contains("only the account changes") && xaml.Contains("If the IP can't change, the other steps still run."), "");

        // B10. A troca de conta pergunta ao Steam antes de fechar o CS2 ou mudar o IP
        check("Revisão B10: Log in vê o Steam antes de fechar o CS2", acc.IndexOf("if (SwitchProblem(a) is string cant)") is var i10 && i10 > 0 && i10 < acc.IndexOf("await _cs2.Close(\"the account switch\")"), "");
        check("Revisão B10: o switch vê o Steam antes do IP", kick.IndexOf("SwitchProblem(next) is string cant") is var j10 && j10 > 0 && j10 < kick.IndexOf("ChangeIp(confirm: false)"), "");
        var core = Src("Core", "Steam", "SteamAccounts.cs");
        check("Revisão B10: uma só verificação no Core, usada também pelo SwitchTo", core.Contains("public static string SwitchProblem(string steamId)")
            && core.Contains("if (SwitchProblem(steamId) is string why) throw why == Linked ? new IOException(why) : new InvalidOperationException(why);"), "");

        // B11. Nada para fazer: diz o que tirou cada passo, em vez de mandar ligar passos já ligados
        var nothing = MainWindow.NothingText(ipSkipped: true, serverMissing: true);
        check("Revisão B11: com os passos ligados não manda ligar passos", !nothing.Contains("turn on at least one step") && nothing.Contains("the IP stays the same") && nothing.Contains("isn't in your list"), nothing);
        check("Revisão B11: com tudo desligado, manda escolher os passos", MainWindow.NothingText(false, false).Contains("turn on at least one step"), "");

        // B12. O retry do servidor cheio só depois de um Join da app
        var now = new DateTime(2026, 11, 6, 21, 0, 0);
        check("Revisão B12: um servidor cheio que não foi o último Join da app não tenta outra vez", !MainWindow.JoinedByApp("b:1", "a:1", now.AddSeconds(-30), now), "");
        check("Revisão B12: logo depois do Join da app, tenta", MainWindow.JoinedByApp("a:1", "a:1", now.AddSeconds(-30), now) && !MainWindow.JoinedByApp("a:1", "a:1", now.AddMinutes(-4), now), "");
        check("Revisão B12: decide antes de mudar o último servidor", kick.IndexOf("var byApp = JoinedByApp(") is var i12 && i12 > 0 && i12 < kick.IndexOf("_s.LastServer = server;")
            && kick.Contains("if (inList != null && byApp && d.Why != KickReason.ReservedSlot)"), "");
        check("Revisão B12: a marca \"last\" segue o servidor do CS2", kick.Contains("SaveSettings();\n            ShowLastServer();"), "");

        // B13. O nome do botão fica certo: a conta em uso vem do Steam, e o fim do cooldown atualiza
        check("Revisão B13: NextQuick usa a conta em uso do Steam", acc.Contains("var active = ActiveAccount()?.SteamId ?? _accounts.FirstOrDefault(a => a.Active)?.SteamId;"), "");
        check("Revisão B13: o fim do cooldown atualiza o botão", Src("MainWindow.xaml.cs").Contains("Toast($\"The cooldown for {a.Name} has ended.\", true);\n                UpdateQuickInfo();"), "");

        // B14. Hotkeys que o Windows usa
        check("Revisão B14: Alt+F4 recusada", Hotkey.Problem(ModifierKeys.Alt, Key.F4) is string p14 && p14 == "Windows uses Alt+F4 itself. Pick another combination.", Hotkey.Problem(ModifierKeys.Alt, Key.F4) ?? "null");
        check("Revisão B14: Alt+Space, Alt+Tab e Alt+Esc recusadas", Hotkey.Problem(ModifierKeys.Alt, Key.Space) != null && Hotkey.Problem(ModifierKeys.Alt, Key.Tab) != null && Hotkey.Problem(ModifierKeys.Alt, Key.Escape) != null, "");
        check("Revisão B14: Alt+F9 e Ctrl+Shift+F4 continuam a servir", Hotkey.Problem(ModifierKeys.Alt, Key.F9) == null && Hotkey.Problem(ModifierKeys.Control | ModifierKeys.Shift, Key.F4) == null
            && !Hotkey.TryParse("Alt+F4", out _, out _) && Hotkey.TryParse("Ctrl+Shift+F9", out _, out _), "");

        // B15. O nome curto não parte um emoji
        var cut = MainWindow.ShortName("abcdefgh😀xyz");
        check("Revisão B15: o nome curto não acaba em meio emoji", !char.IsSurrogate(cut[^2]) || char.IsSurrogatePair(cut[^3], cut[^2]), cut);
        check("Revisão B15: corta por letras que se veem", cut == "abcdefgh😀…" && MainWindow.ShortName("abcdefghij") == "abcdefghij" && MainWindow.ShortName("abcdefghijk") == "abcdefghi…", cut);
        check("Revisão B15: o botão usa o nome curto", acc.Contains("QuickBtn.Content = head + (who == null ? \"\" : ShortName(who)) + tail;"), "");

        // D9. O menu do ícone usa o nome do botão
        var tray = MainWindow.TrayLayout("x", false, false, "Log in to atlas + rejoin");
        check("Revisão D9: o menu do ícone diz o nome do botão", tray.Any(l => l.Key == "switch" && l.Text == "Log in to atlas + rejoin" && l.Enabled), "");
        check("Revisão D9: sem nome, o de sempre", MainWindow.TrayLayout("x", false, false).Any(l => l.Key == "switch" && l.Text == "New IP + next account"), "");
        check("Revisão D9: o menu lê o nome de agora", Src("Views", "MainWindow.Tray.cs").Contains("return TrayItems(SideState.Text, _protOn && !_s.NoPhone, !_s.NoPhone, TrayCommand, _quickName);"), "");
    }
}
