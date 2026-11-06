using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área D: Configs, Profiles, Settings e o resto): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoDTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão D: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
    }
}
