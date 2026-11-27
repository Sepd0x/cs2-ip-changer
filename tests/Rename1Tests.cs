#nullable enable
using System.IO;
using SRT.Core;

// 27 nov., fase 1 do rename (publish/plano-1-0-0/28-checklist-lancamento.md secção 4): a 1.0.1 sai como Pickroute.exe, com
// CS2IPChanger.exe ao lado (o mesmo ficheiro) para as cópias antigas. Um teste por mudança.
static class Rename1Tests
{
    static string Repo(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo() }.Concat(p).ToArray())).Replace("\r\n", "\n");

    public static void Run(Action<string, bool, string> check)
    {
        var wf = Repo(".github", "workflows", "release.yml");
        check("Rename 1: o workflow copia o exe para Pickroute.exe e confirma que é o mesmo ficheiro",
            wf.Contains("Copy-Item out/CS2IPChanger.exe out/Pickroute.exe") && wf.Contains("-ne $hash) { throw 'Pickroute.exe is not the same file as CS2IPChanger.exe' }"), "");
        check("Rename 1: cada nome tem o seu .sha256 com o seu nome (a linha que o release-key e o Updater leem)",
            wf.Contains("foreach ($name in 'CS2IPChanger.exe', 'Pickroute.exe')") && wf.Contains("WriteAllText(\"$PWD/out/$name.sha256\", \"$hash  $name`n\")"), "");
        check("Rename 1: os dois exe são atestados e os quatro ficheiros vão para o rascunho",
            wf.Contains("subject-path: |\n            out/CS2IPChanger.exe\n            out/Pickroute.exe\n")
            && wf.Contains("$files = 'out/Pickroute.exe', 'out/Pickroute.exe.sha256', 'out/CS2IPChanger.exe', 'out/CS2IPChanger.exe.sha256'"), "");

        var key = Repo("tools", "release-key.mjs");
        check("Rename 1: o release-key.mjs assina os nomes do Updater, o novo primeiro",
            key.Contains("const EXES = ['Pickroute.exe', 'CS2IPChanger.exe'];") && Updater.AssetNames.SequenceEqual(new[] { "Pickroute.exe", "CS2IPChanger.exe" }), "");
        check("Rename 1: verifica cada .sha256 exato, que são o mesmo ficheiro e a atestação de cada um",
            key.Contains("if (shaFile.toString('utf8') !== `${actual}  ${name}\\n`)") && key.Contains("if (new Set(files.map((f) => f.actual)).size !== 1)")
            && key.Contains("for (const { name, exe } of files) {") && !key.Contains("const EXE ="), "");
        check("Rename 1: assina cada .sha256 com a mensagem do Updater e envia os dois .sig numa vez",
            key.Contains("const message = (version, shaFile) => Buffer.concat([Buffer.from(`CS2IPChanger ${version.replace(/^v/i, '')}\\n`, 'utf8'), shaFile]);")
            && key.Contains("const sigPath = path.join(dir, `${name}.sha256.sig`);") && key.Contains("['release', 'upload', tag, ...sigs, '--clobber']"), "");
        var sha = System.Text.Encoding.UTF8.GetBytes(new string('a', 64) + "  Pickroute.exe\n");
        check("Rename 1: a mensagem assinada do Pickroute.exe é \"CS2IPChanger <versão>\\n\" + o .sha256 (igual à do CS2IPChanger.exe)",
            System.Text.Encoding.UTF8.GetString(Updater.SignedMessage("v1.0.1", sha)) == "CS2IPChanger 1.0.1\n" + new string('a', 64) + "  Pickroute.exe\n", "");

        var pages = Repo("tools", "site-pages.mjs");
        check("Rename 1: site-pages --release escolhe Pickroute.exe quando a release o tem e guarda-o em release.json",
            pages.Contains("const exe = ['Pickroute.exe', was.asset].map((n) => r.assets?.find((a) => a.name === n)).find(Boolean);")
            && pages.Contains("const next = { ...was, asset: exe.name,") && pages.Contains("a.name === `${exe.name}.sha256`"), "");
        check("Rename 1: os links de download escritos à mão no README seguem o mesmo exe",
            pages.Contains("s = s.replaceAll(/https:\\/\\/github\\.com\\/Sepd0x\\/(?:cs2-ip-changer|pickroute)\\/releases\\/latest\\/download\\/[^\")<\\s]+/g, EXE);\n  put(file, vtLinks(s));"), "");
        var readme = Repo("README.md");
        var asset = System.Text.RegularExpressions.Regex.Match(Repo("site", "release.json"), "\"asset\": \"([^\"]+)\"").Groups[1].Value;
        var links = System.Text.RegularExpressions.Regex.Matches(readme, "releases/latest/download/([^\")<\\s]+)").Select(m => m.Groups[1].Value).ToList();
        check("Rename 1: todos os Download do README apontam para o exe de release.json", links.Count >= 4 && links.All(l => l == asset), string.Join(",", links) + " / " + asset);

        check("Rename 1: o Installer copia o exe que corre (Pickroute.exe também) com o nome de sempre",
            Repo("src", "Core", "Installer.cs").Contains("var exe = Path.Combine(target, AppIds.ExeName);") && Repo("src", "Core", "Installer.cs").Contains("var path = Environment.ProcessPath"), "");
        check("Rename 1: uma cópia aberta encontra a outra com qualquer dos dois nomes",
            AppIds.ProcessNames("Pickroute").SequenceEqual(new[] { "Pickroute", "CS2IPChanger" }) && AppIds.ProcessNames("CS2IPChanger").SequenceEqual(new[] { "CS2IPChanger", "Pickroute" })
            && AppIds.ProcessNames("cs2ipchanger").Length == 2 && Count(Repo("src", "App.xaml.cs"), "AppIds.ProcessNames(me.ProcessName)") == 2, "");
    }

    static int Count(string s, string part) { var n = 0; for (var i = s.IndexOf(part); i >= 0; i = s.IndexOf(part, i + part.Length)) n++; return n; }
}
