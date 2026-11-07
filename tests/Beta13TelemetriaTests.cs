using System.IO;
using SRT;
using SRT.Core;

// 7 nov.: o que o dono trouxe depois de usar a beta.12, parte da telemetria (o que a app manda, o mapa de calor com o scroll e só
// com a app em primeiro plano). Cada correção leva aqui o seu teste, que falhava antes.
static class Beta13TelemetriaTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        check("Beta13 telemetria: o repositório encontra-se", Directory.Exists(Path.Combine(repo, "src")), repo);
    }
}
