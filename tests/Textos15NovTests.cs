using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SRT;
using SRT.Core;

// 15 nov.: a sessão 3 do plano (textos, idiomas e repo) sobre a auditoria final de 13 nov. (publish/auditoria-final-2026-11-13, C, E e H).
// Cada correção tem aqui o seu teste, que falhava antes (o relatório está em publish/plano-2026-11-15/3-textos-idiomas-repo.md).
static class Textos15NovTests
{
    public static void Run(Action<string, bool, string> check)
    {
        ChavesEmTernarios(check);
        PalavrasDoKick(check);
        MensagensDoSteamWeb(check);
        Compare(check);
        LinhasDeListasEPlaceholders(check);
        IdiomaComIconeNaBandeja(check);
        SemContaDoDono(check);
        DocsQueBatem(check);
        Smoke(check);
        GitAttributes(check);
        AvisosDeTerceiros(check);
    }

    static string Repo(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo() }.Concat(p).ToArray()));

    static HashSet<string> Keys()
    {
        var p = Process.Start(new ProcessStartInfo("node", $"\"{Path.Combine(SecurityTests.Repo(), "tools", "loc-keys.mjs")}\" --all") { RedirectStandardOutput = true, UseShellExecute = false });
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return JsonSerializer.Deserialize<string[]>(json).ToHashSet();
    }

    static readonly string[] Codes = { "pt-BR", "es", "ru", "tr" };

    static void Todas(Action<string, bool, string> check, string name, params string[] texts)
    {
        var keys = Keys();
        foreach (var code in Codes)
        {
            var t = Loc.Load(code);
            var gone = texts.Where(x => !t.ContainsKey(x) || t[x] == x && x.Length > 20).ToList();
            check($"{name} ({code}): traduzido", gone.Count == 0, string.Join(" | ", gone.Take(3)));
        }
        var unseen = texts.Where(x => !keys.Contains(x)).ToList();
        check($"{name}: o teste de chaves vê estes textos (tools/loc-keys.mjs)", unseen.Count == 0, string.Join(" | ", unseen.Take(3)));
    }

    // E-04: o estado de cada servidor sai de um ternário dentro de Loc.T; o extrator só via literais logo a seguir a "Loc.T(".
    static void ChavesEmTernarios(Action<string, bool, string> check)
    {
        Todas(check, "E-04: estados dos servidores", "Blocked", "Unprotected", "No kill switch", "Route failed", "Protected");
        Todas(check, "E-04: passos do telemóvel", "ADB installed.", "Using the phone's hotspot…", "Using the phone over USB…",
            "Airplane Mode on: now turn it off on the iPhone…", "the iPhone is back: checking the new IP…");
    }

    // C-02: "Você foi kicked do servidor": as palavras entram numa frase por Loc.T(variável).
    static void PalavrasDoKick(Action<string, bool, string> check)
    {
        Todas(check, "C-02: palavras do aviso de kick", "banned", "blocked", "turned away", "kicked");
        Todas(check, "C-02: parar depois de um passo", "the new IP", "the login to {0}");
        Todas(check, "C-02: porque o IP fica", "no phone mode", "the iPhone changes its IP by hand", "the phone isn't ready on ADB");
        Todas(check, "C-02: troca de conta e pastas com ligação",
            "Steam's loginusers.vdf has content this version doesn't understand, so the account wasn't changed. Log in from Steam itself.",
            "a Steam or CS2 config folder goes through a link or junction; the app only writes to a real folder");
        check("C-02: a troca de conta que falha a meio mostra o texto traduzido (SwitchTo)", Repo("src", "Core", "Steam", "SteamAccounts.cs").Contains("new IOException(Loc.T(why)) : new InvalidOperationException(Loc.T(why))"), "");
        var ru = Loc.Load("ru");
        check("C-02: em russo 'banned' é um verbo que cabe em 'Вас {0} с сервера'", ru["banned"] == "забанили", ru["banned"]);
    }

    // E-05: SteamWebException passa por Loc.T no construtor, mas o extrator não via "throw new SteamWebException("..")".
    static void MensagensDoSteamWeb(Action<string, bool, string> check)
    {
        Todas(check, "E-05: erros do Steam",
            "Steam took too long to answer.", "Steam is limiting requests right now.", "Steam's sign-in answer was not what the app expects.",
            "The saved sign-in no longer works. Sign in again.", "The saved sign-in is for another account. Sign in again.",
            "Steam's profile page was not what the app expects.", "That isn't a Steam profile link.", "Steam didn't show this profile.",
            "Steam is limiting requests. Wait a few minutes and try again.", "network error");
        Todas(check, "E-05: textos que o código passa por constantes", "Windows doesn't let the app read the Wi-Fi name: turn on Location in Windows Settings > Privacy & security > Location.",
            "Usage stats", "Not locked until a phone is checked once");
        Todas(check, "E-05: passos da depuração USB",
            "1. Settings > About phone (Samsung: also Software information). Tap \"Build number\" 7 times.\n2. Settings > Developer options. Turn on \"USB debugging\".\n3. Unplug the cable and plug it back in, then press Phone setup. On the phone, tap Allow.");
    }

    // E-05: os nomes das definições em Configs > Compare, os valores e os continentes de Regions.
    static void Compare(Action<string, bool, string> check)
    {
        Todas(check, "E-05: Configs > Compare", "Sensitivity", "Viewmodel FOV", "Style", "Master volume", "Use key opens buy menu", "no file", "unbound", "default", "on", "off", "red", "Key {0}", "crosshair");
        Todas(check, "E-05: continentes de Regions", "Europe", "North America", "South America", "Asia", "Middle East", "Africa", "Oceania");
        var cfg = Repo("src", "Core", "Steam", "Cs2Configs.cs");
        var labels = System.Text.RegularExpressions.Regex.Matches(cfg[cfg.IndexOf("Dictionary<string, string> Labels")..cfg.IndexOf("static readonly string[] ColorNames")], "\\]\\s*=\\s*\"([^\"]*)\"").Select(m => m.Groups[1].Value)
            .Select(v => v.StartsWith("Crosshair: ") ? char.ToUpper(v[11]) + v[12..] : v).ToList();
        var keys = Keys();
        check("E-05: o teste de chaves vê todos os nomes da tabela Labels (" + labels.Count + ")", labels.All(keys.Contains), string.Join(" | ", labels.Where(l => !keys.Contains(l)).Take(3)));
        var row = new CfgDiffRow(new CfgDiff("binds", "W", "Key W", "on", "unbound"));
        check("E-05: a linha de uma tecla passa por Loc.F (inglês: 'Key W')", row.Label == "Key W", row.Label);
    }

    // Achado novo (captura em russo): o Loaded só chega aos ramos da árvore com um handler de instância, por isso as linhas das listas e o
    // conteúdo dos templates ficavam em inglês; e o texto cinzento das caixas (Tag) nunca passava pelo Loc.
    static void LinhasDeListasEPlaceholders(Action<string, bool, string> check)
    {
        var loc = Repo("src", "Loc.cs");
        check("Idiomas: o hook também ouve SizeChanged (o Loaded não chega aos templates)", loc.Contains("FrameworkElement.SizeChangedEvent") && loc.Contains("FrameworkElement.LoadedEvent"), "");
        check("Idiomas: o texto cinzento de uma caixa (Tag de TextBox e PasswordBox) passa por T", loc.Contains("d is TextBox or PasswordBox") && loc.Contains("T(hint)"), "");
        Todas(check, "Idiomas: textos cinzentos das caixas", "Search name, IP or map", "Search accounts", "Filter accounts", "Search the Log", "Empty = no bio", "Paste server IPs here, e.g. 1.2.3.4:27015 (Ctrl+Enter adds)", "Works without a key too");
    }

    // E-06: com "Close to the notification area" o X só esconde a janela e o idioma não muda.
    static void IdiomaComIconeNaBandeja(Action<string, bool, string> check)
    {
        var normal = MainWindow.LanguageChangedText(false);
        var tray = MainWindow.LanguageChangedText(true);
        check("E-06: sem ícone na bandeja diz para fechar e abrir", normal.Contains("Close and open"), normal);
        check("E-06: com a janela a ir para a bandeja diz para sair pelo menu do ícone", tray.Contains("Quit from the icon's menu") && tray != normal, tray);
        foreach (var code in Codes) check($"E-06 ({code}): o texto novo está traduzido", Loc.Load(code).ContainsKey(tray), "");
    }

    // Bloqueador 5: o nome da conta de Discord do dono (4 letras, aqui partido em duas para este ficheiro não se apanhar a si próprio)
    // não pode ligar o repo pseudónimo a ela. Nenhum ficheiro de texto seguido pelo git o diz, nem no nome nem no conteúdo.
    static void SemContaDoDono(Action<string, bool, string> check)
    {
        var palavra = "x" + "ao";
        var repo = SecurityTests.Repo();
        var p = Process.Start(new ProcessStartInfo("git", "ls-files") { WorkingDirectory = repo, RedirectStandardOutput = true, UseShellExecute = false });
        var files = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
        p.WaitForExit();
        var binary = new[] { ".png", ".gif", ".webp", ".ico", ".wav", ".woff2", ".zip", ".exe", ".jpg" };
        var hits = new List<string>();
        foreach (var f in files)
        {
            if (f.EndsWith("worker/stats/src/assets.js") || binary.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            if (f.Contains(palavra, StringComparison.OrdinalIgnoreCase)) { hits.Add(f); continue; }
            var path = Path.Combine(repo, f);
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 2_000_000) continue;
            if (File.ReadAllText(path).Contains(palavra, StringComparison.OrdinalIgnoreCase)) hits.Add(f);
        }
        check("Bloqueador 5: o nome da conta do dono não está em nenhum ficheiro de texto do repo (nome ou conteúdo)", hits.Count == 0, string.Join(" ", hits.Take(5)));
        check("Bloqueador 5: o ficheiro de testes do feedback de 9 nov. tem o nome novo e está chamado", files.Contains("tests/Feedback9NovTests.cs") && Repo("tests", "Program.cs").Contains("Feedback9NovTests.Run(Check);"), "");
    }

    // B-03, D-03, F-04, H-06: o que os documentos prometem tem de bater com o código.
    static void DocsQueBatem(Action<string, bool, string> check)
    {
        var readme = Repo("README.md");
        var privacy = Repo("docs", "PRIVACY-AND-SECURITY.md");
        var setup = Repo("docs", "SETUP.md");
        // B-03: o Phone setup descarrega o ADB sozinho ao abrir (MainWindow.Phone.cs, AutoInstallAdb)
        check("B-03: o código descarrega o ADB sozinho ao abrir o Phone setup", MainWindow.AutoInstallAdb(false, false, false, false), "");
        check("B-03: o README já não diz que o ADB só se descarrega 'se pedires'", !readme.Contains("only if you ask") && readme.Contains("Phone setup does it by itself"), "");
        check("B-03: PRIVACY-AND-SECURITY já não diz que o ADB só se descarrega 'se pedires'", !privacy.Contains("Only if you ask the app to download ADB") && privacy.Contains("Phone setup downloads it by itself"), "");
        // D-03: não há lançador nativo
        check("D-03: PRIVACY-AND-SECURITY não promete um lançador nativo 'para a 1.0'", !privacy.Contains("planned for 1.0"), "");
        // F-04: o Worker lê o IP para o limitador (worker/stats/src/index.js, ipAllowed), só em hash
        check("F-04: o Worker lê mesmo o IP para o limitador", Repo("worker", "stats", "src", "index.js").Contains("cf-connecting-ip"), "");
        check("F-04: o README não diz que o Worker nunca lê o IP", !readme.Contains("never reads the IP") && readme.Contains("rate limit"), "");
        check("F-04: PRIVACY-AND-SECURITY diz que o IP só serve ao limitador, em hash", privacy.Contains("rate-limit") && privacy.Contains("SHA-256"), "");
        // H-06: SRT_Dados fica (decisão do dono, 15 nov.): documentada como nome fixo
        check("H-06: SETUP diz que SRT_Dados é um nome que fica", setup.Contains("it stays the same"), "");
    }

    // H-03: docs/SMOKE.md cobre a beta.13 e a 1.0.0
    static void Smoke(Action<string, bool, string> check)
    {
        var s = Repo("docs", "SMOKE.md");
        foreach (var part in new[] { "First start", "Phone setup, Android", "Phone setup, iPhone", "Languages", "A banned IP", "SmartScreen", "--restore", "Update from the beta.13", "Whole PC and a crash" })
            check("H-03: SMOKE.md tem o passo '" + part + "'", s.Contains(part), "");
    }

    // O GitHub diz que o repo é HTML por causa do site (gerado); a app é C#.
    static void GitAttributes(Action<string, bool, string> check)
    {
        var a = Repo(".gitattributes");
        var rules = string.Join("\n", a.Split('\n').Where(l => !l.TrimStart().StartsWith("#")));
        foreach (var rule in new[] { "site/*/index.html linguist-generated", "site/pt-br/** linguist-generated", "site/es/** linguist-generated", "site/ru/** linguist-generated", "site/tr/** linguist-generated",
            "site/assets/js/scenes.min.js linguist-generated", "site/assets/guides/world.min.js linguist-generated", "worker/stats/src/assets.js linguist-generated",
            "site/assets/js/gsap.min.js linguist-vendored", "site/assets/js/ScrollTrigger.min.js linguist-vendored", "site/assets/js/lenis.min.js linguist-vendored" })
            check(".gitattributes: " + rule, a.Contains(rule), "");
        check(".gitattributes: a home escrita à mão e o código da app não são marcados", !rules.Contains("site/index.html") && !rules.Contains("\nsrc/") && !rules.StartsWith("src/") && !rules.Contains("*.cs ") && !rules.Contains("\ntests/"), "");
        check(".gitattributes: a regra do .cmd continua", a.Contains("*.cmd text eol=crlf"), "");
        // o que se marca como gerado é mesmo escrito por uma ferramenta
        check(".gitattributes: o que se marca como gerado vem de uma ferramenta do repo",
            Repo("tools", "site-pages.mjs").Contains("site-i18n") && Repo("tools", "dashboard-assets.mjs").Contains("assets.js") && Repo("site", "assets", "js", "README.txt").Contains("built with esbuild"), "");
    }

    // Bloqueador 7: three.js e Lenis são MIT e pedem o aviso de copyright; os pacotes e os ficheiros do site não o trazem.
    static void AvisosDeTerceiros(Action<string, bool, string> check)
    {
        var n = Repo("THIRD-PARTY-NOTICES");
        foreach (var part in new[] { "three.js", "Copyright © 2010-2026 three.js authors", "Lenis", "darkroom.engineering", "Permission is hereby granted", "GSAP", "GreenSock", "Kenney", "CC0",
            "Funnel Display", "Fragment Mono", "SIL Open Font License", "ProtectedData" })
            check("Bloqueador 7: THIRD-PARTY-NOTICES tem '" + part + "'", n.Contains(part), "");
        var license = Repo("LICENSE");
        check("Bloqueador 7: o LICENSE só ganha uma linha à parte (os termos não mudam)", license.Contains("see THIRD-PARTY-NOTICES") && license.Contains("All rights reserved"), "");
        // o que o aviso diz que está no repo existe
        foreach (var f in new[] { "site/assets/js/lenis.min.js", "site/assets/js/gsap.min.js", "src/Assets/Sounds/LICENSE-kenney.txt", "site/fonts/OFL-funnel-display.txt" })
            check("Bloqueador 7: existe " + f, File.Exists(Path.Combine(SecurityTests.Repo(), f)), "");
        check("Bloqueador 7: o csproj usa mesmo o ProtectedData 8.0.0", Repo("src", "ServerRouteTool.csproj").Contains("System.Security.Cryptography.ProtectedData\" Version=\"8.0.0\""), "");
    }
}
