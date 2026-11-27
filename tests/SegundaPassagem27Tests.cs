#nullable enable
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using SRT;
using SRT.Core;

// 27 nov.: a segunda passagem da auditoria do Codex (ServerRouteToolCodex/CODEX-RELATORIO.md), A1, A9, A14 e B2 a B7, um teste por
// achado. B1 (o Worker) está em worker/stats/test/apagar-resumo-27nov.test.mjs.
static class SegundaPassagem27Tests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    public static void Run(Action<string, bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("passagem27-").FullName;

        void Case(string name, Func<bool> run)
        {
            try { check(name, run(), ""); }
            catch (Exception e) { check(name, false, e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message); }
        }

        // A1: só a decisão de confiança da janela, sem WPF, timers nem dispositivos
        Case("Passagem 27 A1: um iPhone de uma sessão anterior não confirma o Android USB de agora", () =>
        {
            var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            typeof(MainWindow).GetField("_s", Instance)!.SetValue(window, new Settings { PhoneIsIphone = true });
            var report = new GuardReport { HasHome = false, Candidate = new Adapter { IsPhone = true, Description = "Remote NDIS based Internet Sharing Device" } };
            return (bool)typeof(MainWindow).GetMethod("MobileUnconfirmedNow", Instance)!.Invoke(window, new object[] { report })!;
        });
        Case("Passagem 27 A1: um iPhone no cabo agora continua a não ser bloqueado", () =>
        {
            var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            typeof(MainWindow).GetField("_s", Instance)!.SetValue(window, new Settings());
            typeof(MainWindow).GetField("_appleOnUsb", Instance)!.SetValue(window, true);
            var report = new GuardReport { HasHome = false, Candidate = new Adapter { IsPhone = true, Description = "Remote NDIS based Internet Sharing Device" } };
            return !(bool)typeof(MainWindow).GetMethod("MobileUnconfirmedNow", Instance)!.Invoke(window, new object[] { report })!;
        });

        // A9: a troca do exe pede ao Windows, antes de mexer, que ponha o .old de volta no nome de sempre ao reiniciar
        Case("Passagem 27 A9: antes da primeira mudança de nome fica pedido o regresso do .old ao nome de sempre", () =>
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var exe = Path.Combine(dir, "app.exe");
            var neu = Path.Combine(dir, "app.new.exe");
            var old = exe + ".old";
            File.WriteAllText(exe, "velha");
            File.WriteAllText(neu, "nova");
            (string From, string To, bool ExeThere)? asked = null;
            Updater.Swap(exe, neu, old, (from, to) => { asked = (from, to, File.ReadAllText(exe) == "velha"); return true; });
            return asked is { } a && a.From == old && a.To == exe && a.ExeThere
                && File.ReadAllText(exe) == "nova" && File.ReadAllText(old) == "velha" && !File.Exists(neu);
        });
        Case("Passagem 27 A9: o pedido ao Windows nunca substitui um ficheiro (MOVEFILE_DELAY_UNTIL_REBOOT sem REPLACE_EXISTING)", () =>
        {
            var updater = Src("Core", "Updater.cs");
            var install = Between(updater, "public static void Install(", "FileStream hold");
            return updater.Contains("const uint MoveFileDelayUntilReboot = 0x4;") && updater.Contains("MoveFileEx(from, to, MoveFileDelayUntilReboot)")
                && install.Contains("Swap(exe, NewExe, OldExe, RestoreAtReboot);") && !install.Contains("File.Move(exe, OldExe)");
        });

        // A14
        Case("Passagem 27 A14: uma linha antiga com o IP de casa inteiro sai escondida no relatório, mesmo depois de o IP mudar", () =>
        {
            var text = Report.HideHomeLines("12:00 The phone connection IP (192.0.2.11) is the same as your home IP. Servers and CS2 blocked.\n"
                + "12:01 The phone's IP (192.0.2.12) is the same as your home IP: it is sharing your Wi-Fi.\n12:02 Phone connection checked: servers see 203.0.113.9.");
            return text.Contains("Servers and CS2 blocked") && !text.Contains("192.0.2.11") && !text.Contains("192.0.2.12") && text.Contains("203.0.113.9")
                && Src("Core", "Report.cs").Contains("HideSsids(HideHomeLines(text))");
        });

        // B2: contratos de leitura; os handlers chamam o Steam e a Firewall, por isso não são invocados aqui
        var servers = Src("Views", "MainWindow.Servers.cs");
        Case("Passagem 27 B2: Join volta a ver Cancel e a proteção depois da última espera pelo menu", () =>
        {
            var last = Between(servers, "if (ready) await Task.Delay(2000);", "Steam.OpenUrl($\"steam://connect/{s.Key}\");");
            return last.Contains("if (CommandCancelled) return false;") && last.Contains("(!_protOn || _distrust)");
        });
        Case("Passagem 27 B2: a vaga consultada ainda pertence à espera ativa (Cancel durante a consulta não entra)", () =>
        {
            var last = Between(servers, "if (!await QueryOne(s, eg) || s.MaxPlayers == 0) return;", "if (SlotAutoJoins");
            return last.Contains("if (!ReferenceEquals(_slotServer, s)) return;");
        });

        // B3
        Case("Passagem 27 B3: reparar o menu Iniciar não mexe no Pickroute.lnk de outra aplicação", () => ExistingShortcut(root, desktop: false));
        Case("Passagem 27 B3: instalar no ambiente de trabalho não mexe no Pickroute.lnk de outra aplicação", () => ExistingShortcut(root, desktop: true));
        Case("Passagem 27 B3: o atalho do menu Iniciar não mostra o nome antigo", () => !Shortcuts.Description.Contains(Shortcuts.OldName) && Shortcuts.Description.StartsWith("Pickroute"));

        // B4
        Case("Passagem 27 B4: algarismos árabes numa duração não lançam exceção (entrada que não entende)", () =>
            Fmt.ParseDuration("٢h") == null && Fmt.ParseDuration("٢") == null && Fmt.ParseDuration("2h") == TimeSpan.FromHours(2));

        // B5
        Case("Passagem 27 B5: o erro ao repor a rota predefinida do telefone entra no que ficou por repor", () =>
        {
            var onError = Between(Src("Core", "Network", "Guard.cs"), "is { Ok: false } reset)", "_fwSignature = null;");
            return onError.Contains("left.Add(");
        });

        // B6
        Case("Passagem 27 B6: a 574 de altura as oito opções e o botão Update cabem na barra", () =>
        {
            var (row, gap) = MainWindow.NavFit(574, 24 + 8);
            return 74 + gap + 8 * row + 25 + 24 + 8 <= 574 && row >= 48 && MainWindow.NavFit(658) == (66, 24)
                && Src("Views", "MainWindow.Update.cs").Contains("FitSideNav();") && Src("Views", "MainWindow.Window.cs").Contains("NavFit(SideBar.ActualHeight, UpdateBtnRoom())");
        });

        // B7
        Case("Passagem 27 B7: o restauro recusa uma cópia incompleta antes de dizer que repôs", () => RestoreIncompleteBackup(root));
    }

    static string Between(string source, string start, string end)
    {
        var at = source.IndexOf(start, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("Rever o ensaio de leitura com a implementação atual.");
        at += start.Length;
        var until = source.IndexOf(end, at, StringComparison.Ordinal);
        if (until < 0) throw new InvalidOperationException("Rever o fim do ensaio de leitura.");
        return source[at..until];
    }

    static bool ExistingShortcut(string root, bool desktop)
    {
        var folder = Path.Combine(root, desktop ? "desktop" : "programas");
        var installed = Path.Combine(root, "instalada");
        Directory.CreateDirectory(installed);
        var exe = Path.Combine(installed, "app.exe");
        var other = Path.Combine(root, "outra.exe");
        File.WriteAllBytes(exe, Array.Empty<byte>());
        File.WriteAllBytes(other, Array.Empty<byte>());
        var link = Path.Combine(folder, Shortcuts.Name + ".lnk");
        Shortcuts.Make(link, other);
        if (!Shortcuts.SameFile(Shortcuts.Target(link), other)) throw new IOException("Atalho de controlo inválido.");
        if (desktop) Shortcuts.AddDesktop(exe, folder);
        else Shortcuts.KeepStartMenu(exe, folder, installed);
        return Shortcuts.SameFile(Shortcuts.Target(link), other) && Shortcuts.RemoveAll(exe, new[] { folder }) == 0 && File.Exists(link);
    }

    static bool RestoreIncompleteBackup(string root)
    {
        var store = new Cs2ConfigStore(Path.Combine(root, "userdata"), Path.Combine(root, "configs")) { Cs2Running = () => false };
        var id = SteamIds.FromId32(51);
        var rel = Cs2Config.PathsOf(CfgPart.Keys).Local;
        var current = Path.Combine(store.Dir(id), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(current, "atual");
        var backupDir = Directory.CreateDirectory(Path.Combine(root, "configs", "incompleta")).FullName;
        var backup = new CfgBackup { Dir = backupDir, SteamId = id, Files = new() { rel } };
        try { return !store.Restore(backup, "restauro") && File.ReadAllText(current) == "atual"; }
        catch (IOException ex) { return File.ReadAllText(current) == "atual" && ex.Message.Contains("missing from this backup"); }
    }
}
