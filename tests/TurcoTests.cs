using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 24 nov.: a app em turco (src/Lang/tr.json). 8 de 33 relatórios têm o Windows em turco, a segunda língua dos utilizadores.
// As chaves completas e os marcadores iguais ao inglês já os vê o LocTests (o turco está na lista dele); aqui: a escolha pela língua do
// Windows, o seletor, os plurais (o turco mantém o nome no singular depois de um número) e o que o turco não pode ter.
static class TurcoTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var forced = Environment.GetEnvironmentVariable("SRT_LANG");
        var ui = CultureInfo.CurrentUICulture;
        var before = Loc.Lang;
        Environment.SetEnvironmentVariable("SRT_LANG", null);
        try
        {
            Escolha(check);
            Plurais(check);
            Texto(check);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            Environment.SetEnvironmentVariable("SRT_LANG", forced);
            Loc.Init(before);
        }
    }

    static void Escolha(Action<string, bool, string> check)
    {
        check("Turco: o seletor tem Türkçe, com um nome por código", Loc.Codes.Contains("tr") && Loc.Names.Length == Loc.Codes.Length && Loc.Names[Array.IndexOf(Loc.Codes, "tr")] == "Türkçe", string.Join(",", Loc.Names));
        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        check("Turco: o Windows em turco escolhe tr sozinho (auto e sem escolha)", Loc.Resolve("auto") == "tr" && Loc.Resolve(null) == "tr", Loc.Resolve("auto"));
        check("Turco: uma escolha guardada ganha ao Windows (en, pt-BR, tr em maiúsculas)", Loc.Resolve("en") == "en" && Loc.Resolve("pt-BR") == "pt-BR" && Loc.Resolve("TR") == "tr", Loc.Resolve("en"));
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        check("Turco: o Windows em inglês não escolhe tr, e tr guardado continua a valer", Loc.Resolve("auto") == "en" && Loc.Resolve("tr") == "tr", Loc.Resolve("auto"));
        CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
        check("Turco: as outras línguas continuam a escolher-se como antes (ru)", Loc.Resolve("auto") == "ru", Loc.Resolve("auto"));
    }

    static void Plurais(Action<string, bool, string> check)
    {
        var tr = Loc.Load("tr");
        var ru = Loc.Load("ru");
        var nouns = ru.Where(kv => kv.Value.Contains('|') && !kv.Key.Contains(',') && !kv.Key.Contains('(')).Select(kv => kv.Key).ToList();
        var gone = nouns.Where(k => !tr.TryGetValue(k, out var v) || !v.Contains('|')).ToList();
        check($"Turco: os {nouns.Count} nomes de contagem têm entrada com formas (singular|singular)", gone.Count == 0 && nouns.Count >= 15, string.Join(" | ", gone.Take(5)));
        var diff = nouns.Where(k => tr.TryGetValue(k, out var v) && v.Split('|').Distinct().Count() != 1 && k != "more time").ToList();
        check("Turco: depois de um número o nome fica no singular (as duas formas são iguais)", diff.Count == 0, string.Join(" | ", diff.Take(5)));
        Loc.Init("tr");
        var one = Fmt.Count(1, "account"); var many = Fmt.Count(3, "account"); var zero = Fmt.Count(0, "server");
        check("Turco: Fmt.Count dá '1 hesap', '3 hesap', '0 sunucu'", one == "1 hesap" && many == "3 hesap" && zero == "0 sunucu", one + " | " + many + " | " + zero);
        check("Turco: One() dá o nome sem número", Loc.One("account") == "hesap", Loc.One("account"));
    }

    static void Texto(Action<string, bool, string> check)
    {
        var tr = Loc.Load("tr");
        var repo = SecurityTests.Repo();
        var json = File.ReadAllText(Path.Combine(repo, "src", "Lang", "tr.json"));
        check("Turco: sem travessões nos textos (a regra do dono)", !tr.Values.Any(v => v.Contains('—') || v.Contains('–')), tr.Values.FirstOrDefault(v => v.Contains('—') || v.Contains('–')));
        // um texto igual ao inglês só é aceitável se for um nome, um código ou muito curto
        var same = tr.Where(kv => kv.Key == kv.Value && kv.Key.Length > 25 && kv.Key.Contains(' ') && !kv.Key.StartsWith("Apple Mobile Device") && !kv.Key.StartsWith("FidelityFX") && !kv.Key.Contains("RegexOptions") && !kv.Key.Contains("///")).Select(kv => kv.Key).ToList();
        check("Turco: nenhuma frase longa ficou por traduzir", same.Count == 0, same.Count + ": " + string.Join(" | ", same.Take(3)));
        var empty = tr.Count(kv => string.IsNullOrWhiteSpace(kv.Value));
        check("Turco: nenhum valor vazio", empty == 0 && !json.Contains("\": \"\""), empty.ToString());
        // o 'İ' e o 'ı' do turco passam intactos (o ficheiro é UTF-8)
        check("Turco: a letra İ, ı, ş, ğ está nos textos em UTF-8", json.Contains('ı') && json.Contains('ş') && json.Contains('ğ') && json.Contains('İ'), "");
        // a mudança de língua no turco: o aviso do Settings > App existe
        check("Turco: o aviso de língua mudada está traduzido", tr.ContainsKey("Language changed. Close and open the app to apply it.") && Regex.IsMatch(tr["Language changed. Close and open the app to apply it."], "[a-zçğıöşü]"), "");
    }
}
