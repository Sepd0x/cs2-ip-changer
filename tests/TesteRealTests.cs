#nullable enable
using System.IO;
using SRT.Core;

// 26 nov.: a versão de testes automática (tools/teste-real.ps1, publish/plano-1-0-0/20-testes-automaticos.md). O que ela nunca
// pode fazer: apagar ficheiros, mexer em passwords ou deixar regras da app na Firewall.
static class TesteRealTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var path = Path.Combine(SecurityTests.Repo(), "tools", "teste-real.ps1");
        var ps = File.Exists(path) ? File.ReadAllText(path) : "";
        check("Teste real: o script existe e está no SMOKE.md", ps.Length > 0
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "docs", "SMOKE.md")).Contains("tools/teste-real.ps1"), "");
        check("Teste real: nunca apaga (a pasta de dados velha muda de nome)",
            !ps.Contains("Remove-Item") && !ps.Contains(" del ") && !ps.Contains("rmdir") && ps.Contains("Rename-Item $data"), "");
        check("Teste real: não toca em passwords nem lê as contas",
            !ps.Contains("Password", StringComparison.OrdinalIgnoreCase) && !ps.Contains("loginusers"), "");
        check("Teste real: acaba sempre com --restore e conta as regras pelos três prefixos da app",
            ps.Contains("} finally {") && ps.IndexOf("Restore-Test $rulesBefore", ps.IndexOf("} finally {", StringComparison.Ordinal), StringComparison.Ordinal) > 0
            && ps.Contains("-ArgumentList '--restore'") && ps.Contains($"'{AppIds.RulePrefix} - *', '{AppIds.FormerRulePrefix} - *', 'ServerRouteTool v9*'"), "");
        // 26 nov. (auditoria S2, S4): a cópia do --restore nunca fica aberta, e a tarefa Start with Windows da instalação real volta
        check("Teste real: o --restore do teste fecha a sua cópia e repõe o Start with Windows da instalação real",
            ps.Contains("if (-not $r.HasExited) { try { $r.Kill()") && ps.Contains("$taskFile = Save-Task") && ps.Contains("Put-Task $taskFile")
            && !ps.Contains("WaitForExit(120000)"), "");
        check("Teste real: o aviso das estatísticas responde Turn off (nada é enviado) e o jogo só com -Jogo",
            ps.Contains("ByName $w 'Turn off'") && ps.Contains("if ($Jogo -and $rows -gt 0)"), "");
        check("Teste real: os botões que carrega existem na janela",
            new[] { "TurnOnBtn", "TurnOffBtn", "ChangeIpBtn", "EgressIp", "SideState", "PhoneState", "QueryBtn", "ServerList", "OptStreamer", "NavSettings", "DoorLaterBtn", "TourSkipBtn" }
                .All(id => ps.Contains($"'{id}'") && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml")).Contains($"x:Name=\"{id}\"")), "");
    }
}
