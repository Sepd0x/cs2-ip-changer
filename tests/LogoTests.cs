using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// O logo (6 nov.): um só desenho em todo o lado. Falha quando o ícone da app (src/app.ico), o do README e do site
// (docs/assets/icon.png), o favicon, o canto da janela e o cartão social deixam de ser o mesmo logo, ou quando o logo ganha
// fundo, brilho, máscaras ou cores fora do Theme.xaml. Feito por node tools/logo.mjs; regras em docs/DESIGN.md (Logo).
static class LogoTests
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    // the frames of an .ico: size -> raw bytes of the image (PNG or DIB)
    static Dictionary<int, byte[]> Frames(byte[] ico)
    {
        var d = new Dictionary<int, byte[]>();
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1) return d;
        int n = BitConverter.ToUInt16(ico, 4);
        for (int i = 0; i < n; i++)
        {
            int e = 6 + 16 * i, w = ico[e] == 0 ? 256 : ico[e];
            int len = BitConverter.ToInt32(ico, e + 8), off = BitConverter.ToInt32(ico, e + 12);
            if (off + len <= ico.Length) d[w] = ico[off..(off + len)];
        }
        return d;
    }

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string P(string rel) => Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
        string Txt(string rel) => File.ReadAllText(P(rel)).Replace("\r\n", "\n");
        byte[] Bin(string rel) => File.Exists(P(rel)) ? File.ReadAllBytes(P(rel)) : Array.Empty<byte>();

        var appIco = Bin("src/app.ico");
        check("Logo: src/app.ico é o docs/assets/app.ico", appIco.Length > 0 && appIco.SequenceEqual(Bin("docs/assets/app.ico")), $"{appIco.Length} bytes");
        check("Logo: o exe usa o src/app.ico", Txt("src/ServerRouteTool.csproj").Contains("<ApplicationIcon>app.ico</ApplicationIcon>"), "");
        var frames = Frames(appIco);
        check("Logo: o .ico tem 16, 20, 24, 32, 40, 48, 64 e 256", Sizes.All(frames.ContainsKey), string.Join(",", frames.Keys));
        check("Logo: o 256 do .ico é o docs/assets/icon.png (README e site)", frames.TryGetValue(256, out var f256) && f256.SequenceEqual(Bin("docs/assets/icon.png")), "");
        var fav = Frames(Bin("docs/assets/favicon.ico"));
        check("Logo: o favicon.ico é o 16, 32 e 48 do ícone da app", new[] { 16, 32, 48 }.All(s => fav.TryGetValue(s, out var a) && frames.TryGetValue(s, out var b) && a.SequenceEqual(b)),
            string.Join(",", fav.Keys));

        var logo = Txt("docs/assets/logo.svg");
        check("Logo: o favicon.svg é o logo.svg", Txt("docs/assets/favicon.svg") == logo, "");
        // sem fundo, sem brilho, sem máscaras nem degradês: forma solta de peças lisas
        var banned = new[] { "<mask", "<filter", "Gradient", "<image", "<text", "<rect", "opacity" }.Where(logo.Contains).ToList();
        check("Logo: sem fundo, brilho, máscaras ou degradês", banned.Count == 0, string.Join(", ", banned));
        var theme = Txt("src/Theme.xaml");
        string Key(string k) => "#" + Regex.Match(theme, $"<Color x:Key=\"{k}\">#([0-9A-Fa-f]{{6}})</Color>").Groups[1].Value.ToLowerInvariant();
        var want = new[] { Key("AccentColor"), Key("AccentDeepColor"), Key("Top3") }.ToHashSet();
        foreach (var f in new[] { "logo.svg", "logo-16.svg", "logo-24.svg", "favicon.svg" })
        {
            var fills = Regex.Matches(Txt("docs/assets/" + f), "fill=\"(#[0-9a-fA-F]{6})\"").Select(m => m.Groups[1].Value.ToLowerInvariant()).ToHashSet();
            check($"Logo: {f} só usa AccentColor, AccentDeepColor e Top3 do Theme.xaml", fills.SetEquals(want), string.Join(" ", fills));
        }
        var black = Regex.Matches(Txt("docs/assets/logo-black.svg"), "fill=\"(#[0-9a-fA-F]{6})\"").Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();
        check("Logo: há a versão a uma cor (preto)", black.SequenceEqual(new[] { "#000000" }), string.Join(" ", black));

        // o canto da janela desenha o logo de 24 px, com as cores do tema
        var xaml = Txt("src/MainWindow.xaml");
        var side = Regex.Match(xaml, "<Image x:Name=\"SideLogo\"[\\s\\S]*?</Image>").Value;
        var l24 = Txt("docs/assets/logo-24.svg");
        var paths = Regex.Matches(side, "Geometry=\"(M[0-9][^\"]+)\"").Select(m => m.Groups[1].Value).Where(g => !g.StartsWith("M0,0")).ToList();
        check("Logo: o canto da janela (SideLogo) desenha o logo-24.svg", paths.Count == 2 && paths.All(p => l24.Contains($"d=\"{p}\"")), paths.Count.ToString());
        check("Logo: o canto da janela usa as cores do tema", side.Length > 0 && !Regex.IsMatch(side, "#[0-9A-Fa-f]{6}") && side.Contains("AccentDeepColor") && side.Contains("Top3"), "");

        // o README, o site (cada página e o modelo dos guias) e o cartão social
        check("Logo: o README mostra o docs/assets/icon.png", Txt("README.md").Contains("src=\"docs/assets/icon.png\""), "");
        var pages = new[] { "site/index.html", "site/404.html" }
            .Concat(Directory.GetDirectories(P("site")).Where(d => File.Exists(Path.Combine(d, "index.html"))).Select(d => "site/" + Path.GetFileName(d) + "/index.html"));
        var noFav = pages.Append("tools/site-pages.mjs").Where(p => { var t = Txt(p); return !t.Contains("img/favicon.svg\" type=\"image/svg+xml\"") || !t.Contains("img/favicon.ico\"") || !t.Contains("img/apple-touch-icon.png\""); }).ToList();
        check("Logo: cada página do site tem o favicon.svg, o favicon.ico e o apple-touch-icon", noFav.Count == 0, string.Join(", ", noFav));
        var pagesYml = Txt(".github/workflows/pages.yml");
        check("Logo: o Pages copia o icon.png e os favicons para img/", new[] { "docs/assets/icon.png", "docs/assets/favicon.svg", "docs/assets/favicon.ico", "docs/assets/apple-touch-icon.png" }.All(pagesYml.Contains), "");
        check("Logo: o cartão social assina com o logo.svg", Txt("docs/assets/src/social-preview.html").Contains("src=\"../logo.svg\""), "");
        check("Logo: o components.mjs já não escreve um logo.svg antigo", !Txt("docs/assets/src/components.mjs").Contains("write('logo.svg'"), "");
    }
}
