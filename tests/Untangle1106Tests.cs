using System.IO;
using SRT;
using SRT.Core;

// 6 nov.: a análise do untangle-codebase antes da beta.11 (publish/untangle-2026-11-06/analise.md), grupo A: o que podia partir
// já nesta versão. Hotspot com a proteção desligada, o modo de avião que deitava abaixo o hotspot, um 429 que limpava os bans,
// a nova tentativa do servidor cheio que te tirava de outro servidor, as pre-releases retiradas, e o rollback do update.
static class Untangle1106Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");

        // A4: com a proteção desligada não há egress no relatório: o nome do hotspot guardado decide
        // (beta.12, B2: a regra mudou-se para Core/Phone/PhoneLink.cs, a mesma para toda a app)
        check("Hotspot: nome guardado e sem egress (proteção desligada) conta como hotspot", PhoneLink.IsHotspot("Pixel", null), "");
        check("Hotspot: nome guardado e egress Wi-Fi conta", PhoneLink.IsHotspot("Pixel", true), "");
        check("Hotspot: egress por cabo não é hotspot, mesmo com nome guardado", !PhoneLink.IsHotspot("Pixel", false), "");
        check("Hotspot: sem nome guardado nunca é hotspot", !PhoneLink.IsHotspot(null, true) && !PhoneLink.IsHotspot("", null), "");
        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Hotspot: o Change IP usa a regra nova", phone.Contains("var hotspot = PhoneLink.IsHotspot(_s.EgressSsid, PhoneLink.Exit(_last)?.IsWifi);"), "");

        // A3: no hotspot nunca modo de avião, nem como recurso; e o método usado de facto volta para a verificação do tethering
        var adb = Src("Core", "Phone", "Adb.cs");
        check("Modo de avião: o recurso só quando é permitido", adb.Contains("if (method == Method.MobileData && allowAirplane) return await Cycle("), "");
        check("Modo de avião: o Change IP proíbe-o no hotspot", phone.Contains("allowAirplane: !hotspot);"), "");
        check("Modo de avião: o tethering volta a ligar também depois do recurso", phone.Contains("how = used;"), "");

        // A5: um 429 a meio do refresh não pode limpar os bans de uma conta
        var prof = Src("Core", "Steam", "SteamProfiles.cs");
        check("Bans: sem página nem API, o refresh conta erro e a conta fica como estava", prof.Contains("if (!bansKnown) throw new HttpRequestException(BansUnknown);"), "");
        check("Bans: a página lida marca os bans como lidos", prof.Contains("p.CommunityBan = CommunityBanRe.IsMatch(html);\n            bansKnown = true;"), "");

        // A6: a nova tentativa no servidor cheio para quando entras noutro
        check("Servidor cheio: entrar noutro servidor para a nova tentativa", MainWindow.FullRetryStops("1.2.3.4:27015", "5.6.7.8:27015"), "");
        check("Servidor cheio: entrar no mesmo não para", !MainWindow.FullRetryStops("1.2.3.4:27015", "1.2.3.4:27015"), "");
        check("Servidor cheio: sem tentativa ou sem servidor, nada muda", !MainWindow.FullRetryStops(null, "5.6.7.8:27015") && !MainWindow.FullRetryStops("1.2.3.4:27015", null), "");
        check("Servidor cheio: ligado onde o CS2 diz que entrou", Src("Views", "MainWindow.Kick.cs").Contains("if (FullRetryStops(_fullServer, _consoleServer)) _fullServer = null;"), "");

        // A1: uma release retirada (pre-release) nunca é oferecida
        var upd = Src("Core", "Updater.cs");
        check("Update: pre-releases ficam de fora", upd.Contains("if (r.TryGetProperty(\"prerelease\", out var pre) && pre.ValueKind == JsonValueKind.True) continue;"), "");
        check("Update: o RELEASING diz para pôr a release em rascunho enquanto houver cópias antigas",
            File.ReadAllText(Path.Combine(repo, "docs", "RELEASING.md")).Contains("gh release edit v<version> --draft=true"), "");

        // A2: o rollback do update nunca deixa a pasta sem exe sem o dizer
        check("Update: o rollback repõe o exe antigo em separado e escreve no Log se falhar",
            upd.Contains("try { if (!File.Exists(exe)) File.Move(OldExe, exe); }") && upd.Contains("Update rollback failed"), "");
    }
}
