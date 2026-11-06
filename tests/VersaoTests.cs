using System.IO;
using System.Text.RegularExpressions;
using SRT.Core;

// 6 nov.: a numeração das versões. O csproj nunca fica atrás do CHANGELOG, a versão que o CHANGELOG já dá como lançada tem as suas
// notas (docs/release-<versão>.md, que o Release workflow publica), e as notas não existem para versões que o CHANGELOG não tem.
static class VersaoTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var csproj = File.ReadAllText(Path.Combine(repo, "src", "ServerRouteTool.csproj"));
        var version = Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
        var changelog = File.ReadAllText(Path.Combine(repo, "CHANGELOG.md"));
        var released = Regex.Matches(changelog, @"^## \[(\d+\.\d+\.\d+[^\]]*)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        check("Versão: o csproj tem uma versão SemVer", Updater.IsValid(version), version);
        check("Versão: o CHANGELOG tem a secção [Unreleased] no topo", Regex.IsMatch(changelog, @"^## \[Unreleased\]", RegexOptions.Multiline), "");
        var newest = released.FirstOrDefault();
        check("Versão: o csproj não fica atrás da última versão do CHANGELOG", newest == null || Updater.Compare(version, newest) >= 0, $"{version} contra {newest}");
        check("Versão: as secções do CHANGELOG vão da mais nova para a mais velha",
            released.Zip(released.Skip(1)).All(p => Updater.Compare(p.First, p.Second) > 0), string.Join(", ", released.Take(4)));
        if (released.Contains(version))
            check("Versão: a versão do csproj já no CHANGELOG tem as notas da release", File.Exists(Path.Combine(repo, "docs", $"release-{version}.md")), $"docs/release-{version}.md");
        var notes = Directory.GetFiles(Path.Combine(repo, "docs"), "release-*.md").Select(f => Path.GetFileNameWithoutExtension(f)["release-".Length..]).ToList();
        var stray = notes.Where(n => !released.Contains(n) && n != version).ToList();
        check("Versão: notas de release só para versões do CHANGELOG (ou a do csproj)", stray.Count == 0, string.Join(", ", stray));
    }
}
