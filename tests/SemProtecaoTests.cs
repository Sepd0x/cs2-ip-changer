using System.IO;
using System.Text.Json;
using SRT;
using SRT.Core;

// 6 nov.: Settings > Use the app with protection off. Com a proteção desligada e esta opção ligada, Servers, Join e Log in com o CS2
// funcionam pela ligação de casa e nada é bloqueado; ligá-la mostra um aviso primeiro. Desligada (o normal), tudo como antes.
static class SemProtecaoTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");

        check("Sem proteção: a opção vem desligada", !new Settings().UseWithProtectionOff, "");
        check("Sem proteção: um settings antigo (sem o campo) fica desligado", !JsonSerializer.Deserialize<Settings>("{\"Mode\":\"servers\"}").UseWithProtectionOff, "");
        check("Sem proteção: a opção guarda-se", JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { UseWithProtectionOff = true })).UseWithProtectionOff, "");

        // HomeOnPurpose(noPhone, protOn, useWithOff)
        check("Sem proteção: desligada, com a proteção desligada continua a esperar pela proteção", !MainWindow.HomeOnPurpose(false, false, false), "");
        check("Sem proteção: ligada e proteção desligada, usa a ligação de casa", MainWindow.HomeOnPurpose(false, false, true), "");
        check("Sem proteção: com a proteção ligada nunca usa casa, mesmo com a opção", !MainWindow.HomeOnPurpose(false, true, true) && !MainWindow.HomeOnPurpose(false, true, false), "");
        check("Sem proteção: No phone continua a usar casa como antes", MainWindow.HomeOnPurpose(true, false, false) && MainWindow.HomeOnPurpose(true, true, false), "");

        var servers = Src("Views", "MainWindow.Servers.cs");
        check("Sem proteção: o Join só pergunta para ligar a proteção quando a opção está desligada", servers.Contains("if (!_protOn && !OnHome)"), "");
        check("Sem proteção: o Join não exige o servidor protegido quando a opção está ligada", servers.Contains("if (!OnHome && s.Protected != true && !fullOk)"), "");
        check("Sem proteção: os servidores são perguntados por casa", servers.Contains("OnHome && _last?.Unprotected == true ? HomeAdapter"), "");
        check("Sem proteção: o Log in abre o CS2", Src("Views", "MainWindow.Accounts.cs").Contains("if (cs2 && !_s.LockCs2 && !OnHome &&"), "");

        var settings = Src("Views", "MainWindow.Settings.cs");
        check("Sem proteção: ligar a opção pergunta primeiro, com o aviso", settings.Contains("!await Ask(\"Use the app with protection off?\", UseOffWarning"), "");
        check("Sem proteção: recusar o aviso deixa a opção desligada", settings.Contains("UseOffBox.IsChecked = false;"), "");
        check("Sem proteção: o aviso diz que os servidores veem o IP de casa", MainWindow.UseOffWarning.Contains("sees your home IP"), "");
        // New IP + next account com a proteção desligada: não diz que troca o IP quando não troca
        check("Switch: com a proteção ligada troca o IP", MainWindow.IpStep(true, false) == MainWindow.IpWhenOff.Change && MainWindow.IpStep(true, true) == MainWindow.IpWhenOff.Change, "");
        check("Switch: desligada e com a opção, salta o IP (os servidores veem o de casa)", MainWindow.IpStep(false, true) == MainWindow.IpWhenOff.Skip, "");
        check("Switch: desligada sem a opção, pergunta para ligar a proteção primeiro", MainWindow.IpStep(false, false) == MainWindow.IpWhenOff.AskTurnOn, "");
        var kick = Src("Views", "MainWindow.Kick.cs");
        check("Switch: o passo do IP decide-se antes de começar", kick.IndexOf("IpStep(_protOn, _s.UseWithProtectionOff) is var step") is var i1 && i1 > 0 && i1 < kick.IndexOf("ChangeIp(confirm: false)"), "");
        check("Switch: o nome do botão tira o New IP quando o IP não muda", Src("Views", "MainWindow.Accounts.cs").Contains("IpStep(_protOn, _s.UseWithProtectionOff) == IpWhenOff.Skip)) c = c with { Ip = false }"), "");
        check("Switch: o botão atualiza quando a proteção muda", Src("Views", "MainWindow.Protection.cs").Contains("UpdateQuickInfo();     // New IP + next account"), "");

        // Cancel no switch: para depois do passo, nunca a meio
        check("Cancel: à espera, cancela já", MainWindow.CancelFor(true, true, false, false, false) == MainWindow.CancelKind.Now, "");
        check("Cancel: a meio da troca de IP num switch, para depois do passo", MainWindow.CancelFor(true, false, true, true, false) == MainWindow.CancelKind.AfterStep
              && MainWindow.CancelFor(true, true, true, true, false) == MainWindow.CancelKind.AfterStep, "");
        check("Cancel: um Change IP sozinho não se corta a meio", MainWindow.CancelFor(true, false, true, false, false) == MainWindow.CancelKind.None, "");
        check("Cancel: sem comando ou já cancelado, não aparece", MainWindow.CancelFor(false, true, false, true, false) == MainWindow.CancelKind.None && MainWindow.CancelFor(true, true, false, true, true) == MainWindow.CancelKind.None, "");
        check("Cancel: o switch para depois do IP e depois do login", kick.Contains("if (StopAfterStep(ip ? \"the new IP\" : null)) return false;") && kick.Contains("if (StopAfterStep(next != null ?"), "");
        check("Cancel: cada comando começa sem o pedido de parar do anterior", Src("Views", "MainWindow.Activity.cs").Contains("_stopAfterStep = false;"), "");
        check("Sem proteção: o interruptor está em Settings > Protection", Src("MainWindow.xaml").Contains("x:Name=\"UseOffBox\" Style=\"{StaticResource Switch}\" Click=\"UseOff_Click\""), "");
    }
}
