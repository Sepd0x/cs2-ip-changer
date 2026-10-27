using System.Diagnostics;

namespace SRT.Core;

/// <summary>
/// Pontos de diagnóstico espalhados pela app. Na build normal não fazem nada: o compilador tira as chamadas
/// (e os argumentos) porque o símbolo PRIVADO não está definido. A build de desenvolvimento liga-os.
/// </summary>
public static partial class Diag
{
    [Conditional("PRIVADO")]
    public static void Write(string kind, object data = null) => Gravar(kind, data);

    static partial void Gravar(string kind, object data);
}
