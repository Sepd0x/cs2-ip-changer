using System.IO;
using SRT;
using SRT.Core;

// 7 nov.: o que o dono trouxe depois de usar a beta.12, parte da app (tempo dos jogadores, miras em Configs, o zip de diagnóstico).
// Cada correção leva aqui o seu teste, que falhava antes.
static class Beta13AppTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        check("Beta13 app: o repositório encontra-se", Directory.Exists(Path.Combine(repo, "src")), repo);
    }
}
