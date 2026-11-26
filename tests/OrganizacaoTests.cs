#nullable enable
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Organização da app (6 nov., para a beta.12; 26 nov., o GUI novo "Dial"): cada página segue docs/DESIGN.md >
// App layout. Desde 26 nov. as páginas não têm caixas: uma secção é um título com uma linha fina (Connection, Configs,
// Profiles, Settings) e as páginas de lista são o título, a barra e a lista. Falha quando:
// 1. uma página tem secções fora da ordem ou na coluna errada (tabela Pages);
// 2. uma opção de Settings está noutro grupo, há uma opção com nome que a tabela não conhece, ou a lista de grupos à
//    esquerda da Settings não diz os grupos pela ordem (tabela Settings);
// 3. um nome de secção se repete, tem parênteses, é comprido, ou usa o nome de outra página sem ser a lista desta;
// 4. uma secção é uma caixa (substitui "a última caixa de cada coluna chega ao fundo": sem caixas não há fundo a que
//    chegar; o que o desenho novo pede é que as secções não voltem a ser caixas); o vermelho não é o último da sua fila;
//    um grupo de Settings tem mais de 7 coisas;
// 5. uma secção só com texto que não é o How it works da Connection;
// 6. a moldura não é 24 nos lados e 16 em cima, ou o fundo não é o que a tabela diz; a página não abre com o seu nome
//    (22 px, Display); a lista da página falta ou não fica por baixo da sua pesquisa;
// 7. um texto (app, docs, README, site) manda para "Settings > X" ou "Página > Secção > X" e o X não existe lá;
// 8. a app aponta com "above", "below" ou "on the right" em vez de nomes;
// 9. as cores da rota (Top1 a Top3, TopLine, NavMark, RouteMid) aparecem numa página fora da rota acesa da Connection.
// As listas leem-se do DESIGN.md: uma só fonte. Mais as mudanças da reorganização: Show hidden e Hide usernames.
static class OrganizacaoTests
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static readonly string[] PageNames = { "Connection", "Servers", "Regions", "Accounts", "Configs", "Profiles", "Log", "Settings" };
    static readonly HashSet<string> Inputs = new() { "CheckBox", "RadioButton", "TextBox", "PasswordBox", "ComboBox", "Slider" };
    static readonly HashSet<string> Doers = new() { "Button", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "ComboBox", "Slider", "ItemsControl", "ListBox" };
    // Expanders que não são secções: a pílula que abre um painel (Regions > Preset) e a linha de uma nota (o aviso e a dica de Regions)
    static readonly HashSet<string> NotSections = new() { "PresetPill", "FoldLine" };
    static readonly Regex RouteColours = new(@"\{StaticResource (Top1|Top2|Top3|TopLine|NavMark|RouteMid)\}");

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
        // Pages: Page | Element | Shape | Frame | List | Sections, left (or only) column | Right column
        var pages = Table(sec.Groups[1].Value, "Page");
        var options = Table(sec.Groups[1].Value, "Group");
        var external = Regex.Matches(Regex.Match(sec.Groups[1].Value, @"Outside the app[^\n]*(\n[^\n-][^\n]*)*").Value, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
        check("Organização: as tabelas Pages e Settings leem-se do DESIGN.md", pages.Count == 8 && pages.All(r => r.Count >= 6) && options.Count >= 10 && external.Count > 0,
            $"{pages.Count} páginas, {options.Count} grupos, {external.Count} externos");
        if (pages.Any(r => r.Count < 6)) return;

        var doc = XDocument.Parse(xamlTxt);
        var theme = XDocument.Parse(Txt("src/Theme.xaml"));
        XElement? ById(string id) => doc.Descendants().FirstOrDefault(e => (string?)e.Attribute(X + "Name") == id);
        static List<string> Split(string s) => s.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // fora do ecrã: os modelos e os recursos (estilos) de cada página
        static bool InTemplate(XElement e) => e.Ancestors().Any(a => a.Name.LocalName is "DataTemplate" or "ControlTemplate" || a.Name.LocalName.EndsWith(".Resources"));
        static string? Key(XElement e) => Regex.Match((string?)e.Attribute("Style") ?? "", @"^\{StaticResource (\w+)\}$") is { Success: true } m ? m.Groups[1].Value : null;

        // ---------------- as secções: HeaderedContentControl (Group na Settings, NetSection na Connection), Expander que abre
        // uma secção (NetFold, CfgFold; o nome no Header ou no AutomationProperties.Name) e os passos da Profiles (ProfStepTitle,
        // a secção é o passo inteiro, o Grid com o número)
        var secName = new Dictionary<XElement, string>();
        foreach (var e in doc.Descendants().Where(e => !InTemplate(e)))
        {
            string? h = null; XElement? el = e;
            switch (e.Name.LocalName)
            {
                case "HeaderedContentControl":
                    h = Resolve((string?)e.Attribute("Header"));
                    break;
                case "Expander" when !NotSections.Contains(Key(e) ?? ""):
                    h = Resolve((string?)e.Attribute("Header"));
                    if (h == null || h.StartsWith('{')) h = (string?)e.Attribute("AutomationProperties.Name");
                    break;
                case "TextBlock" when Key(e) == "ProfStepTitle":
                    h = Resolve((string?)e.Attribute("Text"));
                    el = e.Ancestors().FirstOrDefault(a => a.Elements().Any(c => Key(c) == "ProfNum"));
                    break;
            }
            if (h != null && el != null) secName[el] = h;
        }
        bool IsSection(XElement e) => secName.ContainsKey(e);
        string Header(XElement e) => secName.GetValueOrDefault(e) ?? "";

        // ---------------- 1. secções de cada página, por coluna e por ordem
        var all = new List<(string page, string header, XElement el)>();
        var groupsOf = new Dictionary<string, List<XElement>>();
        var wrong = new List<string>();
        foreach (var row in pages)
        {
            var (name, id) = (row[0], row[1]);
            var page = ById(id);
            if (page == null) { wrong.Add($"{name}: falta {id}"); continue; }
            var groups = page.DescendantsAndSelf().Where(IsSection).ToList();
            groupsOf[name] = groups;
            all.AddRange(groups.Select(g => (name, Header(g), g)));
            bool Right(XElement g) => g.AncestorsAndSelf().TakeWhile(a => a != page).Any(a => (string?)a.Attribute("Grid.Column") == "2");
            var left = groups.Where(g => !Right(g)).Select(Header).ToList();
            var right = groups.Where(Right).Select(Header).ToList();
            var wantL = Split(row[5]); var wantR = row.Count > 6 ? Split(row[6]) : new();
            if (!left.SequenceEqual(wantL)) wrong.Add($"{name} esquerda: {string.Join(", ", left)} (quer {string.Join(", ", wantL)})");
            if (!right.SequenceEqual(wantR)) wrong.Add($"{name} direita: {string.Join(", ", right)} (quer {string.Join(", ", wantR)})");
        }
        check("Organização: cada página tem as secções do DESIGN.md, na ordem e na coluna certas", wrong.Count == 0, string.Join("; ", wrong));

        // ---------------- 2. cada opção de Settings no seu grupo; a lista de grupos à esquerda diz os mesmos, pela mesma ordem
        var settings = ById("PageSettings");
        if (settings != null)
        {
            var want = options.SelectMany(r => (r.Count > 1 ? Split(r[1]) : new()).Select(o => (o, g: r[0]))).ToDictionary(x => x.o, x => x.g);
            var bad = new List<string>();
            var listed = options.Select(r => r[0]).ToList();
            var headers = groupsOf.GetValueOrDefault("Settings")?.Select(Header).ToList() ?? new();
            if (!listed.SequenceEqual(headers)) bad.Add($"grupos da tabela ({string.Join(", ", listed)}) não são os da página ({string.Join(", ", headers)})");
            var nav = ById("SetNav")?.Elements().Where(e => e.Name.LocalName == "RadioButton").Select(e => Resolve((string?)e.Attribute("Content")) ?? "").ToList() ?? new();
            if (!nav.SequenceEqual(headers)) bad.Add($"a lista de grupos (SetNav: {string.Join(", ", nav)}) não é a dos grupos da página");
            foreach (var e in settings.Descendants().Where(e => Inputs.Contains(e.Name.LocalName) && e.Attribute(X + "Name") != null && !InTemplate(e)))
            {
                var n = (string)e.Attribute(X + "Name")!;
                var g = e.Ancestors().FirstOrDefault(IsSection) is { } s ? Header(s) : "(sem grupo)";
                if (!want.TryGetValue(n, out var wg)) bad.Add($"{n} em {g} não está na tabela");
                else if (wg != g) bad.Add($"{n} está em {g}, a tabela diz {wg}");
            }
            foreach (var (o, g) in want)
                if (settings.Descendants().All(e => (string?)e.Attribute(X + "Name") != o)) bad.Add($"{o} ({g}) não está em Settings");
            check("Organização: cada opção de Settings está no grupo que o DESIGN.md diz", bad.Count == 0, string.Join("; ", bad));
        }

        // ---------------- 3. nomes. Uma secção só usa o nome de outra página quando é a lista desta (Profiles > Accounts tem
        // o ProfList, as contas a que o passo dá o aspeto)
        var names = new List<string>();
        foreach (var (page, h, el) in all)
        {
            if (h.Contains('(') || h.Split(' ').Length > 5) names.Add($"{page} > {h}: parênteses ou mais de 5 palavras");
            var list = pages.First(r => r[0] == page)[4];
            if (PageNames.Contains(h) && h != page && !el.Descendants().Any(d => list.Length > 0 && (string?)d.Attribute(X + "Name") == list))
                names.Add($"{page} > {h}: é o nome de outra página e não é a lista desta");
        }
        names.AddRange(all.GroupBy(g => g.header).Where(x => x.Count() > 1).Select(x => $"{x.Key} repete-se ({string.Join(", ", x.Select(y => y.page))})"));
        check("Organização: os nomes das secções são curtos, sem parênteses e únicos", names.Count == 0, string.Join("; ", names));

        // ---------------- 4. secções sem caixa; o vermelho no fim da fila; em Settings, no máximo 7 coisas por grupo.
        // 5. só texto, só o How it works da Connection
        static bool Box(string? t) => t != null && t.Split(',').Select(x => x.Trim()).ToList() is var p && p.Distinct().Count() == 1
                                       && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0;
        IEnumerable<XElement> Styles(XElement res) => res.Elements().Concat(res.Elements().Where(r => r.Name.LocalName == "ResourceDictionary").SelectMany(d => d.Elements()))
            .Where(s => s.Name.LocalName == "Style");
        XElement? StyleOf(XElement at, string key) =>
            at.Ancestors().SelectMany(a => a.Elements().Where(r => r.Name.LocalName.EndsWith(".Resources")).SelectMany(Styles))
              .Concat(theme.Root!.Elements().Where(s => s.Name.LocalName == "Style"))
              .FirstOrDefault(s => (string?)s.Attribute(X + "Key") == key);
        // o vermelho é o último da sua fila: num DockPanel o primeiro encostado à direita, nos outros painéis o último botão
        static bool LastInRow(XElement b)
        {
            var btns = b.Parent!.Elements().Where(e => e.Name.LocalName == "Button").ToList();
            if (b.Parent.Name.LocalName == "DockPanel" && btns.FirstOrDefault(e => (string?)e.Attribute("DockPanel.Dock") == "Right") is { } r) return r == b;
            return btns.Last() == b;
        }
        var boxes = new List<string>();
        var textOnly = new List<string>();
        var danger = new List<string>();
        var big = new List<string>();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null || !groupsOf.TryGetValue(row[0], out var groups)) continue;
            foreach (var g in groups)
            {
                var h = Header(g);
                var styled = g.Name.LocalName == "Grid" ? g.Descendants().First(d => Key(d) == "ProfStepTitle") : g;
                var def = Key(styled) is { } k ? StyleOf(styled, k) : null;
                if (def?.Descendants().Any(d => d.Name.LocalName == "Border" && Box((string?)d.Attribute("BorderThickness"))) == true
                    || def?.Descendants().Any(d => (string?)d.Attribute("Property") == "BorderThickness" && Box((string?)d.Attribute("Value"))) == true
                    || g.AncestorsAndSelf().TakeWhile(a => a != page).Any(a => Box((string?)a.Attribute("BorderThickness"))))
                    boxes.Add($"{row[0]} > {h}" + (def == null ? "" : $" ({Key(styled)})"));
                var mine = g.Descendants().Where(d => !InTemplate(d) && d.Ancestors().First(IsSection) == g).ToList();
                if (!g.Descendants().Any(d => Doers.Contains(d.Name.LocalName)) && !(h == "How it works" && row[0] == "Connection")) textOnly.Add($"{row[0]} > {h}");
                var n = mine.Count(d => d.Name.LocalName == "Button" || Inputs.Contains(d.Name.LocalName));
                if (row[0] == "Settings" && n > 7) big.Add($"{h}: {n}");
            }
            foreach (var red in page.Descendants().Where(d => !InTemplate(d) && d.Name.LocalName == "Button" && Key(d) is "Danger" or "DangerLink"))
                if (!LastInRow(red)) danger.Add($"{row[0]} > {Resolve((string?)red.Attribute("Content")) ?? (string?)red.Attribute(X + "Name")}");
        }
        check("Organização: as secções são um título com uma linha fina, não caixas", boxes.Count == 0, string.Join("; ", boxes));
        check("Organização: não há secções só de texto (fora o How it works da Connection)", textOnly.Count == 0, string.Join("; ", textOnly));
        check("Organização: o botão vermelho é o último da sua fila", danger.Count == 0, string.Join("; ", danger));
        check("Organização: nenhum grupo de Settings tem mais de 7 coisas para clicar ou preencher", big.Count == 0, string.Join("; ", big));

        // ---------------- 6. moldura (a do elemento que leva o título: 24 nos lados, 16 em cima, o fundo da tabela), o título
        // e a lista
        var frames = new List<string>();
        var titles = new List<string>();
        var lists = new List<string>();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null) continue;
            var title = page.Descendants().FirstOrDefault(e => e.Name.LocalName == "TextBlock" && !InTemplate(e));
            if (title == null || Resolve((string?)title.Attribute("Text")) != row[0] || (string?)title.Attribute("FontSize") != "22"
                || (string?)title.Attribute("FontFamily") != "{StaticResource DisplayFont}")
                titles.Add($"{row[0]}: {(string?)title?.Attribute("Text") ?? "sem texto"}");
            var f = title?.AncestorsAndSelf().TakeWhile(a => a != page.Parent).Select(a => (string?)a.Attribute("Margin") ?? (string?)a.Attribute("Padding"))
                         .FirstOrDefault(v => v != null && v.Split(',').Length == 4 && v.Replace(" ", "") != "0,0,0,0");
            if (f == null || !f.StartsWith("24,16,") || f != row[3]) frames.Add($"{row[0]}: {f} (quer {row[3]})");
            if (row[4].Length == 0) continue;
            var list = ById(row[4]);
            var search = page.Descendants().FirstOrDefault(e => e.Name.LocalName == "TextBox" && !InTemplate(e) && ((string?)e.Attribute(X + "Name"))?.EndsWith("Search") == true);
            var order = page.Descendants().ToList();
            if (list == null || !list.Ancestors().Contains(page) || InTemplate(list)) lists.Add($"{row[0]}: falta {row[4]}");
            else if (title != null && order.IndexOf(list) < order.IndexOf(title)) lists.Add($"{row[0]}: {row[4]} antes do título");
            else if (search != null && order.IndexOf(list) < order.IndexOf(search)) lists.Add($"{row[0]}: {row[4]} antes de {(string?)search.Attribute(X + "Name")}");
        }
        check("Organização: cada página tem 24 px nos lados e 16 em cima, e o fundo que o DESIGN.md diz", frames.Count == 0, string.Join("; ", frames));
        check("Organização: cada página abre com o seu nome (22 px, Display)", titles.Count == 0, string.Join("; ", titles));
        check("Organização: cada página de lista tem a sua lista, por baixo do título e da pesquisa", lists.Count == 0, string.Join("; ", lists));

        // ---------------- 7. "Settings > X" e "Página > X" apontam para o que existe
        var labels = new Dictionary<string, List<string>>();
        var menus = doc.Descendants().Where(e => e.Name.LocalName == "MenuItem").Select(e => (string?)e.Attribute("Header")).OfType<string>().ToList();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null) continue;
            labels[row[0]] = page.DescendantsAndSelf().SelectMany(e => new[] { "Header", "Text", "Content", "Tag", "AutomationProperties.Name" }.Select(a => Resolve((string?)e.Attribute(a))))
                .OfType<string>().Where(s => s.Length > 1 && !s.StartsWith('{')).Concat(row[0] == "Settings" ? external : menus).Distinct().ToList();
        }
        static IEnumerable<string> Strings(XElement el) => el.DescendantsAndSelf().SelectMany(e => new[] { "Header", "Text", "Content", "Tag", "AutomationProperties.Name" }.Select(a => Resolve((string?)e.Attribute(a))))
            .OfType<string>().Where(s => s.Length > 1 && !s.StartsWith('{'));
        // a referência corta no "(", por isso "Regions > Allow all (block off)" chega como "Allow all" e vale o nome inteiro
        // do botão (antes passava por acaso, pela coluna "Allow" da página antiga)
        static bool Starts(string rest, IEnumerable<string> ls) =>
            ls.Any(l => rest.StartsWith(l, StringComparison.Ordinal) && (rest.Length == l.Length || !char.IsLetterOrDigit(rest[l.Length]))
                        || l.StartsWith(rest + " (", StringComparison.Ordinal));
        var refRx = new Regex(@"\b(Settings|Connection|Servers|Regions|Accounts|Configs|Profiles|Log)(?: page)? (?:>|›) ([A-Z][^\n.,;:()""<]*)");
        var stale = new List<string>();
        void Scan(string rel, IEnumerable<string> texts)
        {
            foreach (var t in texts)
                foreach (Match m in refRx.Matches(WebUtility.HtmlDecode(t)))
                {
                    var (pg, rest) = (m.Groups[1].Value, m.Groups[2].Value.TrimEnd());
                    if (!labels.TryGetValue(pg, out var ls)) continue;
                    // Página > Secção > Opção: a opção tem de estar nessa secção
                    var parts = rest.Split(" > ", 2);
                    var grp = parts.Length == 2 ? groupsOf.GetValueOrDefault(pg)?.FirstOrDefault(g => Header(g) == parts[0]) : null;
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

        // ---------------- 8. na app, aponta-se com nomes, não com sítios: nada de "on the right" nem "<Secção> above"
        var headersAll = all.Select(g => Regex.Escape(g.header)).Distinct();
        var place = new Regex(@"\bon the (right|left)\b|\b(" + string.Join("|", headersAll) + @") (above|below)\b");
        var where = xamlAttrs.SelectMany(t => place.Matches(t).Select(m => m.Value)).Distinct().ToList();
        check("Organização: os textos da app apontam com nomes (Página > Secção), não com above, below ou on the right", where.Count == 0, string.Join("; ", where));

        // ---------------- 9. as cores da rota só no que está vivo: nas páginas, só na rota acesa da Connection (RouteOn). A linha
        // do topo, a marca da página escolhida, o aviso (toast) e o arco do mostrador de Regions ficam fora das páginas ou no código
        var route = new List<string>();
        foreach (var row in pages)
        {
            var page = ById(row[1]);
            if (page == null) continue;
            foreach (var e in page.DescendantsAndSelf().Where(e => !e.AncestorsAndSelf().Any(a => (string?)a.Attribute(X + "Name") == "RouteOn")))
                foreach (var a in e.Attributes())
                    foreach (Match m in RouteColours.Matches(a.Value))
                        route.Add($"{row[0]}: {m.Groups[1].Value} em {(string?)e.Attribute(X + "Name") ?? e.Name.LocalName}");
        }
        check("Organização: as cores da rota só aparecem nas páginas na rota acesa da Connection", route.Count == 0, string.Join("; ", route.Distinct().Take(8)));
    }

    // 10 nov.: os textos novos vêm de Texts ({x:Static local:Texts.Nome}), para a tradução; lidos como a janela os mostra
    static string? Resolve(string? s) => Regex.Match(s ?? "", @"^\{x:Static local:Texts\.(\w+)\}$") is { Success: true } m
        ? typeof(SRT.Texts).GetField(m.Groups[1].Value)?.GetValue(null) as string : s;

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
