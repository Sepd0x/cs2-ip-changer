#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 26 nov.: o GUI novo "Dial". O rato num raio do mostrador acende a linha dessa região na lista, como o rato na
// linha já acendia o raio.
static class GuiDialTests
{
    public static void Run(Action<string, bool, string> check)
    {
        RegionItem Row(string id, string city) => new() { Info = new RegionInfo(id, city, "", "Germany", "Europe", new[] { id }, new[] { "155.133.226.1" }) };
        var fra = Row("frankfurt-germany", "Frankfurt");
        var mad = Row("madrid-spain", "Madrid");
        var rows = new[] { fra, mad };
        var changed = new List<string>();
        fra.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        RegionDial.MarkHot(rows, "frankfurt-germany");
        check("Dial: o rato num raio acende só a linha dessa região", fra.Hot && !mad.Hot && changed.Contains(nameof(RegionItem.Hot)), $"{fra.Hot} {mad.Hot}");
        RegionDial.MarkHot(rows, "madrid-spain");
        check("Dial: noutro raio, a linha anterior apaga-se", !fra.Hot && mad.Hot, $"{fra.Hot} {mad.Hot}");
        RegionDial.MarkHot(rows, null);
        check("Dial: fora do mostrador nenhuma linha fica acesa", !fra.Hot && !mad.Hot, "");
        RegionDial.MarkHot(rows, "Europe");
        check("Dial: um continente (o rato na cabeça do grupo) não acende as linhas", !fra.Hot && !mad.Hot, "");
        RegionDial.MarkHot(null!, "madrid-spain");

        var xaml = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml"));
        var style = xaml[xaml.IndexOf("x:Key=\"RegionRowItem\"")..];
        style = style[..style.IndexOf("</Style>")];
        check("Dial: a linha acesa pelo mostrador tem o mesmo fundo que com o rato em cima",
            style.Contains("<DataTrigger Binding=\"{Binding Hot}\" Value=\"True\">"), "RegionRowItem");
        var only = xaml[xaml.IndexOf("x:Key=\"RegionOnlyLink\"")..];
        only = only[..only.IndexOf("</Style>")];
        check("Dial: a linha acesa pelo mostrador mostra o Only this region", only.Contains("Binding=\"{Binding Hot}\""), "RegionOnlyLink");
    }
}
