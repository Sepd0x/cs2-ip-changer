#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 26 nov.: Configs e Profiles na versão simplificada (desenho App v4). A linha da Sync, o estado de cada conta, o painel do
// Copy com os mesmos ticks que a Sync, e o passo 3 da Profiles (quem fica como está, quem é saltado, Apply to N accounts).
static class GuiDialV4Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        check("Configs v4: a linha da Sync diz o modo",
            MainWindow.CfgSyncModeText(CfgMode.Main, "nightfall") == "from main account nightfall" && MainWindow.CfgSyncModeText(CfgMode.Main, null) == "from a main account"
            && MainWindow.CfgSyncModeText(CfgMode.Previous, null) == "from the previous account" && MainWindow.CfgSyncModeText(CfgMode.Off, "x") == "off", "");

        var main = MainWindow.CfgStatus(CfgMode.Main, true, false, false, false, true, null, true);
        check("Configs v4: a conta principal é a fonte, sem marca", main.Text == "Main: the others copy it" && main.Level == "none", main.Text);
        var same = MainWindow.CfgStatus(CfgMode.Main, false, false, false, false, true, 0, true);
        check("Configs v4: igual à principal diz Synced", same.Text == "Synced" && same.Level == "good", same.Text);
        var differs = MainWindow.CfgStatus(CfgMode.Main, false, false, false, false, true, 3, true);
        check("Configs v4: diferente da principal, com o When do login, sincroniza no próximo login", differs.Text == "Differs · syncs at next login" && differs.Level == "warn", differs.Text);
        var noLogin = MainWindow.CfgStatus(CfgMode.Main, false, false, false, false, true, 3, false);
        check("Configs v4: sem o When do login não promete o próximo login", noLogin.Text == "Differs · 3 settings differ", noLogin.Text);
        var left = MainWindow.CfgStatus(CfgMode.Main, false, false, false, false, false, 7, true);
        check("Configs v4: Sync desligada numa conta é Left out, com quantas diferem", left.Text == "Left out · 7 settings differ" && left.Level == "off", left.Text);
        var waiting = MainWindow.CfgStatus(CfgMode.Main, false, false, false, true, true, 2, true);
        check("Configs v4: uma cópia à espera do CS2 fechar ganha a tudo menos à principal", waiting.Text == "Waiting for CS2 to close" && waiting.Level == "warn", waiting.Text);
        var off = MainWindow.CfgStatus(CfgMode.Off, false, false, false, false, true, null, true);
        check("Configs v4: com a Sync desligada nenhuma conta diz Synced", off.Text == "Sync is off", off.Text);
        var link = MainWindow.CfgStatus(CfgMode.Main, false, true, false, false, true, null, true);
        check("Configs v4: uma pasta que é link fica de fora", link.Level == "off" && link.Text.Contains("link"), link.Text);

        check("Profiles v4: quem não foi escolhido fica como está",
            MainWindow.NotPickedText(new[] { "ember", "vanta" }) == "Not picked, so they stay as they are: ember, vanta."
            && MainWindow.NotPickedText(new[] { "ember" }) == "Not picked, so it stays as it is: ember." && MainWindow.NotPickedText(Array.Empty<string>()) == null, "");
        check("Profiles v4: quem não tem sessão é saltado",
            MainWindow.SkippedText(new[] { "kestrel" }) == "kestrel is skipped until it is signed in." && MainWindow.SkippedText(Array.Empty<string>()) == null
            && MainWindow.SkippedText(new[] { "a", "b" }) == "a, b are skipped until they are signed in.", "");

        var xaml = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml"));
        foreach (var cat in new[] { "CfgCatBinds", "CfgCatCrosshair", "CfgCatGame", "CfgCatVideo", "CfgCatHud" })
            check($"Configs v4: o What it copies da Sync é o mesmo tick do Copy ({cat})", xaml.Contains($"x:Name=\"{cat}\"") && xaml.Contains($"ElementName={cat}, Mode=TwoWay"), cat);
        check("Configs v4: o menu da Sync guarda os modos e a conta principal com os nomes de antes",
            new[] { "CfgModeOff", "CfgModeMain", "CfgModePrevious", "CfgModeLatest", "CfgMainBox" }.All(n => xaml.IndexOf($"x:Name=\"{n}\"") > xaml.IndexOf("x:Name=\"CfgSyncMenu\"")), "");
        check("Profiles v4: o passo 3 mostra as mesmas linhas que o passo 2", xaml.Contains("ItemsSource=\"{Binding ItemsSource, ElementName=ProfList}\""), "ProfReview");
    }
}
