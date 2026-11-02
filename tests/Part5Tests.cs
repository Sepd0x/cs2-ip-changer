using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using SRT;
using SRT.Core;

// beta.8, parte 5: remover sem passos à mão (o que está em uso sai depois de a app fechar), as duas escolhas e a entrada em Aplicações instaladas
static class Part5Tests
{
    public static void Run(CoreTests.CheckFn check)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "srt-p5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try { Remove(check, tmp); Registry(check); Texts(check); }
        finally { try { SRT.Program.DeleteTree(tmp); } catch { } }
        QuitFromTray(check);
    }

    // Quit no menu da bandeja com a janela escondida: a pergunta do fecho mostra a janela. Mostrar dentro do Closing rebenta
    // (o erro do registo de 2 nov.); depois de deixar o Closing acabar (o que MainWindow.AfterCancelledClose faz), já não.
    static void QuitFromTray(CoreTests.CheckFn check)
    {
        (bool Threw, bool Shown) Try(bool yield)
        {
            (bool, bool) r = (false, false);
            var t = new Thread(() =>
            {
                var w = new System.Windows.Window { Width = 50, Height = 50, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
                w.Show(); w.Hide();
                w.Closing += async (_, e) =>
                {
                    e.Cancel = true;
                    try
                    {
                        if (yield) await System.Windows.Threading.Dispatcher.Yield();
                        w.Show();
                        r = (false, w.IsVisible);
                    }
                    catch (InvalidOperationException) { r = (true, false); }
                    _ = w.Dispatcher.BeginInvoke(() => System.Windows.Threading.Dispatcher.ExitAllFrames(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
                w.Dispatcher.BeginInvoke(() => { try { w.Close(); } catch (InvalidOperationException) { r = (true, false); System.Windows.Threading.Dispatcher.ExitAllFrames(); } });
                System.Windows.Threading.Dispatcher.Run();
            });
            t.IsBackground = true;   // never holds the test run open
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(10000);
            return r;
        }
        var direct = Try(false);
        var yielded = Try(true);
        check("Quit da bandeja: mostrar a janela dentro do Closing rebenta (o erro de 2 nov.)", direct.Threw, $"{direct}");
        check("Quit da bandeja: depois de o Closing acabar, a pergunta mostra a janela", !yielded.Threw && yielded.Shown, $"{yielded}");
    }

    static bool Admin { get { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }

    // Como admin (o CI) as pastas da app têm de ser só de admin, como no PC a sério
    static string Folder(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { Adb.CreateAdminOnly(path); } catch { Directory.CreateDirectory(path); }
        return path;
    }

    static bool Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        p.WaitForExit(10000);
        return Directory.Exists(link);
    }

    static void Remove(CoreTests.CheckFn check, string tmp)
    {
        var outside = Path.Combine(tmp, "fora");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "fica.txt"), "x");

        // A pasta do ProgramData: sai sem seguir a junção; o que está em uso fica na lista e o resto sai na mesma (também ao lado dele)
        {
            var root = Folder(Path.Combine(tmp, "CS2IPChanger"));
            Directory.CreateDirectory(Path.Combine(root, "adb"));
            File.WriteAllText(Path.Combine(root, "adb", "adb.exe"), "x");
            var madeJunction = Junction(Path.Combine(root, "ligacao"), outside);
            var busy = Path.Combine(root, "net", "x", "wpfgfx.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(busy)!);
            File.WriteAllText(Path.Combine(root, "net", "x", "zz-depois.dll"), "x");   // listed after the busy one
            var left = new Uninstall.Leftovers();
            bool ok;
            using (new FileStream(busy, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                ok = Uninstall.RemoveTree(root, left);
            check("Remover: junção removida, o alvo fica", madeJunction && !Directory.Exists(Path.Combine(root, "ligacao")) && File.Exists(Path.Combine(outside, "fica.txt")));
            check("Remover: o que está em uso fica na lista, o resto sai (também o ficheiro ao lado)",
                ok && left.Files.SequenceEqual(new[] { busy }) && !File.Exists(Path.Combine(root, "adb", "adb.exe")) && !File.Exists(Path.Combine(root, "net", "x", "zz-depois.dll")),
                string.Join(" | ", left.Files));
            check("Remover: pastas por esvaziar da mais funda para a de cima",
                left.Dirs.SequenceEqual(new[] { Path.GetDirectoryName(busy), Path.Combine(root, "net"), root }), string.Join(" | ", left.Dirs));
            check("Remover: pasta que não existe está bem", Uninstall.RemoveTree(Path.Combine(tmp, "nada"), new Uninstall.Leftovers()));
            // A ordem para o reinício (último recurso): ficheiros, depois pastas da mais funda
            var order = new List<string>();
            Uninstall.AtRestart(left, p => { order.Add(p); return true; });
            check("Remover: no reinício, ficheiros antes das pastas", order.SequenceEqual(left.Files.Concat(left.Dirs)));
            // A pasta é uma junção: nada sai, nem lá dentro
            var j = Path.Combine(tmp, "pd-junction");
            if (Junction(j, outside))
                check("Remover: pasta que é uma junção fica (e o alvo também)", !Uninstall.RemoveTree(j, new Uninstall.Leftovers()) && File.Exists(Path.Combine(outside, "fica.txt")) && Directory.Exists(j));
        }

        // Manter definições: a app sai, SRT_Dados fica, a pasta de instalação fica (tem o SRT_Dados)
        {
            var root = Folder(Path.Combine(tmp, "pd-keep"));
            File.WriteAllText(Path.Combine(root, "task.xml"), "x");
            var install = Folder(Path.Combine(tmp, "Program Files", "CS2 IP Changer"));
            var exe = Path.Combine(install, "CS2IPChanger.exe");
            File.WriteAllText(exe, "x");
            var data = Path.Combine(install, "SRT_Dados");
            Folder(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
            var r = Uninstall.RemoveFiles(false, exe, data, root, install);
            check("Manter: a app e o ProgramData saem, SRT_Dados e a pasta ficam, nada para depois",
                !File.Exists(exe) && !Directory.Exists(root) && File.Exists(Path.Combine(data, "settings.json")) && r.Left.Count == 0 && !r.ProgramDataRefused && !r.AppRefused && !r.DataRefused,
                $"exe={File.Exists(exe)} root={Directory.Exists(root)} data={File.Exists(Path.Combine(data, "settings.json"))} pd={r.ProgramDataRefused} app={r.AppRefused} d={r.DataRefused}");
            // Apagar tudo: SRT_Dados e a pasta de instalação também
            File.WriteAllText(exe, "x");
            var r2 = Uninstall.RemoveFiles(true, exe, data, root, install);
            check("Apagar tudo: SRT_Dados e a pasta de instalação saem", !Directory.Exists(data) && !Directory.Exists(install) && r2.Left.Count == 0);
        }

        // Cópia fora de Program Files: só o exe e o SRT_Dados, nunca a pasta onde estavam (não é da app)
        {
            var folder = Folder(Path.Combine(tmp, "Tools"));
            File.WriteAllText(Path.Combine(folder, "outro.txt"), "x");
            var exe = Path.Combine(folder, "CS2IPChanger.exe");
            File.WriteAllText(exe, "x");
            var data = Path.Combine(folder, "SRT_Dados");
            Folder(data);
            var r = Uninstall.RemoveFiles(true, exe, data, Path.Combine(tmp, "pd-none"), Path.Combine(tmp, "Program Files", "CS2 IP Changer"));
            check("Cópia noutra pasta: sai o exe e o SRT_Dados, a pasta e o resto ficam",
                !File.Exists(exe) && !Directory.Exists(data) && File.Exists(Path.Combine(folder, "outro.txt")) && r.Left.Count == 0, $"exe={File.Exists(exe)} data={Directory.Exists(data)} app={r.AppRefused} d={r.DataRefused} pd={r.ProgramDataRefused}");
            // SRT_Dados que é uma junção (para fora): fica, e o alvo também
            var data2 = Path.Combine(folder, "SRT_Dados");
            if (Junction(data2, outside))
            {
                var r2 = Uninstall.RemoveFiles(true, null, data2, Path.Combine(tmp, "pd-none"));
                check("Apagar tudo: SRT_Dados que é uma junção fica, o alvo também", r2.DataRefused && File.Exists(Path.Combine(outside, "fica.txt")));
            }
        }

        // Como admin, uma pasta que outros programas podem mudar fica (não é da app apagar)
        if (Admin)
        {
            var open = Path.Combine(tmp, "aberta");
            Directory.CreateDirectory(open);
            var sec = new DirectoryInfo(open).GetAccessControl();
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(open).SetAccessControl(sec);
            File.WriteAllText(Path.Combine(open, "x.txt"), "x");
            check("Remover: como admin, pasta que outros mudam fica", !Uninstall.RemoveTree(open, new Uninstall.Leftovers()) && !Uninstall.RemoveTree(open, new Uninstall.Leftovers(), programData: false) && File.Exists(Path.Combine(open, "x.txt")));
        }

        // O passo depois de a app fechar: só apaga os caminhos dados, ficheiros um a um e pastas vazias, nunca Remove-Item nem -Recurse
        {
            var l = new Uninstall.Leftovers();
            l.Files.Add(@"C:\Program Files\CS2 IP Changer\CS2IPChanger.exe");
            l.Files.Add(@"C:\Pasta d'Alguém\x.dll");
            l.Dirs.Add(@"C:\Program Files\CS2 IP Changer");
            var s = Uninstall.Script(4242, l);
            check("Depois de fechar: espera pela app, apaga um a um, pastas só vazias",
                s.Contains("Wait-Process -Id 4242") && s.Contains("[IO.File]::Delete($p)") && s.Contains("[IO.Directory]::Delete($p, $false)")
                && !s.Contains("Remove-Item") && !s.Contains("-Recurse") && !s.Contains("\"") && s.Contains("PendingFileRenameOperations"));
            check("Depois de fechar: a plica do caminho vai dobrada", s.Contains(@"'C:\Pasta d''Alguém\x.dll'"));
            static Uninstall.Leftovers One(string p) { var x = new Uninstall.Leftovers(); x.Files.Add(p); return x; }
            check("Depois de fechar: caminho relativo, de rede ou com uma linha nova fica para o reinício",
                Uninstall.Script(1, One(@"x\y.dll")) == null && Uninstall.Script(1, One(@"\\server\share\x")) == null && Uninstall.Script(1, One("C:\\a\nb")) == null);
            check("Depois de fechar: caminho com aspas curvas (o PowerShell fecha a string nelas) fica para o reinício",
                Uninstall.Script(1, One("C:\\a’; calc; ’b\\x.dll")) == null && Uninstall.Script(1, One("C:\\a‘b\\x.dll")) == null);
            check("Depois de fechar: nada em uso, nada a arrancar",Uninstall.AfterExit(new Uninstall.Leftovers()));
        }

        // O passo a sério: um processo segura um ficheiro; o script espera que ele acabe e apaga o ficheiro e a pasta vazia
        {
            var dir = Path.Combine(tmp, "em-uso");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "preso.dll");
            File.WriteAllText(file, "x");
            var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var holder = new ProcessStartInfo(ps) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", $"$h=[IO.File]::Open('{file}','Open','Read','None'); Start-Sleep -Seconds 3; $h.Close()" }) holder.ArgumentList.Add(a);
            using var hold = Process.Start(holder);
            Thread.Sleep(1500);   // the file is held now
            var l = new Uninstall.Leftovers();
            l.Files.Add(file);
            l.Dirs.Add(dir);
            var stillThere = File.Exists(file);
            var run = new ProcessStartInfo(ps) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", Uninstall.Script(hold.Id, l) }) run.ArgumentList.Add(a);
            using var cleaner = Process.Start(run);
            var done = cleaner.WaitForExit(40000);
            check("Depois de fechar: espera pelo processo e apaga o ficheiro e a pasta", stillThere && done && hold.HasExited && !File.Exists(file) && !Directory.Exists(dir));
        }
    }

    // Entrada em Definições > Aplicações instaladas (escrita numa chave de teste do utilizador, não no HKLM)
    static void Registry(CoreTests.CheckFn check)
    {
        var target = @"C:\Program Files\CS2 IP Changer";
        var exe = Path.Combine(Path.GetTempPath(), "srt-p5-exe-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(exe, new byte[5000]);
        var key = @"Software\SRT-Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var entry = Installer.UninstallEntry(target + @"\CS2IPChanger.exe", "0.1.0-beta.8", 70_000_000, new DateTime(2026, 11, 1)).ToDictionary(e => e.Name, e => e.Value);
            check("Aplicações instaladas: nome, versão, editor, ícone, pasta, tamanho em KB, data",
                (string)entry["DisplayName"] == "CS2 IP Changer" && (string)entry["DisplayVersion"] == "0.1.0-beta.8" && (string)entry["Publisher"] == "Sepd0x"
                && (string)entry["DisplayIcon"] == target + @"\CS2IPChanger.exe,0" && (string)entry["InstallLocation"] == target && (int)entry["EstimatedSize"] == 68360
                && (string)entry["InstallDate"] == "20261101" && (int)entry["NoModify"] == 1 && (int)entry["NoRepair"] == 1);
            check("Aplicações instaladas: Uninstall abre a pergunta da app (exe entre aspas e --uninstall)",
                (string)entry["UninstallString"] == "\"" + target + "\\CS2IPChanger.exe\" --uninstall");

            var dir = Path.GetDirectoryName(exe)!;
            check("Aplicações instaladas: outra cópia não escreve nada",
                Installer.KeepRegistered(exe, "0.1.0", Microsoft.Win32.Registry.CurrentUser, key, target) == null && Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key) == null);
            var w1 = Installer.KeepRegistered(exe, "0.1.0-beta.8", Microsoft.Win32.Registry.CurrentUser, key, dir);
            string date1, v1;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) { date1 = k?.GetValue("InstallDate") as string; v1 = k?.GetValue("DisplayVersion") as string; }
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, true)) k.SetValue("InstallDate", "20250101");
            var w2 = Installer.KeepRegistered(exe, "0.1.0-beta.9", Microsoft.Win32.Registry.CurrentUser, key, dir);
            string date2, v2;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) { date2 = k?.GetValue("InstallDate") as string; v2 = k?.GetValue("DisplayVersion") as string; }
            check("Aplicações instaladas: a cópia instalada escreve; a atualização muda a versão e mantém a data",
                w1 == null && w2 == null && v1 == "0.1.0-beta.8" && date1 == DateTime.Today.ToString("yyyyMMdd") && v2 == "0.1.0-beta.9" && date2 == "20250101", $"{v1} {date1} {v2} {date2}");
            check("Aplicações instaladas: Remove apaga a entrada (e sem entrada também está bem)",
                Installer.Unregister(Microsoft.Win32.Registry.CurrentUser, key) == null && Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key) == null
                && Installer.Unregister(Microsoft.Win32.Registry.CurrentUser, key) == null);
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); } catch { }
            try { File.Delete(exe); } catch { }
        }
    }

    static void Texts(CoreTests.CheckFn check)
    {
        var data = @"C:\Program Files\CS2 IP Changer\SRT_Dados";
        var c = Uninstall.Choices(data, true);
        check("Pergunta: duas escolhas, manter primeiro; diz o que sai em cada uma",
            c.Length == 2 && c[0].StartsWith("Keep my settings and accounts\n") && c[0].Contains(data) && c[0].Contains("installing the app again")
            && c[1].StartsWith("Delete everything\n") && c[1].Contains("SRT_Dados") && c[1].Contains("accounts") && !Uninstall.Choices(data, false)[0].Contains("installing"));
        check("Pergunta: o que sai sempre, sem passos à mão", Uninstall.Text().Contains("Start with Windows task") && Uninstall.Text().Contains(Uninstall.Root)
            && Uninstall.Text().Contains("the app itself") && !Uninstall.Text().Contains("Delete the folder"));
        var left = new Uninstall.Leftovers();
        left.Files.Add(@"C:\x.exe");
        var keep = Uninstall.Result(new Uninstall.Outcome(null, null, null, new Uninstall.Files(false, false, false, left), true, false), data);
        check("Resultado: removida, o resto sai depois de fechar, o reinício só se o Windows segurar um ficheiro, diz o que ficou",
            keep.Title == "CS2 IP Changer was removed" && !keep.Problem && keep.Text.Contains("a few seconds after the app closes") && keep.Text.Contains("next restart")
            && keep.Text.Contains("Kept: " + data));
        var all = Uninstall.Result(new Uninstall.Outcome(null, null, null, new Uninstall.Files(false, false, false, new Uninstall.Leftovers()), true, true), data);
        check("Resultado: apagar tudo, sem nada para depois, não fala de reinício nem do que ficou", !all.Problem && !all.Text.Contains("restart") && !all.Text.Contains("Kept"));
        var bad = Uninstall.Result(new Uninstall.Outcome("rule X", "task Y", "access denied", new Uninstall.Files(true, false, false, left), false, true), data);
        check("Resultado: o que falhou diz-se, e sem o passo depois de fechar fica para o reinício",
            bad.Problem && bad.Title == "Not everything was removed" && bad.Text.Contains("rule X") && bad.Text.Contains("task Y") && bad.Text.Contains("Installed apps")
            && bad.Text.Contains("left alone") && bad.Text.Contains("Windows deletes them at the next restart"));
        var line = Uninstall.LogLine(new Uninstall.Outcome(null, null, null, new Uninstall.Files(false, false, false, left), true, false));
        check("Registo: diz se manteve o SRT_Dados e quantos saem depois", line.StartsWith("Remove (kept SRT_Dados): rules and routes gone; task gone") && line.Contains("in use: 1 path, deleted after the app closes"), line);
    }
}
