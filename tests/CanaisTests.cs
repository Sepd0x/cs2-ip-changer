using System.IO;
using System.Text.Json;
using SRT;
using SRT.Core;

// 6 nov.: dois canais de versões. Estável: o Latest do GitHub, o que o update oferece por omissão. Teste: cada versão nova sai
// primeiro como pre-release e só a recebe quem ligar Settings > App > Get test versions. Uma release puxada passa a ser draft.
static class CanaisTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");

        // A opção: desligada por omissão e num settings antigo
        check("Canais: Get test versions vem desligada", !new Settings().TestVersions, "");
        check("Canais: um settings antigo (sem o campo) fica no canal estável", !JsonSerializer.Deserialize<Settings>("{\"Mode\":\"servers\",\"CheckUpdates\":true}").TestVersions, "");
        check("Canais: a opção guarda-se", JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { TestVersions = true })).TestVersions, "");

        // A mesma lista de /releases dá a estável sem a opção e a pre-release mais nova com ela; drafts nunca
        const string U = "https://github.com/Sepd0x/cs2-ip-changer/releases/";
        string R(string tag, bool draft, bool pre) =>
            $"{{\"tag_name\":\"{tag}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(pre ? "true" : "false")},\"html_url\":\"{U}tag/{tag}\","
            + $"\"assets\":[{{\"name\":\"CS2IPChanger.exe\",\"browser_download_url\":\"{U}download/{tag}/CS2IPChanger.exe\"}}]}}";
        var list = "[" + string.Join(",", R("v0.1.0-beta.13", true, true), R("v0.1.0-beta.12", false, true), R("v0.1.0-beta.11", false, false),
            R("v0.1.0-beta.10", false, true), R("v0.1.0-beta.9", false, false), R("v0.1.0-beta.14", true, false)) + "]";
        var stable = Updater.PickLatest(list);
        check("Canais: sem a opção, a estável mais nova (nem pre-release nem draft)", stable?.Version == "0.1.0-beta.11" && stable.Test == false, stable?.Version);
        check("Canais: o canal estável é o de sempre (PickLatest sem canal = estável)", Updater.PickLatest(list, test: false)?.Version == stable?.Version, "");
        var test = Updater.PickLatest(list, test: true);
        check("Canais: com a opção, a pre-release mais nova", test?.Version == "0.1.0-beta.12" && test.Test, test?.Version);
        check("Canais: com a opção, a versão de teste traz os ficheiros dela", test?.ExeUrl == U + "download/v0.1.0-beta.12/CS2IPChanger.exe" && test.PageUrl == U + "tag/v0.1.0-beta.12", test?.ExeUrl);
        var onlyDrafts = "[" + string.Join(",", R("v0.2.0", true, false), R("v0.2.0-beta.1", true, true)) + "]";
        check("Canais: um draft nunca é oferecido, em nenhum canal", Updater.PickLatest(onlyDrafts) == null && Updater.PickLatest(onlyDrafts, test: true) == null, "");
        var stableNewer = "[" + string.Join(",", R("v0.1.0-beta.12", false, true), R("v0.1.0", false, false)) + "]";
        var t2 = Updater.PickLatest(stableNewer, test: true);
        check("Canais: com a opção, uma estável mais nova que as de teste ganha (e não se chama de teste)", t2?.Version == "0.1.0" && !t2.Test, t2?.Version);
        check("Canais: uma estável lida pelo redirect de releases/latest não é de teste",
            Updater.FromLatestRedirect(new Uri(U + "tag/v0.1.0-beta.11"))?.Test == false, "");

        // O texto do update diz quando é uma versão de teste
        check("Canais: o aviso de uma versão de teste diz que é de teste", MainWindow.UpdateToast("0.1.0-beta.12", true).StartsWith("Test version 0.1.0-beta.12 is out."), MainWindow.UpdateToast("0.1.0-beta.12", true));
        check("Canais: o aviso de uma estável fica como era", MainWindow.UpdateToast("0.1.0-beta.12", false) == "CS2 IP Changer 0.1.0-beta.12 is available. Click Update under the version number.", MainWindow.UpdateToast("0.1.0-beta.12", false));
        check("Canais: a dica do Update diz que é de teste", MainWindow.UpdateTip("0.1.0-beta.12", true).StartsWith("Test version 0.1.0-beta.12 is available.")
            && MainWindow.UpdateTip("0.1.0-beta.12", false).StartsWith("Version 0.1.0-beta.12 is available."), MainWindow.UpdateTip("0.1.0-beta.12", true));
        check("Canais: a pergunta do Update diz que é de teste", MainWindow.UpdateWhat("0.1.0-beta.12", true) == "test version 0.1.0-beta.12" && MainWindow.UpdateWhat("0.1.0-beta.12", false) == "version 0.1.0-beta.12", "");

        // A app usa o canal da opção nas duas procuras, e lembra-se se o que viu era de teste
        var upd = Src("Views", "MainWindow.Update.cs");
        check("Canais: a procura diária e o Update usam o canal da opção",
            upd.Contains("r = await Updater.Latest(_s.TestVersions, cts.Token);") && upd.Contains("_update = await Updater.Latest(_s.TestVersions, cts.Token) ?? _update;"), "");
        check("Canais: a app guarda se a versão vista é de teste", upd.Contains("_s.UpdateLatestTest = r?.Test == true;"), "");
        check("Canais: desligar a opção esquece uma versão de teste já vista e procura a estável",
            upd.Contains("if (!_s.TestVersions && _s.UpdateLatestTest) { _s.UpdateLatest = null; _s.UpdateLatestTest = false; _update = null; }"), "");

        // Settings > App > Get test versions
        var xaml = Src("MainWindow.xaml");
        var app = xaml[xaml.IndexOf("Header=\"App\"")..xaml.IndexOf("Header=\"Maintenance\"")];
        check("Canais: o interruptor está em Settings > App, depois de Check for updates",
            app.Contains("x:Name=\"TestVersionsBox\" Style=\"{StaticResource Switch}\" Click=\"TestVersions_Click\"") && app.IndexOf("TestVersionsBox") > app.IndexOf("CheckUpdatesBox"), "");
        check("Canais: o nome e a linha cinzenta", app.Contains("Text=\"Get test versions\"") && app.Contains("Text=\"New versions before they are fully tested. Off: only stable versions.\""), "");
        check("Canais: o grupo App tem no máximo 7 coisas", System.Text.RegularExpressions.Regex.Matches(app, "<(CheckBox|Button|TextBox|ComboBox|PasswordBox)\\b").Count <= 7, "");
        check("Canais: o Settings mostra o valor guardado", Src("Views", "MainWindow.Settings.cs").Contains("TestVersionsBox.IsChecked = _s.TestVersions;"), "");
        check("Canais: o DESIGN tem a opção no grupo App", File.ReadAllText(Path.Combine(repo, "docs", "DESIGN.md")).Contains("| App | `LanguageBox`, `StartWithWindowsBox`, `CloseToTrayBox`, `CheckUpdatesBox`, `TestVersionsBox` |"), "");

        // Telemetria: conta a opção quando sai do default, como UseWithProtectionOff
        check("Canais: a telemetria conta Get test versions ligada", Telemetry.SettingNames.Contains("TestVersions")
            && Telemetry.Changed(new Settings { TestVersions = true }, new Settings()).Contains("TestVersions") && !Telemetry.Changed(new Settings(), new Settings()).Contains("TestVersions"), "");

        // O RELEASING: pre-release primeiro, estável só depois das rondas; puxar = draft
        var rel = File.ReadAllText(Path.Combine(repo, "docs", "RELEASING.md"));
        check("Canais: o RELEASING publica como pre-release e só depois tira o pre-release",
            rel.Contains("--prerelease") && rel.Contains("gh release edit v<version> --prerelease=false --latest"), "");
        check("Canais: o RELEASING tem a regra para passar a estável",
            rel.Contains("No version becomes stable without the test suite, the audit and the test rounds with the owner on his PC."), "");
        check("Canais: o RELEASING puxa uma release com draft, não com pre-release",
            rel.Contains("gh release edit v<version> --draft=true") && !rel.Contains("ticking that on GitHub is enough"), "");
        var key = File.ReadAllText(Path.Combine(repo, "tools", "release-key.mjs"));
        check("Canais: o release-key.mjs publica o draft como pre-release", key.Contains("['release', 'edit', tag, '--draft=false', '--prerelease']"), "");
    }
}
