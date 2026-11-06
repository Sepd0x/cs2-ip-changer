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

        // ---------------- 3. os passos da história (6 nov.): o chip do telemóvel e o endereço seguem o rail; o passo marca-se
        // quando o scroll assenta, não em cada frame (antes eram ~200 escritas de aria-current numa passagem pela história)
        var homeHtml = Txt("site/index.html"); var homeJs = Txt("site/assets/js/home.js");
        List<string> Steps(string id) => Regex.Matches(Regex.Match(homeHtml, $"id=\"{id}\"[\\s\\S]*?</ol>").Value, "<a href=\"(#[a-z-]+)\">([^<]+)</a>").Select(m => m.Groups[1].Value + "=" + m.Groups[2].Value).ToList();
        var railSteps = Steps("rail"); var whereSteps = Steps("where");
        check("Site: o chip do telemóvel tem os seis passos do rail", railSteps.Count == 6 && railSteps.SequenceEqual(whereSteps), string.Join(" ", whereSteps));
        var jsSteps = Regex.Matches(Regex.Match(homeJs, @"var STEPS = \[([^\]]*)\]").Groups[1].Value, "'(#[a-z-]+)'").Select(m => m.Groups[1].Value).ToList();
        check("Site: o endereço usa os passos do rail", railSteps.Count == 6 && jsSteps.SequenceEqual(railSteps.Skip(1).Select(s => s.Split('=')[0])), string.Join(" ", jsSteps));
        var updateBody = Regex.Match(homeJs, @"function update\(\) \{[\s\S]*?\n  \}\n").Value;
        check("Site: aria-current e o rail só quando o passo assenta", updateBody.Length > 0 && !updateBody.Contains("aria-current") && homeJs.Contains("'scrollend'") && homeJs.Contains("history.replaceState"), "");

        // ---------------- 4. auditoria de SEO (6 nov.): títulos e descrições nos limites, o cartão social completo em cada guia,
        // a linha do SmartScreen debaixo de cada Download, o ?ref= no contador, e no telemóvel o ecrã de carregamento não espera pelo 3D
        string Un(string x) => System.Net.WebUtility.HtmlDecode(x);
        var pagesHtml = new[] { "site/index.html" }.Concat(guides).ToDictionary(f => f, Txt);
        var longT = pagesHtml.Where(kv => Un(Regex.Match(kv.Value, "<title>([^<]*)</title>").Groups[1].Value).Length > 60).Select(kv => kv.Key).ToList();
        check("SEO: cada título tem 60 caracteres ou menos", longT.Count == 0, string.Join(", ", longT));
        var longD = pagesHtml.Where(kv => Un(Regex.Match(kv.Value, "<meta name=\"description\" content=\"([^\"]*)\"").Groups[1].Value) is var d && (d.Length == 0 || d.Length > 155)).Select(kv => kv.Key).ToList();
        check("SEO: cada descrição tem 155 caracteres ou menos", longD.Count == 0, string.Join(", ", longD));
        var noCard = pagesHtml.Where(kv => !new[] { "twitter:image\"", "og:image:alt", "og:image:width", "og:image:height" }.All(kv.Value.Contains)).Select(kv => kv.Key).ToList();
        check("SEO: cada página tem twitter:image e og:image com alt, largura e altura", noCard.Count == 0, string.Join(", ", noCard));
        var homeH1 = Un(Regex.Match(homeHtml, "<h1[^>]*>([^<]*)</h1>").Groups[1].Value);
        var sameH1 = guides.Where(f => Un(Regex.Match(pagesHtml[f], "<h1[^>]*>([^<]*)</h1>").Groups[1].Value) == homeH1).ToList();
        check("SEO: nenhuma guia repete o h1 da home", homeH1.Length > 0 && sameH1.Count == 0, string.Join(", ", sameH1));
        var noSs = pagesHtml.Where(kv => Regex.Matches(kv.Value, @"Download for Windows\s*</a>").Count > Regex.Matches(kv.Value, "class=\"rel-ss[^\"]*\">Windows may say \"Windows protected your PC\": click More info, then Run anyway. <a href=\"(\\.\\./)?is-it-safe/\">").Count).Select(kv => kv.Key).ToList();
        check("SEO: debaixo de cada Download, o que fazer no aviso do SmartScreen", noSs.Count == 0, string.Join(", ", noSs));
        var noRef = pagesHtml.Where(kv => !kv.Value.Contains("if(!/^[a-z0-9-]{1,20}$/.test(rq))rq=''") || !kv.Value.Contains("sp.delete('ref')") || !kv.Value.Contains("if(!rf&&rq)rf='ref:'+rq")).Select(kv => kv.Key).ToList();
        check("SEO: o contador usa o ?ref= (só [a-z0-9-], até 20) e tira-o da barra", noRef.Count == 0, string.Join(", ", noRef));
        // o site manda "ref:<nome>" e o Worker só guarda esse formato (antes mandava "tiktok" e o Worker contava-o como "other")
        var workerSrc = Txt("worker/stats/src/index.js");
        check("SEO: o ?ref= chega ao Worker no formato que ele guarda (ref:<nome>)",
            workerSrc.Contains("const REF_TAG = /^ref:[a-z0-9-]{1,20}$/;") && workerSrc.Contains("!REF_TAG.test(ref)"), "");
        var slowLd = new[] { "site/index.html", "site/404.html" }.Concat(guides).Where(f => !Txt(f).Contains("setTimeout(end,matchMedia('(pointer:coarse)').matches?1100:6000)")).ToList();
        check("Site: no telemóvel o ecrã de carregamento sai em ~1,5 s e não espera pelo 3D", slowLd.Count == 0 && homeHtml.Contains("@media (pointer:fine),(pointer:none){html.ld .b-cover .copy>*{animation-play-state:paused}}"), string.Join(", ", slowLd));

        // ---------------- 5. o telemóvel com componentes próprios (6 nov.): as seis páginas lado a lado com recortes a tamanho
        // real, a história mais curta ao alto, o chip e a pausa fora do texto, o rodapé em duas colunas, o ?ref= na is-it-safe
        const string carQ = "(max-width:759px) and (orientation:portrait)";
        check("Site: as seis páginas da janela lado a lado no telemóvel ao alto, com o CSS e o home.js na mesma medida",
            Regex.IsMatch(homeHtml, @"@media \(max-width:759px\) and \(orientation:portrait\)\{\s*\.js \.app-body\{") && homeHtml.Contains("scroll-snap-type:x mandatory") && homeJs.Contains($"matchMedia('{carQ}')"), "");
        // cada recorte é a captura a 1240x800 (o tamanho da janela da app), com o anel no sítio das marcas do PC
        check("Site: os recortes das páginas são a captura a tamanho real, centrados nas marcas da janela",
            homeHtml.Contains(".cut-in{position:absolute;width:1240px;height:800px;") && homeJs.Contains("'.marks .mk[data-pg=\"' + pg + '\"]'") && homeJs.Contains("* 12.4).toFixed(1) + 'px'") && homeJs.Contains("* 8).toFixed(1) + 'px'"), "");
        check("Site: um botão por página (44 px) e o endereço segue a página deslizada",
            homeJs.Contains("nav.className = 'pg-nav'") && homeHtml.Contains(".pg-nav button{flex:1;min-width:44px;min-height:44px;") && homeJs.Contains("take(stepAt(scrollY)); }, 160);"), "");
        check("Site: no telemóvel ao alto os passos da história mais juntos (o ban de 334 para 226 svh)",
            Regex.IsMatch(homeHtml, @"@media \(max-width:759px\) and \(orientation:portrait\)\{\s*#how \.beat\{min-height:78svh\}\s*\.beat-long\{min-height:226svh\}"), "");
        check("Site: a pausa e o chip saem de cima do texto simples (a nota do ban, as secções sólidas) e não só dos cartões",
            homeJs.Contains("var plain = $$('.ba-copy, section.solid');") && homeJs.Contains("plain.some(") && homeHtml.Contains(".where.under,.pause.under{transition:opacity .12s,visibility .12s}"), "");
        var guideRow = homeHtml.Contains("ul.guides a::after{content:\"\";position:absolute;inset:0}") && homeHtml.Contains("ul.guides li{position:relative;");
        check("Site: cada linha das guias é clicável por inteiro", guideRow, "");
        var footer2 = guides.Where(f => !Txt(f).Contains(".fgrid nav[aria-labelledby=f2] ul{grid-template-columns:repeat(2,minmax(0,1fr));")).ToList();
        check("Site: o rodapé em duas colunas no telemóvel, na home e em cada guia, com 44 px por link",
            homeHtml.Contains("footer nav li a{display:flex;align-items:center;min-height:44px;") && footer2.Count == 0, string.Join(", ", footer2));
        // Android ou iPhone no telemóvel (pedido do dono, 6 nov.): separadores com teclado, iPhone primeiro para quem
        // vem de iOS, os dois cartões na mesma célula (sem salto ao trocar), e sem JS ou no PC os dois à vista
        check("Site: Android | iPhone no telemóvel é um tablist com setas, Home e End, e abre no iPhone para quem vem de iOS",
            homeJs.Contains("list.setAttribute('role', 'tablist')") && homeJs.Contains("b.setAttribute('role', 'tab')") && homeJs.Contains("'ArrowLeft'") && homeJs.Contains("'Home'") && homeJs.Contains("'End'")
            && homeJs.Contains("/iPhone|iPad|iPod/.test(navigator.userAgent)") && homeJs.Contains("on = ios ? 1 : 0"), "");
        check("Site: Android | iPhone só no telemóvel, com alvos de 44 px e sem salto ao trocar (os dois cartões na mesma célula)",
            homeHtml.Contains(".ph-tabs{display:none}") && homeHtml.Contains(".ph-tabs button{min-width:6.5rem;min-height:44px;") && homeHtml.Contains(".duo.tabbed>div{grid-area:1/1;")
            && homeJs.Contains("matchMedia('(max-width:759px),(max-height:540px) and (orientation:landscape)')") && homeHtml.Contains("@media (max-width:759px),(max-height:540px) and (orientation:landscape){\n  .ph-tabs{display:flex;".Replace("\n", homeHtml.Contains("\r\n") ? "\r\n" : "\n")), "");
        check("Site: a is-it-safe diz que o contador conta o ?ref= de um link",
            Txt("site/is-it-safe/index.html").Contains("or the short name in the link when there is none (like <code>?ref=tiktok</code>)"), "");
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
