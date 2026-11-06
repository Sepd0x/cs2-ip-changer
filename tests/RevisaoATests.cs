using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área A: topo, proteção, telemóvel): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoATests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão A: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
    }
}
