using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Sistema de design (5 nov.): a app, o site, a dashboard e o README contam a mesma história. Falha quando:
// 1. as cores (tokens --t-…) de um ficheiro web não são as do src/Theme.xaml, um nome partilhado deixa de apontar para o seu
//    token, ou uma cor partilhada aparece escrita à mão (node tools/design-tokens.mjs);
// 2. as capturas da app no site, no README e na dashboard são de outra versão ou de outro XAML, ou foram trocadas sem o
//    manifesto docs/assets/shots.json, ou os posters 3D mostram uma Connection antiga (node tools/shots.mjs). docs/DESIGN.md.
// As listas (tokens, ficheiros, nomes) são lidas de tools/design-tokens.mjs e tools/shots.mjs: uma só fonte.
static class DesignSyncTests
{
    static readonly Regex Block = new(@"/\* tokens:[\s\S]*?/\* /tokens \*/");

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Path_(string rel) => Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
        string Txt(string rel) => File.ReadAllText(Path_(rel)).Replace("\r\n", "\n");
        var tokensMjs = Txt("tools/design-tokens.mjs");

        // ---------------- 1. tokens
        var theme = ReadTheme(Txt("src/Theme.xaml"));
        var tokenKeys = Regex.Matches(Regex.Match(tokensMjs, @"export const TOKENS = \[([\s\S]*?)\];").Groups[1].Value, @"\['([a-z0-9-]+)', '([A-Za-z0-9]+)'\]")
            .Select(m => (name: m.Groups[1].Value, key: m.Groups[2].Value)).ToList();
        check("Design: a lista de tokens lê-se do design-tokens.mjs", tokenKeys.Count >= 20, tokenKeys.Count.ToString());
        var missing = tokenKeys.Where(t => !theme.ContainsKey(t.key)).Select(t => t.key).ToList();
        check("Design: cada token tem a sua cor no Theme.xaml", missing.Count == 0, string.Join(", ", missing));
        var want = tokenKeys.Where(t => theme.ContainsKey(t.key)).ToDictionary(t => t.name, t => theme[t.key]);
        if (want.TryGetValue("accent", out var acc))
            want["accent-rgb"] = string.Join(",", new[] { 1, 3, 5 }.Select(i => Convert.ToInt32(acc.Substring(i, 2), 16)));
        if (want.ContainsKey("top1") && want.ContainsKey("top3")) want["top"] = $"linear-gradient(90deg,{want["top1"]},{want["top2"]} 50%,{want["top3"]})";

