using System.IO;
using SRT;
using SRT.Core;

// 6 nov. (ronda 2 de testes no PC do dono): os 6 problemas que ele viu ou que o Log mostrou. Cada um falhava antes da correção.
static class Ronda2Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");

        // 1. Use the app with protection off: um aviso vermelho fixo no topo e no Status
        check("Ronda 2: com a opção ligada, o topo fica vermelho", MainWindow.OffHeroKind(true) == "bad" && MainWindow.OffHeroTitle(true) == "Home IP: servers see it", "");
        check("Ronda 2: sem a opção, Protection off a cinzento como antes", MainWindow.OffHeroKind(false) == "idle" && MainWindow.OffHeroTitle(false) == "Protection off", "");
        var prot = Src("Views", "MainWindow.Protection.cs");
        check("Ronda 2: o Status usa o título e a cor da opção", prot.Contains("SetHero(OffHeroKind(_s.UseWithProtectionOff), OffHeroTitle(_s.UseWithProtectionOff),"), "");
        check("Ronda 2: mudar a opção atualiza logo o topo", Src("Views", "MainWindow.Settings.cs").Contains("UpdateQuickInfo();\n        UpdateStatusUi();   // the red \"Home IP\""), "");

        // 2. O nome do botão diz cada passo, também o rejoin
        check("Ronda 2: New IP + conta + rejoin", MainWindow.QuickName(true, "atlas", true) == ("New IP + ", " + rejoin"), "");
        check("Ronda 2: Log in + rejoin (proteção desligada com a opção)", MainWindow.QuickName(false, "Acc2", true) == ("Log in to ", " + rejoin"), "");
        check("Ronda 2: sem rejoin, o nome fica como era", MainWindow.QuickName(true, "atlas", false) == ("New IP + ", "") && MainWindow.QuickName(false, "Acc2", false) == ("Log in to ", ""), "");
        check("Ronda 2: só o rejoin chama-se Rejoin", MainWindow.QuickName(false, null, true) == ("Rejoin", ""), "");
        check("Ronda 2: só o IP com rejoin", MainWindow.QuickName(true, null, true) == ("New IP", " + rejoin"), "");
        var acc = Src("Views", "MainWindow.Accounts.cs");
        check("Ronda 2: o rejoin só entra no nome com um último servidor na lista", acc.Contains("var rejoin = c.Rejoin && _s.LastServer != null && _servers.Any(x => x.Key == _s.LastServer);"), "");
        check("Ronda 2: o topo e Accounts mostram o fim do nome", acc.Contains("SideQuickTail.Text = tail;") && acc.Contains("+ tail;"), "");
        var servers = Src("Views", "MainWindow.Servers.cs");
        check("Ronda 2: o nome atualiza quando o último servidor muda ou sai da lista",
            servers.Contains("UpdateRejoinBtn();\n        UpdateQuickInfo();") && servers.Contains("UpdateQuickInfo();   // the last server may be gone"), "");
        var kick = Src("Views", "MainWindow.Kick.cs");
        check("Ronda 2: o Log não diz que só muda a conta quando também volta ao servidor", !kick.Contains("only the account changes") && !kick.Contains("Switching the account only"), "");

        // 3. Show hidden alterna com Hide hidden
        check("Ronda 2: Show hidden e Hide hidden", MainWindow.HiddenBtnText(false) == "Show hidden" && MainWindow.HiddenBtnText(true) == "Hide hidden", "");
        check("Ronda 2: o botão alterna, não devolve as contas para sempre", acc.Contains("_showHidden = !_showHidden && _accHidden > 0;") && !acc.Contains("void UnhideAll_Click"), "");
        var meta = new Dictionary<string, AccountMeta> { ["1"] = new AccountMeta(), ["2"] = new AccountMeta { Hidden = true } };
        var locals = new[] { new LocalAccount("1", "a", "A", false, 0), new LocalAccount("2", "b", "B", false, 0) };
        var hiddenItems = new List<AccountItem>();
        var items = MainWindow.AccountItems(locals, meta, null, false, out var hidden, hiddenItems);
        check("Ronda 2: as escondidas ficam à parte (fora da rotação, de Configs e de Profiles)", items.Count == 1 && hidden == 1 && hiddenItems.Count == 1 && hiddenItems[0].SteamId == "2", "");
        check("Ronda 2: uma escondida mostra-se a meio tom e o menu devolve-a", hiddenItems[0].ListOpacity < 1 && hiddenItems[0].HideMenuText == "Show in list" && items[0].HideMenuText == "Hide from list" && items[0].ListOpacity == 1, "");
        check("Ronda 2: a lista junta as escondidas só com Show hidden", acc.Contains("IEnumerable<AccountItem> list = _showHidden ? _accounts.Concat(_accHiddenItems) : _accounts;"), "");
        check("Ronda 2: uma escondida à vista não aparece In rotation", !hiddenItems[0].InRotation && hiddenItems[0].QuickTip.StartsWith("Hidden:") && items[0].InRotation, "");
        check("Ronda 2: na largura mínima os filtros de Accounts passam a duas linhas (o Refresh e o Show hidden ficavam fora)",
            Src("MainWindow.xaml").Contains("<WrapPanel Grid.Column=\"1\" Orientation=\"Horizontal\" Margin=\"12,0,0,0\" VerticalAlignment=\"Top\">\n                                        <RadioButton x:Name=\"FAll\"")
            && Src("MainWindow.xaml").Contains("<TextBlock x:Name=\"AccCount\" Foreground=\"{StaticResource Faint}\" VerticalAlignment=\"Center\" Margin=\"12,0,12,0\"/>\n                                    </WrapPanel>"), "");
        check("Ronda 2: as escondidas à vista não contam no \"7 of 7\"", acc.Contains("$\"{_accShown.Count(a => !a.Meta.Hidden)} of {_accounts.Count}\""), "");
        var xaml = Src("MainWindow.xaml");
        check("Ronda 2: o menu e o cartão seguem a conta escondida", xaml.Contains("Header=\"{Binding HideMenuText}\"") && xaml.Contains("Opacity=\"{Binding ListOpacity}\""), "");

        // 4. Nomes do Steam com caracteres invisíveis (tags Unicode, isoladores de direção)
        var raw = "\U000E0073⁧⁧ sepd";
        check("Ronda 2: o nome do Steam sai sem caracteres invisíveis", Steam.ShownName(raw) == "sepd", Steam.ShownName(raw) ?? "null");
        check("Ronda 2: o zero-width joiner dos emoji fica", Steam.Visible("a‍b") == "a‍b", "");
        check("Ronda 2: um nome só de invisíveis não se mostra", Steam.ShownName("⁧​") == null, "");
        check("Ronda 2: um nome normal fica igual", Steam.ShownName("Acc2") == "Acc2" && Steam.ShownName("José ★") == "José ★", "");

        // 5. Frases do Log que assumiam a proteção ligada
        check("Ronda 2: Wi-Fi do telemóvel com a proteção ligada bloqueia", MainWindow.PhoneWifiOnText("casa", true, false).EndsWith("Servers and CS2 blocked until it's off."), "");
        check("Ronda 2: Wi-Fi do telemóvel com a proteção desligada não diz blocked", MainWindow.PhoneWifiOnText(null, false, false) is string t && !t.Contains("blocked") && t.Contains("before you turn on protection"), "");
        check("Ronda 2: em No phone o Wi-Fi do telemóvel não vai ao Log", MainWindow.PhoneWifiOnText("casa", true, true) == null, "");
        check("Ronda 2: o CS2 só passa pelo telemóvel com a proteção ligada", prot.Contains("if (_s.GameMode && !_s.NoPhone && _protOn) Log.Info("), "");

        // 6. 429 das estatísticas: o servidor diz "ainda não" (a app aberta duas vezes em 10 minutos); não é um aviso no Log
        check("Ronda 2: 429 é \"ainda não\", outros códigos continuam a avisar", Telemetry.NotYet(429) && !Telemetry.NotYet(500) && !Telemetry.NotYet(403), "");
        check("Ronda 2: um 429 não escreve Warn no Log", Src("Core", "Telemetry.cs").Contains("if (!NotYet((int)resp.StatusCode)) NotSent(url,"), "");
    }
}
