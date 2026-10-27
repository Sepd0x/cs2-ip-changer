using System.Diagnostics;

namespace SRT.Core;

/// <summary>
/// Diagnostic points scattered through the app. In the normal build they do nothing: the compiler removes the calls
/// (and their arguments) because the PRIVADO symbol is not defined. The development build turns them on.
/// </summary>
public static partial class Diag
{
    [Conditional("PRIVADO")]
    public static void Write(string kind, object data = null) => Gravar(kind, data);

    static partial void Gravar(string kind, object data);
}