        var files = Regex.Matches(Regex.Match(tokensMjs, @"export const FILES = \[([\s\S]*?)\];").Groups[1].Value, @"path: '([^']+)'").Select(m => m.Groups[1].Value).ToList();
        var guides = Directory.GetDirectories(Path_("site")).Where(d => File.Exists(Path.Combine(d, "index.html")))
            .Select(d => "site/" + Path.GetFileName(d) + "/index.html").OrderBy(x => x, StringComparer.Ordinal).ToList();
        check("Design: os ficheiros com tokens incluem o site, a dashboard v5 e o README", files.Contains("site/index.html") && files.Contains("worker/stats/dashboard/v5/home.css")
            && files.Contains("docs/assets/src/social-preview.css"), string.Join(", ", files));
        var behind = new List<string>();
        foreach (var f in files.Concat(guides))
        {
            var m = Block.Match(Txt(f));
            if (!m.Success) { behind.Add($"{f}: sem bloco de tokens"); continue; }
            var got = Regex.Matches(m.Value, @"--t-([a-z0-9-]+):\s*([^;]+);").ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value.Trim());
            foreach (var (n, v) in want)
                if (!got.TryGetValue(n, out var g) || g != v) behind.Add($"{f} --t-{n} {(g ?? "falta")} em vez de {v}");
        }
        check("Design: os tokens do site, das guias, da dashboard e do README são os do Theme.xaml", behind.Count == 0, string.Join("; ", behind.Take(6)) + (behind.Count > 0 ? " (node tools/design-tokens.mjs)" : ""));
        var bg = theme.GetValueOrDefault("BgColor");
        var wrongMeta = new[] { "site/index.html" }.Concat(guides).Where(f => Regex.Match(Txt(f), "<meta name=\"theme-color\" content=\"([^\"]+)\">").Groups[1].Value != bg).ToList();
        check("Design: o theme-color do site e das guias é o fundo da app", wrongMeta.Count == 0, string.Join(", ", wrongMeta));

        // cada nome partilhado é o seu token (WEB no design-tokens.mjs), no ficheiro de cada página
        var webFile = new Dictionary<string, string>
        {
            ["site"] = "site/index.html", ["dashboard"] = "worker/stats/dashboard/style.css",
            ["dashboard5"] = "worker/stats/dashboard/v5/home.css", ["readme"] = "docs/assets/src/social-preview.css",
        };
        var web = Regex.Match(tokensMjs, @"export const WEB = \{([\s\S]*?)\n\};").Groups[1].Value;
        var kinds = Regex.Matches(web, @"\n  ([a-z0-9]+): \{([\s\S]*?)\n  \},").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        check("Design: cada página com nomes partilhados tem o seu ficheiro", kinds.Count > 0 && kinds.Keys.All(webFile.ContainsKey), string.Join(", ", kinds.Keys));
        var unshared = new List<string>();
        foreach (var (kind, body) in kinds)
        {
            if (!webFile.TryGetValue(kind, out var f)) continue;
            var css = Block.Replace(Txt(f), "");
            foreach (Match m in Regex.Matches(body, @"'(--[a-z0-9-]+)': '([a-z0-9-]+)'"))
                if (!Regex.IsMatch(css, Regex.Escape(m.Groups[1].Value) + @"\s*:\s*var\(--t-" + Regex.Escape(m.Groups[2].Value) + @"\)")) unshared.Add($"{f} {m.Groups[1].Value}");
        }
        check("Design: os nomes partilhados apontam para os tokens", unshared.Count == 0, string.Join(", ", unshared));

        // nenhuma cor partilhada escrita à mão (os gráficos e as outras paletas têm as suas)
        string[] exact = { "bg", "edge", "surface", "surface2", "surface3", "line", "line-strong", "good", "warn", "accent", "accent-light", "accent-dark", "accent-deep", "top1", "top2", "top3" };
        string[] near = { "accent", "accent-light", "accent-dark", "accent-deep", "top1", "top2", "top3" };
        // conhecidos: a cópia da app no "Na app" da dashboard corta a linha do topo em duas, cada ponta tirada de uma captura
        var known = new HashSet<string> { "worker/stats/dashboard/style.css #3cb3e5", "worker/stats/dashboard/style.css #d8df3b" };
        var palettes = new Regex(@"html\[data-pal=""[a-z]+""\]\s*\{[^}]*\}");
        string StyleOf(string f) => f.EndsWith(".html") ? string.Join("\n", Regex.Matches(Txt(f), @"<style>([\s\S]*?)</style>").Select(m => m.Groups[1].Value)) : Txt(f);
        var copies = new List<string>();
        foreach (var f in files.Concat(guides).Distinct())
        {
            var css = palettes.Replace(Block.Replace(StyleOf(f), ""), "");
            var lits = Regex.Matches(css, @"#[0-9a-fA-F]{6}\b").Select(m => (lit: m.Value.ToLowerInvariant(), rgb: Rgb(m.Value)))
                .Concat(Regex.Matches(css, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)").Select(m => (lit: m.Value, rgb: new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value) })));
            foreach (var (lit, rgb) in lits)
                foreach (var n in exact.Where(want.ContainsKey))
                {
                    var t = Rgb(want[n]);
                    var d = Enumerable.Range(0, 3).Max(i => Math.Abs(t[i] - rgb[i]));
                    if ((d == 0 || (d <= 3 && near.Contains(n))) && !known.Contains($"{f} {lit}")) copies.Add($"{f}: {lit} é {(d > 0 ? "quase " : "")}--t-{n}");
                }
        }
        check("Design: nenhuma cor da app escrita à mão no site, na dashboard ou no README", copies.Count == 0, string.Join("; ", copies.Take(6)));

        // o Worker serve o CSS e as capturas que estão no repo (node tools/dashboard-assets.mjs)
        var assets = Txt("worker/stats/src/assets.js");
        string Served(string rel)
        {
            var m = Regex.Match(assets, "\n  " + Regex.Escape(JsonSerializer.Serialize(rel)) + @": \{ type: ""[^""]*"", b64: (?:true|false), body: (""(?:[^""\\]|\\.)*"") \},");
            return m.Success ? JsonSerializer.Deserialize<string>(m.Groups[1].Value) : null;
        }
        var stale = new List<string>();
        foreach (var css in new[] { "style.css", "v5/home.css", "ui-map.js" })
            if (Served(css)?.Replace("\r\n", "\n") != Txt("worker/stats/dashboard/" + css)) stale.Add(css);
        foreach (var png in Directory.GetFiles(Path_("worker/stats/dashboard/ui"), "*.png"))
            if (Served("ui/" + Path.GetFileName(png)) != Convert.ToBase64String(File.ReadAllBytes(png))) stale.Add("ui/" + Path.GetFileName(png));
        check("Design: o Worker da dashboard serve o CSS, o mapa e as capturas de agora", stale.Count == 0, string.Join(", ", stale) + (stale.Count > 0 ? " (node tools/dashboard-assets.mjs)" : ""));

        // ---------------- 2. capturas
        var shotsMjs = Txt("tools/shots.mjs");
        var man = JsonDocument.Parse(Txt("docs/assets/shots.json")).RootElement;
        var version = Regex.Match(Txt("src/ServerRouteTool.csproj"), "<Version>([^<]+)</Version>").Groups[1].Value;
        var redo = " (node tools/shots.mjs)";
        check("Capturas: são da versão da app", man.GetProperty("app").GetString() == version, man.GetProperty("app").GetString() == version ? version : $"{man.GetProperty("app").GetString()} / app {version}{redo}");
        var uiFiles = new[] { "src", "src/Views" }.SelectMany(d => Directory.GetFiles(Path_(d), "*.xaml").Select(p => d + "/" + Path.GetFileName(p)))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        check("Capturas: o manifesto conta os mesmos XAML", man.GetProperty("uiFiles").EnumerateArray().Select(e => e.GetString()).SequenceEqual(uiFiles), string.Join(", ", uiFiles));
        var fp = Sha(Encoding.UTF8.GetBytes(string.Concat(uiFiles.Select(f => $"{f}\n{Txt(f)}\n"))));
        check("Capturas: a UI da app não mudou depois das capturas", man.GetProperty("ui").GetString() == fp, man.GetProperty("ui").GetString() == fp ? "" : "um XAML mudou: refaz as capturas" + redo);

        var scale = man.GetProperty("scale").GetInt32();
        var b = man.GetProperty("base").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        check("Capturas: o site e o README a 2x", scale == 2 && b.SequenceEqual(new[] { 1240, 800 }), $"{scale}x {string.Join("x", b)}");
        var listed = new Dictionary<string, string>();
        var bad = new List<string>();
        foreach (var e in man.GetProperty("files").EnumerateArray())
        {
            var rel = e.GetProperty("file").GetString();
            listed[rel] = e.GetProperty("sha256").GetString();
            if (!File.Exists(Path_(rel))) { bad.Add($"{rel} falta"); continue; }
            var bytes = File.ReadAllBytes(Path_(rel));
            if (Sha(bytes) != listed[rel]) bad.Add($"{rel} mudou fora do script");
            if (rel.StartsWith("docs/screenshots/") && PngSize(bytes) is var (w, h) && (w != b[0] * scale || h != b[1] * scale)) bad.Add($"{rel} {w}x{h}");
            if (rel.StartsWith("worker/stats/dashboard/ui/") && PngSize(bytes) is var (w1, h1) && (w1 != b[0] || h1 != b[1])) bad.Add($"{rel} {w1}x{h1}");
        }
        check("Capturas: cada ficheiro é o que o script fez", bad.Count == 0, string.Join("; ", bad.Take(6)) + (bad.Count > 0 ? redo : ""));

        // tudo o que mostra a app está no manifesto: as pastas, o que o site e o README apontam
        var shown = new List<string>();
        shown.AddRange(Directory.GetFiles(Path_("docs/screenshots"), "*.png").Select(p => "docs/screenshots/" + Path.GetFileName(p)));
        shown.AddRange(Directory.GetFiles(Path_("docs/assets/shots")).Where(p => p.EndsWith(".png") || p.EndsWith(".webp")).Select(p => "docs/assets/shots/" + Path.GetFileName(p)));
        shown.AddRange(Directory.GetFiles(Path_("worker/stats/dashboard/ui"), "*.png").Select(p => "worker/stats/dashboard/ui/" + Path.GetFileName(p)));
        foreach (var page in new[] { "site/index.html", "site/404.html" }.Concat(guides))
            foreach (Match m in Regex.Matches(Txt(page), @"img/([a-z0-9-]+)\.(?:png|webp)"))
                if (File.Exists(Path_($"docs/screenshots/{m.Groups[1].Value}.png"))) shown.Add($"docs/screenshots/{m.Groups[1].Value}.png");
        foreach (var md in new[] { "README.md", "docs/FEATURES.md" })
            foreach (Match m in Regex.Matches(Txt(md), @"(?:docs/)?assets/shots/([a-z0-9-]+\.(?:png|webp))")) shown.Add("docs/assets/shots/" + m.Groups[1].Value);
        var outside = shown.Distinct().Where(f => !listed.ContainsKey(f)).ToList();
        check("Capturas: as do site, do README e da dashboard estão todas no manifesto", outside.Count == 0, string.Join(", ", outside) + (outside.Count > 0 ? redo : ""));
        check("Capturas: o site mostra as de 2x no tamanho de 1x", Regex.Matches(Txt("site/index.html"), @"<img src=""img/(connection|servers|accounts|regions|configs|profiles)\.png""[^>]*width=""1240"" height=""800""").Count == 6, "");

        // os posters e stills 3D mostram a captura da Connection de agora
        var renders = man.GetProperty("renders");
        var from = renders.GetProperty("from").GetString();
        var rOk = listed.GetValueOrDefault("docs/screenshots/connection.png") == from;
        check("Capturas: os posters 3D mostram a Connection de agora", rOk, rOk ? "" : "refaz os posters e os stills com node tools/site-renders.mjs (docs/DESIGN.md)");
        var rNames = Regex.Matches(Regex.Match(shotsMjs, @"export const RENDERS = \[([^\]]*)\]").Groups[1].Value, "'([a-z0-9-]+)'").Select(m => m.Groups[1].Value).ToList();
        var rList = rNames.Select(n => $"site/img/{n}.webp").ToList();
        // um só comando refaz os renders, sem caminhos de outras sessões (publish/, pastas temporárias)
        var renderMjs = File.Exists(Path_("tools/site-renders.mjs")) ? Txt("tools/site-renders.mjs") : "";
        var rNoDef = rNames.Where(n => !renderMjs.Contains($"'{n}': ")).ToList();
        check("Capturas: tools/site-renders.mjs sabe fazer cada render", renderMjs.Length > 0 && rNoDef.Count == 0, string.Join(", ", rNoDef));
        var rPaths = Regex.Matches(renderMjs, @"(?<![A-Za-z])[A-Za-z]:[\\/]|AppData|scratchpad|publish/|localhost:\d+").Select(m => m.Value).Distinct().ToList();
        check("Capturas: tools/site-renders.mjs não depende de pastas de outras sessões", rPaths.Count == 0, string.Join(", ", rPaths));
        var rBad = renders.GetProperty("files").EnumerateArray().Where(e => !File.Exists(Path_(e.GetProperty("file").GetString())) || Sha(File.ReadAllBytes(Path_(e.GetProperty("file").GetString()))) != e.GetProperty("sha256").GetString())
            .Select(e => e.GetProperty("file").GetString()).ToList();
        check("Capturas: cada poster 3D é o que foi registado", rList.Count >= 6 && rList.SequenceEqual(renders.GetProperty("files").EnumerateArray().Select(e => e.GetProperty("file").GetString())) && rBad.Count == 0, string.Join(", ", rBad));
    }

    /// <summary>Theme.xaml: as cores de 6 dígitos (Color e SolidColorBrush), em minúsculas, como o design-tokens.mjs.</summary>
    internal static Dictionary<string, string> ReadTheme(string xaml)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(xaml, @"<Color x:Key=""([^""]+)"">#([0-9A-Fa-f]{6})</Color>")) d[m.Groups[1].Value] = "#" + m.Groups[2].Value.ToLowerInvariant();
        foreach (Match m in Regex.Matches(xaml, @"<SolidColorBrush x:Key=""([^""]+)"" Color=""#([0-9A-Fa-f]{6})""/>")) d[m.Groups[1].Value] = "#" + m.Groups[2].Value.ToLowerInvariant();
        return d;
    }

    static int[] Rgb(string hex) => new[] { 1, 3, 5 }.Select(i => Convert.ToInt32(hex.Substring(i, 2), 16)).ToArray();
    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static (int, int) PngSize(byte[] b) => ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
}
