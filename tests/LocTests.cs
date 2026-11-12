using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 11 nov.: a app em pt-BR, es e ru (src/Loc.cs, src/Lang/*.json). O inglês é a chave.
static class LocTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var keys = Keys(repo);
        check("Idiomas: há textos para traduzir", keys.Count > 300, keys.Count.ToString());

        foreach (var code in new[] { "pt-BR", "es", "ru" })
        {
            var t = Loc.Load(code);
            var missing = keys.Where(k => !t.ContainsKey(k)).ToList();
            check($"Idiomas {code}: nenhuma chave em falta", missing.Count == 0, missing.Count + ": " + string.Join(" | ", missing.Take(5)));
            // os marcadores {0} {1}... iguais ao inglês
            var bad = keys.Where(k => t.TryGetValue(k, out var v) && !Same(k, v)).ToList();
            check($"Idiomas {code}: os marcadores {{0}} iguais ao inglês", bad.Count == 0, bad.Count + ": " + string.Join(" | ", bad.Take(3)));
        }

        // plurais: a forma do russo (1, 2-4, 5+)
        var ru = Loc.Load("ru");
        check("Idiomas: Fmt.Count no inglês continua igual", Loc.Lang != "en" || Fmt.Count(1, "account") == "1 account" && Fmt.Count(3, "account") == "3 accounts", "");

        // nenhum texto visível novo fora do mecanismo: Toast e avisos com literal cru
        var raw = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.Combine("src", "Privado")) || f.Contains(Path.Combine("src", "obj")) || f.Contains(Path.Combine("src", "bin"))) continue;
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"\bToast\(\s*\$?""") && !lines[i].Contains("void Toast") && !lines[i].Contains("Toast(\"Demo:"))
                    raw.Add(Path.GetFileName(f) + ":" + (i + 1));
        }
        check("Idiomas: nenhum Toast com texto cru (passa por Loc.T ou Loc.F)", raw.Count == 0, raw.Count + ": " + string.Join(" ", raw.Take(8)));
    }

    static bool Same(string en, string tr)
    {
        static string P(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x));
        return P(en) == P(tr);
    }

    static HashSet<string> Keys(string repo)
    {
        var p = Process.Start(new ProcessStartInfo("node", $"\"{Path.Combine(repo, "tools", "loc-keys.mjs")}\" --all") { RedirectStandardOutput = true, UseShellExecute = false });
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return JsonSerializer.Deserialize<string[]>(json).ToHashSet();
    }
}
