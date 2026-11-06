using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SRT.Core;

// Auditoria de segurança de 6 nov. (antes da beta.11 chegar a mais gente): publish/seguranca-2026-11-06/RELATORIO.md
static class Seguranca1106Tests
{
    static readonly string[] Binary = { ".png", ".webp", ".gif", ".ico", ".jpg", ".woff2", ".wav", ".exe", ".dll", ".zip" };

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();

        // A8: nenhum ficheiro que vai para o GitHub tem o nome do utilizador do Windows deste PC (nem o nome com mais umas
        // letras), nem a pasta dele. No PC do dono apanha o nome dele; no runner do GitHub o nome é outro e o teste fica vazio.
        {
            var user = Environment.UserName;
            var files = TrackedFiles(repo);
            var hits = new List<string>();
            if (user.Length >= 3 && files != null)
            {
                var re = new Regex(@"(?<![A-Za-z0-9])" + Regex.Escape(user) + @"[a-z]{0,3}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
                foreach (var f in files.Where(f => !Binary.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    var path = Path.Combine(repo, f);
                    if (!File.Exists(path) || new FileInfo(path).Length > 4_000_000) continue;
                    var n = 0;
                    foreach (var line in File.ReadLines(path))
                    {
                        n++;
                        if (re.IsMatch(line)) { hits.Add($"{f}:{n}"); break; }
                    }
                }
            }
            check("Dono: nenhum ficheiro do git tem o nome do utilizador do Windows deste PC", files != null && hits.Count == 0,
                files == null ? "git ls-files falhou" : string.Join(", ", hits.Take(5)));
        }

        // A1 (defesa que já existe e não pode regredir): a tarefa de Start with Windows corre como admin sem UAC. O comando é o
        // caminho literal do exe instalado: uma variável (%windir%, %LOCALAPPDATA%) viria do ambiente do utilizador, que qualquer
        // programa dele muda (a falha da tarefa SilentCleanup do Windows). E não se arranca a pedido (schtasks /Run).
        {
            var exe = Path.Combine(Installer.Target, Updater.ExeName);
            var xml = Autostart.TaskXml(exe, "S-1-5-21-1-2-3-1001");
            check("Start with Windows: o comando é o caminho do exe, sem variáveis do ambiente",
                Autostart.CommandIn(xml) == exe && !xml.Contains('%') && xml.Contains("<AllowStartOnDemand>false</AllowStartOnDemand>"), Autostart.CommandIn(xml));
            check("Start with Windows: só a cópia instalada pode ter a tarefa",
                Autostart.Allowed(exe) && !Autostart.Allowed(Path.Combine(Path.GetTempPath(), Updater.ExeName)), "");
        }
    }

    static List<string> TrackedFiles(string repo)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = repo };
            foreach (var a in new[] { "ls-files", "-z" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            return p.ExitCode == 0 ? output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList() : null;
        }
        catch { return null; }
    }
}
