#nullable enable
using System.IO;
using System.Text.Json;
using SRT;
using SRT.Core;

// 26 nov.: o nome novo, Pickroute (publish/plano-1-0-0/11-nome.md, 6.2). Muda o que se vê; ficam o exe, as pastas, a tarefa, as chaves
// de cifra, o endereço das estatísticas e o User-Agent. As regras "CS2 IP Changer - ..." (builds da 1.0.0 antes do nome, só em PCs de
// teste) são lidas e tiradas como as "ServerRouteTool v9".
static class PickrouteTests
{
    // os únicos textos que se veem com o nome antigo: o que tem mesmo esse nome no PC (a tarefa, a pasta, os dispositivos da Steam
    // feitos antes). 27 nov. (o dono): a linha "Formerly CS2 IP Changer" das Settings saiu; o nome antigo fica só no que não se lê
    static readonly string[] Allowed =
    {
        "A scheduled task named 'CS2 IP Changer'",
        "task \"CS2 IP Changer\"",
        "C:\\Program Files\\CS2 IP Changer",
        "or CS2 IP Changer before 1.0.0",
    };

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var keys = LocTests.Keys(repo);
        // tirados os pedaços permitidos, o nome antigo não pode sobrar em lado nenhum
        var old = keys.Where(k => Allowed.Aggregate(k, (s, a) => s.Replace(a, "")).Contains("CS2 IP Changer")).ToList();
        check("Pickroute: nenhum texto visível diz CS2 IP Changer, fora do que tem mesmo esse nome no PC", old.Count == 0, string.Join(" | ", old.Take(4)));
        check("Pickroute: a janela, o aviso e os textos dizem Pickroute, e já não há a linha Formerly",
            keys.Contains("Pickroute is already running") && keys.Contains("Remove Pickroute") && !keys.Contains("Formerly CS2 IP Changer")
            && Src(repo, "MainWindow.xaml").Contains("Title=\"Pickroute\"") && Src(repo, "Views", "NoticeWindow.xaml").Contains("Title=\"Pickroute\""), "");
        foreach (var code in new[] { "es", "pt-BR", "ru", "tr" })
        {
            var t = Loc.Load(code);
            var bad = t.Where(kv => kv.Value.Contains("CS2 IP Changer") && !Allowed.Any(a => kv.Key.Contains(a))).Select(kv => kv.Key).ToList();
            check($"Pickroute: {code} só diz CS2 IP Changer onde o inglês diz, e já não tem a linha Formerly", bad.Count == 0 && !t.ContainsKey("Formerly CS2 IP Changer"),
                string.Join(" | ", bad.Take(3)));
        }
        var tr = Loc.Load("tr");
        check("Pickroute: em turco os sufixos seguem o nome novo ('u, 'un, nunca 'ı ou 'ın)",
            tr.Values.Any(v => v.Contains("Pickroute'u")) && !tr.Values.Any(v => v.Contains("Pickroute'ı") || v.Contains("Pickroute'ın")), "");

        check("Pickroute: o que é interno fica (exe, pasta, ProgramData, tarefa, User-Agent)",
            AppIds.Name == "CS2IPChanger" && AppIds.ExeName == "CS2IPChanger.exe" && AppIds.InstallFolder == "CS2 IP Changer"
            && Autostart.TaskName == "CS2 IP Changer" && AppIds.UserAgent == "CS2IPChanger/0.1" && AppIds.DataFolder == "SRT_Dados", "");
        var util = Src(repo, "Core", "Util.cs");
        check("Pickroute: as chaves de cifra e o endereço das estatísticas não mudam",
            util.Contains("\"CS2IPChanger/Secret/v1/\"") && Src(repo, "Core", "Steam", "ProfileSwitcher.cs").Contains("\"CS2IPChanger/ProfileSession/v1/\"")
            && Src(repo, "Core", "Telemetry.cs").Contains("https://stats.cs2ipchanger.workers.dev/v1"), "");
        check("Pickroute: Installed apps, a Steam e a bandeja dizem o nome novo",
            SteamAuth.DeviceName == "Pickroute" && MainWindow.TrayTip(null) == "Pickroute"
            && Installer.UninstallEntry(@"C:\Program Files\CS2 IP Changer\CS2IPChanger.exe", "1.0.0", 1, DateTime.Today).Any(e => e.Name == "DisplayName" && (string)e.Value == "Pickroute"), "");

