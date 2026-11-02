using SRT.Core;

// beta.9: o mapa de calor dos cliques (grelha 24×16 à direita da barra lateral, como o ui-map.js do dashboard), os cliques de raiva
// e a idade da instalação em bandas
static class Beta9Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        // A captura do dashboard tem 1240×800 e a grelha começa a seguir à barra lateral de 80
        check("Calor: a barra lateral fica fora da grelha", Telemetry.Cell(40, 300, 1240, 800) == null, "");
        check("Calor: o canto de cima à esquerda da página é 0,0", Telemetry.Cell(80, 0, 1240, 800) == "0,0", Telemetry.Cell(80, 0, 1240, 800));
        check("Calor: o canto de baixo à direita é 23,15", Telemetry.Cell(1239.9, 799.9, 1240, 800) == "23,15", Telemetry.Cell(1239.9, 799.9, 1240, 800));
        check("Calor: o Change IP da captura cai na célula certa", Telemetry.Cell(163, 238, 1240, 800) == "1,4", Telemetry.Cell(163, 238, 1240, 800));
        check("Calor: a mesma posição relativa dá a mesma célula numa janela maior", Telemetry.Cell(80 + (163 - 80) * 1.5, 238 * 1.5, 80 + 1160 * 1.5, 1200) == "1,4", "");
        check("Calor: fora da janela ou janela sem tamanho dá nada",
            Telemetry.Cell(1240, 10, 1240, 800) == null && Telemetry.Cell(100, -1, 1240, 800) == null && Telemetry.Cell(100, 10, 60, 800) == null && Telemetry.Cell(100, 10, 1240, 0) == null, "");

        var t = new DateTime(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);
        var two = new List<(DateTime, string)> { (t.AddMilliseconds(-600), "3,5"), (t.AddMilliseconds(-300), "3,5") };
        check("Raiva: o 3.º clique na mesma célula em 1 s", Telemetry.IsRage(two, t, "3,5"), "");
        check("Raiva: noutra célula não conta", !Telemetry.IsRage(two, t, "4,5"), "");
        check("Raiva: 2 cliques não chegam", !Telemetry.IsRage(two.Take(1), t, "3,5"), "");
        check("Raiva: cliques com mais de 1 s não contam", !Telemetry.IsRage(new List<(DateTime, string)> { (t.AddSeconds(-2), "3,5"), (t.AddMilliseconds(-1100), "3,5") }, t, "3,5"), "");

        var bands = new[] { (0, "0"), (1, "1"), (2, "2-3"), (3, "2-3"), (4, "4-6"), (6, "4-6"), (7, "7-13"), (13, "7-13"), (14, "14-29"), (29, "14-29"), (30, "30+"), (400, "30+"), (-3, "0") };
        foreach (var (d, b) in bands) check($"Idade: {d} dias vai na banda {b}", Telemetry.AgeBand(d) == b, Telemetry.AgeBand(d));
        check("Aviso: já não promete que nada liga um dia ao seguinte (a idade em bandas liga à semana da instalação)",
            !Telemetry.NoticeText.Contains("never links") && Telemetry.NoticeText.Contains("how long ago"), "");
    }
}
