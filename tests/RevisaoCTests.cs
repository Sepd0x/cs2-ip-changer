using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área C: servidores e regiões): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoCTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão C: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
    }
}
