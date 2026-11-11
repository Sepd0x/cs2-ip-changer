#nullable enable
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Organização da app (6 nov., para a beta.12): cada página segue docs/DESIGN.md > App layout. Falha quando:
// 1. uma página tem grupos fora da ordem ou na coluna errada (tabela Pages);
// 2. uma opção de Settings está noutro grupo, ou há uma opção com nome que a tabela não conhece (tabela Settings);
// 3. um nome de grupo se repete, tem parênteses, é comprido, ou usa o nome de outra página;
// 4. em duas colunas e em Settings, a última caixa de uma coluna não chega ao fundo; o vermelho não é o último da sua
//    fila; um grupo de Settings tem mais de 7 coisas;
// 5. um grupo só com texto que não é o How it works da Connection;
// 6. a moldura da página não é 16 nos lados e 18 em baixo;
// 7. um texto (app, docs, README, site) manda para "Settings > X" ou "Página > Grupo > X" e o X não existe lá;
// 8. a app aponta com "above", "below" ou "on the right" em vez de nomes.
// As listas leem-se do DESIGN.md: uma só fonte. Mais as mudanças da reorganização: Show hidden e Hide usernames.
static class OrganizacaoTests
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static readonly string[] PageNames = { "Connection", "Servers", "Regions", "Accounts", "Configs", "Profiles", "Log", "Settings" };
    static readonly HashSet<string> Inputs = new() { "CheckBox", "RadioButton", "TextBox", "PasswordBox", "ComboBox", "Slider" };
    static readonly HashSet<string> Doers = new() { "Button", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "ComboBox", "Slider", "ItemsControl", "ListBox" };

    public static void Run(Action<string, bool, string> check, string? root = null)
    {
        root ??= SecurityTests.Repo();
        string P(string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        string Txt(string rel) => File.ReadAllText(P(rel)).Replace("\r\n", "\n");

        // ---------------- as mudanças da reorganização (beta.12)
        check("Organização: Show hidden só aparece com contas escondidas", !SRT.MainWindow.ShowUnhide(0) && SRT.MainWindow.ShowUnhide(2), "");
        var accCs = Txt("src/Views/MainWindow.Accounts.cs");
        check("Organização: a lista de Accounts mostra ou esconde o Show hidden", accCs.Contains("AccUnhideBtn.Visibility = ShowUnhide(_accHidden)"), "");
        var xamlTxt = Txt("src/MainWindow.xaml");
        check("Organização: Hide usernames em Settings continua a usar o mesmo clique (AccOpt_Click)",
            Regex.IsMatch(xamlTxt, @"<CheckBox x:Name=""OptStreamer""[^>]*Click=""AccOpt_Click"""), "");
        check("Organização: o Show hidden alterna com Hide hidden (ShowHidden_Click, ronda 2)", xamlTxt.Contains(@"x:Name=""AccUnhideBtn"" Click=""ShowHidden_Click"""), "");

        var sec = Regex.Match(Txt("docs/DESIGN.md"), @"\n## App layout\n([\s\S]*?)(?=\n## |$)");
        check("Organização: o DESIGN.md tem a secção App layout", sec.Success, "docs/DESIGN.md");
        if (!sec.Success) return;
        var pages = Table(sec.Groups[1].Value, "Page");
        var options = Table(sec.Groups[1].Value, "Group");
        var external = Regex.Matches(Regex.Match(sec.Groups[1].Value, @"Outside the app[^\n]*(\n[^\n-][^\n]*)*").Value, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
        check("Organização: as tabelas Pages e Settings leem-se do DESIGN.md", pages.Count == 8 && options.Count >= 10 && external.Count > 0,
            $"{pages.Count} páginas, {options.Count} grupos, {external.Count} externos");

        var doc = XDocument.Parse(Txt("src/MainWindow.xaml"));
        XElement? ById(string id) => doc.Descendants().FirstOrDefault(e => (string?)e.Attribute(X + "Name") == id);
        static bool IsGroup(XElement e) => e.Name.LocalName == "HeaderedContentControl" && (string?)e.Attribute("Style") == "{StaticResource Group}";
        static List<string> Split(string s) => s.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        static bool InTemplate(XElement e) => e.Ancestors().Any(a => a.Name.LocalName is "DataTemplate" or "ControlTemplate");

        // ---------------- 1. grupos de cada página, por coluna e por ordem
        var all = new List<(string page, string header, XElement el)>();
        var groupsOf = new Dictionary<string, List<XElement>>();
        var wrong = new List<string>();
        foreach (var row in pages)
        {
            var (name, id, shape) = (row[0], row[1], row[2]);
            var page = ById(id);
            if (page == null) { wrong.Add($"{name}: falta {id}"); continue; }
            var groups = page.DescendantsAndSelf().Where(IsGroup).Where(g => !InTemplate(g)).ToList();
            groupsOf[name] = groups;
            all.AddRange(groups.Select(g => (name, (string?)g.Attribute("Header") ?? "", g)));
            bool Right(XElement g) => g.AncestorsAndSelf().TakeWhile(a => a != page).Any(a => (string?)a.Attribute("Grid.Column") == "2");
            var left = groups.Where(g => !Right(g)).Select(g => (string?)g.Attribute("Header")).ToList();
            var right = groups.Where(Right).Select(g => (string?)g.Attribute("Header")).ToList();
            var wantL = Split(row[3]); var wantR = row.Count > 4 ? Split(row[4]) : new();
            if (!left.SequenceEqual(wantL)) wrong.Add($"{name} esquerda: {string.Join(", ", left)} (quer {string.Join(", ", wantL)})");
            if (!right.SequenceEqual(wantR)) wrong.Add($"{name} direita: {string.Join(", ", right)} (quer {string.Join(", ", wantR)})");
        }
        check("Organização: cada página tem os grupos do DESIGN.md, na ordem e na coluna certas", wrong.Count == 0, string.Join("; ", wrong));

        // ---------------- 2. cada opção de Settings no seu grupo
        var settings = ById("PageSettings");
        if (settings != null)
        {
            var want = options.SelectMany(r => (r.Count > 1 ? Split(r[1]) : new()).Select(o => (o, g: r[0]))).ToDictionary(x => x.o, x => x.g);
            var bad = new List<string>();
            var listed = options.Select(r => r[0]).ToList();
            var headers = groupsOf.GetValueOrDefault("Settings")?.Select(g => (string?)g.Attribute("Header") ?? "").ToList() ?? new();
            if (!listed.SequenceEqual(headers)) bad.Add($"grupos da tabela ({string.Join(", ", listed)}) não são os da página ({string.Join(", ", headers)})");
            foreach (var e in settings.Descendants().Where(e => Inputs.Contains(e.Name.LocalName) && e.Attribute(X + "Name") != null && !InTemplate(e)))
            {
                var n = (string)e.Attribute(X + "Name")!;
                var g = (string?)e.Ancestors().FirstOrDefault(IsGroup)?.Attribute("Header") ?? "(sem grupo)";
                if (!want.TryGetValue(n, out var wg)) bad.Add($"{n} em {g} não está na tabela");
                else if (wg != g) bad.Add($"{n} está em {g}, a tabela diz {wg}");
            }
            foreach (var (o, g) in want)
                if (settings.Descendants().All(e => (string?)e.Attribute(X + "Name") != o)) bad.Add($"{o} ({g}) não está em Settings");
            check("Organização: cada opção de Settings está no grupo que o DESIGN.md diz", bad.Count == 0, string.Join("; ", bad));
        }

        // ---------------- 3. nomes
        var names = new List<string>();
        foreach (var (page, h, _) in all)
        {
            if (h.Contains('(') || h.Split(' ').Length > 5) names.Add($"{page} > {h}: parênteses ou mais de 5 palavras");
            if (PageNames.Contains(h) && h != page) names.Add($"{page} > {h}: é o nome de outra página");
        }
        names.AddRange(all.GroupBy(g => g.header).Where(x => x.Count() > 1).Select(x => $"{x.Key} repete-se ({string.Join(", ", x.Select(y => y.page))})"));
        check("Organização: os nomes dos grupos são curtos, sem parênteses e únicos", names.Count == 0, string.Join("; ", names));

        // ---------------- 4. duas colunas: as duas acabam na mesma linha; 5. só texto, só o How it works da Connection;
        //                  o botão vermelho é o último do grupo; em Settings, no máximo 7 coisas por grupo
        var shortCols = new List<string>();
        var textOnly = new List<string>();
        var danger = new List<string>();
        var big = new List<string>();
        foreach (var row in pages)
        {
            if (!groupsOf.TryGetValue(row[0], out var groups)) continue;
            foreach (var g in groups)
            {
                var h = (string?)g.Attribute("Header");
                var mine = g.Descendants().Where(d => !InTemplate(d) && d.Ancestors().First(IsGroup) == g).ToList();
                if (!g.Descendants().Any(d => Doers.Contains(d.Name.LocalName)) && !(h == "How it works" && row[0] == "Connection")) textOnly.Add($"{row[0]} > {h}");
                // o vermelho é o último botão da sua fila (os botões irmãos do mesmo painel)
                foreach (var red in mine.Where(d => d.Name.LocalName == "Button" && (string?)d.Attribute("Style") == "{StaticResource Danger}"))
                    if (red.Parent!.Elements().Where(e => e.Name.LocalName == "Button").Last() != red) danger.Add($"{row[0]} > {h} > {red.Attribute("Content")?.Value}");
                var n = mine.Count(d => d.Name.LocalName == "Button" || Inputs.Contains(d.Name.LocalName));
                if (row[0] == "Settings" && n > 7) big.Add($"{h}: {n}");
            }
            if (row[2] is not ("two columns" or "options")) continue;
            var cols = groups.Select(g => g.Parent!).Distinct().ToList();
            if (cols.Count != 2) { shortCols.Add($"{row[0]}: {cols.Count} colunas"); continue; }
            foreach (var col in cols)
            {
                var last = col.Elements().Where(IsGroup).Last();
                var rows = col.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.RowDefinitions")?.Elements().ToList();
                var n = (int?)last.Attribute("Grid.Row") ?? 0;
                var ok = col.Name.LocalName == "Grid" && rows != null && n == rows.Count - 1 && (string?)rows[^1].Attribute("Height") == "*"
                         && last.Attribute("VerticalAlignment") == null && (string?)col.Parent?.Attribute("MinHeight") is string mh && mh.Contains("ViewportHeight");
                if (!ok) shortCols.Add($"{row[0]} > {last.Attribute("Header")?.Value}");
            }
        }
        check("Organização: nas páginas de duas colunas a última caixa de cada coluna chega ao fundo", shortCols.Count == 0, string.Join("; ", shortCols));
        check("Organização: não há grupos só de texto (fora o How it works da Connection)", textOnly.Count == 0, string.Join("; ", textOnly));
        check("Organização: o botão vermelho é o último do seu grupo", danger.Count == 0, string.Join("; ", danger));
        check("Organização: nenhum grupo de Settings tem mais de 7 coisas para clicar ou preencher", big.Count == 0, string.Join("; ", big));

        // ---------------- 6. moldura
        var frames = new List<string>();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null) continue;
            var f = (string?)page.Attribute("Margin") ?? (string?)page.Attribute("Padding")
                    ?? (string?)page.Descendants().FirstOrDefault(e => e.Name.LocalName == "ScrollViewer")?.Attribute("Padding");
            var groups = groupsOf.GetValueOrDefault(row[0]) ?? new();
            var ok = f == "16,0,16,6" ? groups.All(g => g.Attribute("Margin") == null)
                   : f == "16,0,16,18" && (IsGroup(page) || groups.All(g => (string?)g.Attribute("Margin") == "0"));
            if (!ok) frames.Add($"{row[0]}: {f}");
        }
        check("Organização: cada página tem 16 px nos lados e 18 por baixo do último grupo", frames.Count == 0, string.Join("; ", frames));

        // ---------------- 7. "Settings > X" e "Página > X" apontam para o que existe
        var labels = new Dictionary<string, List<string>>();
        var menus = doc.Descendants().Where(e => e.Name.LocalName == "MenuItem").Select(e => (string?)e.Attribute("Header")).OfType<string>().ToList();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null) continue;
            labels[row[0]] = page.DescendantsAndSelf().SelectMany(e => new[] { "Header", "Text", "Content", "Tag" }.Select(a => Resolve((string?)e.Attribute(a))))
                .OfType<string>().Where(s => s.Length > 1 && !s.StartsWith('{')).Concat(row[0] == "Settings" ? external : menus).Distinct().ToList();
        }
        static IEnumerable<string> Strings(XElement el) => el.DescendantsAndSelf().SelectMany(e => new[] { "Header", "Text", "Content", "Tag" }.Select(a => Resolve((string?)e.Attribute(a))))
            .OfType<string>().Where(s => s.Length > 1 && !s.StartsWith('{'));
        // 10 nov.: os textos novos vêm de Texts ({x:Static local:Texts.Nome}), para a tradução; lidos como a janela os mostra
        static string? Resolve(string? s) => Regex.Match(s ?? "", @"^\{x:Static local:Texts\.(\w+)\}$") is { Success: true } m
            ? typeof(SRT.Texts).GetField(m.Groups[1].Value)?.GetValue(null) as string : s;
        static bool Starts(string rest, IEnumerable<string> ls) =>
            ls.Any(l => rest.StartsWith(l, StringComparison.Ordinal) && (rest.Length == l.Length || !char.IsLetterOrDigit(rest[l.Length])));
        var refRx = new Regex(@"\b(Settings|Connection|Servers|Regions|Accounts|Configs|Profiles|Log)(?: page)? (?:>|›) ([A-Z][^\n.,;:()""<]*)");
        var stale = new List<string>();
        void Scan(string rel, IEnumerable<string> texts)
        {
            foreach (var t in texts)
                foreach (Match m in refRx.Matches(WebUtility.HtmlDecode(t)))
                {
                    var (pg, rest) = (m.Groups[1].Value, m.Groups[2].Value.TrimEnd());
                    if (!labels.TryGetValue(pg, out var ls)) continue;
                    // Página > Grupo > Opção: a opção tem de estar nesse grupo
                    var parts = rest.Split(" > ", 2);
                    var grp = parts.Length == 2 ? groupsOf.GetValueOrDefault(pg)?.FirstOrDefault(g => (string?)g.Attribute("Header") == parts[0]) : null;
                    var ok = grp != null ? Starts(parts[1], Strings(grp)) : Starts(rest, ls);
                    if (!ok) stale.Add($"{rel}: {pg} > {rest}");
                }
        }
        var xamlAttrs = doc.Descendants().SelectMany(e => e.Attributes()).Select(a => a.Value).Concat(doc.Descendants().Where(e => !e.HasElements).Select(e => e.Value));
        Scan("src/MainWindow.xaml", xamlAttrs);
        var lit = new Regex(@"""(?:[^""\\\n]|\\.)*""");
        foreach (var f in Directory.EnumerateFiles(P("src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !Regex.IsMatch(f, @"[\\/](bin|obj|Privado)[\\/]")))
            Scan(Path.GetRelativePath(root, f), lit.Matches(File.ReadAllText(f)).Select(m => m.Value));
        var docs = Directory.EnumerateFiles(P("docs"), "*.md").Where(f => !Path.GetFileName(f).StartsWith("release-"))
            .Append(P("README.md")).Concat(Directory.Exists(P("site"))
                ? Directory.EnumerateFiles(P("site"), "*.html", SearchOption.AllDirectories).Append(P("site/llms.txt")) : Array.Empty<string>());
        foreach (var f in docs.Where(File.Exists)) Scan(Path.GetRelativePath(root, f), new[] { File.ReadAllText(f) });
        check("Organização: os textos que mandam para Settings > X ou Página > X apontam para o que existe", stale.Count == 0,
            string.Join("; ", stale.Distinct().Take(12)) + (stale.Count > 12 ? $" (+{stale.Count - 12})" : ""));

        // ---------------- 8. na app, aponta-se com nomes, não com sítios: nada de "on the right" nem "<Grupo> above"
        var headersAll = all.Select(g => Regex.Escape(g.header)).Distinct();
        var place = new Regex(@"\bon the (right|left)\b|\b(" + string.Join("|", headersAll) + @") (above|below)\b");
        var where = xamlAttrs.SelectMany(t => place.Matches(t).Select(m => m.Value)).Distinct().ToList();
        check("Organização: os textos da app apontam com nomes (Página > Grupo), não com above, below ou on the right", where.Count == 0, string.Join("; ", where));
    }

    // Linhas de uma tabela markdown cuja 1.ª coluna do cabeçalho é `first` (sem o cabeçalho e o separador; sem crases)
    static List<List<string>> Table(string md, string first)
    {
        var m = Regex.Match(md, @"(?m)^\| " + Regex.Escape(first) + @" \|[^\n]*\n\|[-| ]+\|\n((?:\|[^\n]*\n?)*)");
        return m.Success
            ? m.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim().Replace("`", "")).ToList()).ToList()
            : new();
    }
}