        // as regras: prefixo novo, e as "CS2 IP Changer - ..." lidas como antigas
        check("Pickroute: as regras começam por Pickroute e o grupo também", Firewall.RuleName == "Pickroute - kill switch" && AppIds.RulePrefix == "Pickroute", "");
        check("Pickroute: uma regra CS2 IP Changer traduz-se no nome novo, com a cauda de um temporário",
            Firewall.Current("CS2 IP Changer - kill switch") == Firewall.RuleName
            && Firewall.Current("CS2 IP Changer - IPv6 blocked (updating 0123456789ab)") == Firewall.Ipv6RuleName + " (updating 0123456789ab)", "");
        check("Pickroute: o nome novo sabe os dois antigos (v9 e CS2 IP Changer); o Apply e o Remove tiram-nos",
            Firewall.LegacyNames(Firewall.RuleName).SequenceEqual(new[] { "ServerRouteTool v9 - kill switch", "CS2 IP Changer - kill switch" })
            && Firewall.LegacyNames(Firewall.Ipv6RuleName).Contains("CS2 IP Changer - IPv6 blocked") && !Firewall.LegacyNames("Outra").Any(), "");
        check("Pickroute: IsAppRule vê CS2 IP Changer - ..., não vê CS2 IP Changer Pro nem Pickroute Pro",
            Firewall.IsAppRule("CS2 IP Changer - kill switch") && Firewall.IsLegacyName("CS2 IP Changer - kill switch")
            && !Firewall.IsAppRule("CS2 IP Changer Pro") && !Firewall.IsAppRule("Pickroute Pro") && !Firewall.IsLegacyName(Firewall.RuleName), "");
        check("Pickroute: um bloqueio CS2 IP Changer conta como bloqueio; o Has encontra-o pelo nome novo",
            Firewall.BlockLeft(new[] { "CS2 IP Changer - kill switch" }) && Firewall.Has(new[] { "CS2 IP Changer - IPv6 blocked" }, Firewall.Ipv6RuleName), "");
        var (remove, left) = Firewall.LegacyToRemove(new (string, string)[]
        {
            ("CS2 IP Changer - kill switch", "CS2 IP Changer"), (Firewall.RuleName, AppIds.RulePrefix),
            ("CS2 IP Changer - IPv6 blocked", "CS2 IP Changer"),
            ("CS2 IP Changer - TESTE restore.cmd", "CS2 IP Changer"),
        });
        check("Pickroute: a limpeza tira a CS2 IP Changer já renomeada e a que ninguém escreve; deixa a que ainda não tem a nova",
            remove.ToHashSet().SetEquals(new[] { "CS2 IP Changer - kill switch", "CS2 IP Changer - TESTE restore.cmd" }) && left == 1,
            string.Join(" | ", remove) + " / left " + left);
        // quem já tinha RulesRenamed (só os nomes v9) volta a procurar uma vez, pelas CS2 IP Changer
        var s = JsonSerializer.Deserialize<Settings>("{\"RulesRenamed\":true}")!;
        check("Pickroute: o RulesRenamed de antes não conta (é guardado com outro nome)",
            !s.RulesRenamed && JsonSerializer.Serialize(new Settings { RulesRenamed = true }).Contains("\"RulesRenamedPickroute\":true"), "");
        check("Pickroute: o restore.cmd tira os três prefixos", File.ReadAllText(Path.Combine(repo, "tools", "restore.cmd"))
            .Contains("-DisplayName 'Pickroute - *','CS2 IP Changer - *','ServerRouteTool v9*'"), "");
    }

    static string Src(string repo, params string[] parts) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(parts).ToArray()));
}
