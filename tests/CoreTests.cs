using System.IO;
using SRT;
using SRT.Core;

// Correções da auditoria do núcleo (sem admin, Steam nem rede): cada bloco diz o que protege.
static class CoreTests
{
    public delegate void CheckFn(string name, bool ok, string detail = "");

    // Junção de teste numa pasta temporária (não precisa de admin)
    static bool Junction(string link, string target)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        p.WaitForExit(10000);
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    public static void Run(CheckFn check)
    {
        // Troca de conta: o Steam aberto pelo Explorer só entra na conta nova com -login (testado a 29 out.)
        check("Troca: -login com a conta", Steam.LoginArgs("vanityidacc2", "") == "-login vanityidacc2", Steam.LoginArgs("vanityidacc2", ""));
        check("Troca: -login junta os outros argumentos", Steam.LoginArgs("acc_2", "-silent") == "-login acc_2 -silent", Steam.LoginArgs("acc_2", "-silent"));
        check("Troca: login estranho não entra na linha de comandos", Steam.LoginArgs("a b\" -x", "-silent") == "-silent", Steam.LoginArgs("a b\" -x", "-silent"));
        // F03: as DLLs nativas do WPF só contam vindas de %ProgramData%\CS2IPChanger\net só de admins
        {
            var safe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger", "net");
            var app = AppContext.BaseDirectory;
            var temp = Path.Combine(Path.GetTempPath(), ".net", "CS2IPChanger", "abc123") + "\\";
            check("Arranque: DLLs do WPF no %TEMP% recusadas", !SRT.Program.NativeDirSafe(safe, temp + ";" + app + ";" + temp.TrimEnd('\\') + ";", app));
            check("Arranque: só a pasta da app (build normal) passa", SRT.Program.NativeDirSafe(safe, app + ";", app));
            // Uma "base" que o utilizador criou: dentro dela, mas não só de admins
            var mine = Path.Combine(Path.GetTempPath(), "srt-core-net-" + Guid.NewGuid().ToString("N"));
            var inside = Path.Combine(mine, "CS2IPChanger", "x");
            Directory.CreateDirectory(inside);
            check("Arranque: pasta certa mas que o utilizador muda recusada", !SRT.Program.NativeDirSafe(mine, inside + "\\;" + app, app));
            check("Arranque: caminho parecido fora da base recusado", !SRT.Program.NativeDirSafe(safe, safe + "-evil\\CS2IPChanger\\x\\;" + app, app));
            try { Directory.Delete(mine, true); } catch { }
        }

        // F04: o Restore network carrega o NetTCPIP do System32 pelo caminho, nunca pelo PSModulePath (Documentos primeiro)
        {
            var a = Guard.PcOffPhoneArgs(18, false);
            var module = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules", "NetTCPIP", "NetTCPIP.psd1");
            check("Restore: NetTCPIP do System32 pelo caminho", a.Contains($"Import-Module '{module}';") && File.Exists(module), a);
            check("Restore: cmdlet chamado pelo nome do módulo", a.Contains(@"NetTCPIP\Set-NetIPInterface -InterfaceIndex 18 ") && !a.Contains(" Set-NetIPInterface"), a);
            var r = Guard.ResetPhonesArgs(new[] { 13, 21 });
            check("--restore: Get e Set do NetTCPIP pelo nome do módulo", r.Contains($"Import-Module '{module}';") && r.Contains(@"NetTCPIP\Get-NetIPInterface -InterfaceIndex 13,21 ")
                && r.Contains(@"| NetTCPIP\Set-NetIPInterface ") && !r.Contains(" Set-NetIPInterface") && !r.Contains(" Get-NetIPInterface"), r);
        }

        // F07: nas listas públicas (Regions, países) as palavras pessoais não se tapam, os IPs sim; a marca passa para as linhas
        {
            check("Gravação: lista pública sem palavras", Rec.Scrub("Germany · 203.0.113.37", null) == "Germany · •••", Rec.Scrub("Germany · 203.0.113.37", null));
            bool inherited = false, plain = true;
            var t = new Thread(() =>
            {
                var list = new System.Windows.Controls.StackPanel();
                var row = new System.Windows.Controls.TextBlock { Text = "Germany" };
                list.Children.Add(row);
                plain = Rec.GetPublic(row);
                Rec.SetPublic(list, true);
                inherited = Rec.GetPublic(row);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start(); t.Join();
            check("Gravação: marca pública herdada pelas linhas", inherited && !plain);
        }

        // F46: o id32 da pasta userdata (e das cópias das configs) é a SteamID64 menos uma constante: tapa-se no ecrã e no Export diagnostics
        {
            var msg = @"Access to the path 'C:\Program Files (x86)\Steam\userdata\87654321\730\local\cfg\x.vcfg.srt-tmp' is denied.";
            check("Gravação: id32 no caminho do userdata", !Rec.Scrub(msg).Contains("87654321") && Rec.Scrub(msg).Contains(@"\userdata\•••\730\"), Rec.Scrub(msg));
            check("Diagnostics: id32 no caminho tapado", !Rec.ScrubIds(msg, null).Contains("87654321"), Rec.ScrubIds(msg, null));
            var bak = @"C:\CS2IPChanger\SRT_Dados\configs\backups\87654321\20261029-120000-000\backup.json";
            check("Gravação: id32 nas cópias das configs", !Rec.Scrub(bak).Contains("87654321") && Rec.Scrub(bak).Contains("20261029-120000-000"), Rec.Scrub(bak));
            check("Gravação: o 730 e outros números ficam", Rec.Scrub(@"userdata 730 \cfg\12345") == @"userdata 730 \cfg\12345", Rec.Scrub(@"userdata 730 \cfg\12345"));
        }

        // F18: a pasta da app tem de ser só de admins (aviso), e as DLLs das P/Invokes vêm do System32
        {
            var mine = Path.Combine(Path.GetTempPath(), "srt-core-dir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mine);
            check("Pasta da app: pasta do utilizador avisa", Adb.UsersCanWrite(mine));
            try { Directory.Delete(mine); } catch { }
            check("Pasta da app: Program Files não avisa", !Adb.UsersCanWrite(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
            check("Pasta da app: System32 não avisa", !Adb.UsersCanWrite(Environment.SystemDirectory));
            var h = SRT.Program.SystemDll("iphlpapi.dll");
            check("DLLs: iphlpapi do System32", h != IntPtr.Zero);
            check("DLLs: nome com caminho recusado", SRT.Program.SystemDll(@"..\iphlpapi.dll") == IntPtr.Zero && SRT.Program.SystemDll(@"C:\x\iphlpapi.dll") == IntPtr.Zero);
        }

        // F50: a ligação confirmada perde a confiança quando desaparece ou muda (volta a ser medida)
        {
            check("Confiança: mesma ligação continua", MainWindow.TrustAfter("7|192.168.42.10", "7|192.168.42.10") == "7|192.168.42.10");
            check("Confiança: ligação desaparece e perde-a", MainWindow.TrustAfter("7|192.168.42.10", null) == null);
            check("Confiança: outra ligação não a herda", MainWindow.TrustAfter("7|192.168.42.10", "9|192.168.42.10") == null);
        }

        // Sem admin, a escrita das regras falha a cada sync (5 s): uma linha só, e nunca "blocked" (ver o Guard)
        {
            string last = null;
            var denied = new List<string> { "Access is denied.", "Access is denied.", "Access is denied." };
            var l1 = Guard.FirewallErrorLine(denied, ref last);
            check("Firewall: 3 erros iguais numa linha", l1 == "Firewall: Access is denied (3 rules). The rules were not written.", l1);
            check("Firewall: o mesmo erro outra vez fica calado", Guard.FirewallErrorLine(denied, ref last) == null && Guard.FirewallErrorLine(new List<string>(denied), ref last) == null, "");
            var l2 = Guard.FirewallErrorLine(new List<string> { "Access is denied." }, ref last);
            check("Firewall: erro diferente volta a aparecer", l2 == "Firewall: Access is denied. The rules were not written.", l2);
            check("Firewall: escrita que volta a funcionar diz-se uma vez", Guard.FirewallErrorLine(new List<string>(), ref last) == "Firewall: rules written again." && Guard.FirewallErrorLine(new List<string>(), ref last) == null, "");
            string fresh = null;
            check("Firewall: sem erros desde o início, nada", Guard.FirewallErrorLine(new List<string>(), ref fresh) == null, "");
        }

        // Singular e plural certos ("1 account", nunca "1 account(s)")
        {
            check("Contagem: singular", Fmt.Count(1, "account") == "1 account", Fmt.Count(1, "account"));
            check("Contagem: plural e zero", Fmt.Count(3, "account") == "3 accounts" && Fmt.Count(0, "server") == "0 servers", Fmt.Count(3, "account"));
            check("Contagem: plural irregular", Fmt.Count(2, "entry", "entries") == "2 entries", Fmt.Count(2, "entry", "entries"));
        }

        // F80: horas sem unidade lidas igual em qualquer língua do Windows
        {
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
                check("1.5 (horas, pt-BR)", Fmt.ParseDuration("1.5") == TimeSpan.FromHours(1.5), Fmt.ParseDuration("1.5")?.ToString());
                check("1,5 (horas, pt-BR)", Fmt.ParseDuration("1,5") == TimeSpan.FromHours(1.5), Fmt.ParseDuration("1,5")?.ToString());
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-PT");
                check("1.5 (horas, pt-PT)", Fmt.ParseDuration("1.5") == TimeSpan.FromHours(1.5));
                bool threw = false; TimeSpan? inf = TimeSpan.Zero;
                try { inf = Fmt.ParseDuration("Infinity"); } catch { threw = true; }
                check("Horas infinitas recusadas sem erro", !threw && inf == null);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = before; }
        }

        // F45: a janela nunca abre maior do que a área de trabalho (1366x768 a 125%: cerca de 1093x590)
        {
            var (minW, minH, w, h) = MainWindow.FitWorkArea(1020, 660, 1240, 800, 1093, 590);
            check("Janela: cabe num ecrã pequeno", minW == 1020 && minH == 590 && w == 1093 && h == 590, $"{minW} {minH} {w} {h}");
            var big = MainWindow.FitWorkArea(1020, 660, 1240, 800, 2560, 1400);
            check("Janela: ecrã grande sem mudança", big == (1020, 660, 1240, 800));
        }

        // F17: o loginusers.vdf escreve-se pela pasta presa e pelo próprio handle (pastas temporárias, nunca o Steam deste PC)
        {
            var root = Path.Combine(Path.GetTempPath(), "srt-core-steam-" + Guid.NewGuid().ToString("N"));
            var config = Path.Combine(root, "Steam", "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "loginusers.vdf"), "\"users\"\n{\n}\n");
            try
            {
                using (var folder = new Steam.PinnedFolder(config))
                {
                    using (var f = folder.Open("loginusers.vdf", FileMode.Open, FileAccess.ReadWrite))
                        Steam.WriteAll(f, System.Text.Encoding.UTF8.GetBytes("novo"));
                    check("Contas: escrita pela pasta presa", File.ReadAllText(Path.Combine(config, "loginusers.vdf")) == "novo");
                    var moved = false;
                    try { Directory.Move(config, config + "-x"); moved = true; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    check("Contas: a pasta presa não pode ser trocada", !moved);
                    for (var i = 0; i < 12; i++)
                        using (var b = folder.Open($"loginusers.vdf.srt-backup-20261029-1200{i:00}", FileMode.Create, FileAccess.Write)) b.WriteByte(1);
                    Steam.TidyBackups(folder, "loginusers.vdf");
                    var left = Directory.GetFiles(config, "loginusers.vdf.srt-backup-*");
                    check("Contas: só as 10 cópias mais novas ficam", left.Length == 10 && !left.Any(x => x.EndsWith("120000") || x.EndsWith("120001")), left.Length.ToString());
                    var refused = false;
                    try { folder.Open(@"..\fora.txt", FileMode.Create, FileAccess.Write).Dispose(); } catch (ArgumentException) { refused = true; }
                    check("Contas: nada fora da pasta presa", refused && !File.Exists(Path.Combine(root, "Steam", "fora.txt")));
                }
                // Uma junção na pasta config é recusada; uma pasta Steam inteira movida com junção continua a funcionar
                var real = Path.Combine(root, "real");
                Directory.CreateDirectory(real);
                var link = Path.Combine(root, "link");
                var ok = Junction(link, Path.Combine(root, "Steam"));
                var linkConfig = Path.Combine(root, "Steam", "config-link");
                ok &= Junction(linkConfig, real);
                if (ok)
                {
                    var linked = false;
                    try { new Steam.PinnedFolder(linkConfig).Dispose(); } catch (IOException) { linked = true; }
                    check("Contas: pasta config que é junção recusada", linked);
                    using var viaJunction = new Steam.PinnedFolder(Path.Combine(link, "config"));
                    check("Contas: pasta Steam movida com junção aceite", viaJunction.Real.EndsWith(@"\Steam\config", StringComparison.OrdinalIgnoreCase), viaJunction.Real);
                }
                else check("Contas: junções de teste criadas", false);
                try { Directory.Delete(linkConfig); } catch { }
                try { Directory.Delete(link); } catch { }
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        // E: troca de conta com o cliente do Steam de hoje (AutoLogin + timestamp em minúsculas, sem MostRecent nem AllowAutoLogin)
        {
            const string A1 = "76561198000000001", A2 = "76561198000000002";
            var now = "\"users\"\n{\n" +
                $"\t\"{A1}\"\n\t{{\n\t\t\"AccountName\"\t\t\"acc1\"\n\t\t\"PersonaName\"\t\t\"Acc1\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n\t\t\"AutoLogin\"\t\t\"1\"\n\t\t\"timestamp\"\t\t\"1793307462\"\n\t}}\n" +
                $"\t\"{A2}\"\n\t{{\n\t\t\"AccountName\"\t\t\"acc2\"\n\t\t\"PersonaName\"\t\t\"Acc2\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n\t\t\"AutoLogin\"\t\t\"0\"\n\t\t\"timestamp\"\t\t\"1793300000\"\n\t}}\n}}\n";
            var before = Steam.ParseAccounts(now);
            check("Contas: formato novo lido (AutoLogin e timestamp em minúsculas)", before.First(a => a.SteamId == A1).MostRecent && !before.First(a => a.SteamId == A2).MostRecent
                && before.First(a => a.SteamId == A1).Timestamp == 1793307462, string.Join(" ", before.Select(a => $"{a.AccountName}:{a.MostRecent}:{a.Timestamp}")));
            var doc = VdfNode.Parse(now);
            Steam.MarkForLogin(doc.Child("users"), A2, false, 1793308000);
            var text = doc.Write();
            var after = Steam.ParseAccounts(text);
            check("Contas: troca marca AutoLogin 1 só no destino e o timestamp novo", after.First(a => a.SteamId == A2).MostRecent && !after.First(a => a.SteamId == A1).MostRecent
                && after.First(a => a.SteamId == A2).Timestamp == 1793308000 && VdfNode.RoundTrips(text), text.Replace("\n", " "));
            check("Contas: formato novo sem MostRecent nem AllowAutoLogin acrescentados, chave timestamp em minúsculas",
                !text.Contains("MostRecent") && !text.Contains("AllowAutoLogin") && text.Contains("\"timestamp\"") && !text.Contains("\"Timestamp\""));

            var old = "\"users\"\n{\n" +
                $"\t\"{A1}\"\n\t{{\n\t\t\"AccountName\"\t\t\"acc1\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"AllowAutoLogin\"\t\t\"1\"\n\t\t\"Timestamp\"\t\t\"1700000000\"\n\t}}\n" +
                $"\t\"{A2}\"\n\t{{\n\t\t\"AccountName\"\t\t\"acc2\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t\t\"Timestamp\"\t\t\"1690000000\"\n\t}}\n}}\n";
            var od = VdfNode.Parse(old);
            Steam.MarkForLogin(od.Child("users"), A2, true, 1700000500);
            var ot = od.Write();
            var oa = Steam.ParseAccounts(ot);
            var a2 = od.Child("users").Child(A2);
            check("Contas: formato antigo continua (MostRecent e AllowAutoLogin, Timestamp com maiúscula)", oa.First(a => a.SteamId == A2).MostRecent && od.Child("users").Child(A1).Get("MostRecent") == "0"
                && a2.Get("AllowAutoLogin") == "1" && a2.Get("WantsOfflineMode") == "1" && ot.Contains("\"Timestamp\"\t\t\"1700000500\""), ot.Replace("\n", " "));

            // Ronda G: segunda tentativa, fecho limpo e a conta em uso quando o ActiveUser fica a 0
            check("Contas: conta errada tenta mais uma vez, só uma", Steam.RetrySwitch(Steam.LoginState.Other, 1) && !Steam.RetrySwitch(Steam.LoginState.Other, 2)
                && !Steam.RetrySwitch(Steam.LoginState.Target, 1) && !Steam.RetrySwitch(Steam.LoginState.Waiting, 1));
            check("Contas: o Steam só está fechado sem steam.exe nem steamwebhelper (o serviço fica)", Steam.SteamClosed(new[] { "steamservice" }) && !Steam.SteamClosed(new[] { "steamwebhelper" }) && !Steam.SteamClosed(new[] { "steam", "steamservice" }) && Steam.SteamClosed(null));
            var inUse = new[] { new LocalAccount(A1, "acc1", "Acc1", false, 10), new LocalAccount(A2, "acc2", "Acc2", true, 20) };
            check("Contas: ActiveUser a 0 com o Steam aberto: a conta com AutoLogin", Steam.InUse(null, true, inUse) == A2 && Steam.InUse(A1, true, inUse) == A1 && Steam.InUse(null, false, inUse) == null);
            check("Contas: cartões ficam no lugar enquanto a página está aberta", SRT.MainWindow.StableOrder(new[] { "a", "b", "c" }, new[] { "c", "a", "b", "d" }, keep: true).SequenceEqual(new[] { "a", "b", "c", "d" })
                && SRT.MainWindow.StableOrder(new[] { "a", "b", "c" }, new[] { "c", "a", "b" }, keep: false).SequenceEqual(new[] { "c", "a", "b" })
                && SRT.MainWindow.StableOrder(new[] { "a", "x" }, new[] { "b", "a" }, keep: true).SequenceEqual(new[] { "a", "b" }));

            // Quem o Steam pôs a dentro depois de reiniciar
            LocalAccount L(string id, long ts) => new(id, id, id, false, ts);
            var stamp = 1793308000L;
            check("Contas: ActiveUser no destino = feito", Steam.LoginDecision(A2, A2, new[] { L(A1, 1793307462) }, stamp, false).State == Steam.LoginState.Target);
            var wrong = Steam.LoginDecision(A2, null, new[] { L(A1, 1793308006), L(A2, stamp) }, stamp, false);
            check("Contas: o Steam reescreveu o ficheiro com outra conta = conta errada", wrong.State == Steam.LoginState.Other && wrong.Other == A1);
            check("Contas: o ficheiro com o destino mais novo = feito", Steam.LoginDecision(A2, null, new[] { L(A1, 1793307462), L(A2, 1793308004) }, stamp, false).State == Steam.LoginState.Target);
            check("Contas: ActiveUser antigo ainda não conta (pode ser de antes do reinício)", Steam.LoginDecision(A2, A1, new[] { L(A2, stamp) }, stamp, activeTrusted: false).State == Steam.LoginState.Waiting);
            check("Contas: ActiveUser de outra conta que fica = conta errada", Steam.LoginDecision(A2, A1, new[] { L(A2, stamp) }, stamp, activeTrusted: true) is { State: Steam.LoginState.Other, Other: A1 });
            check("Contas: nada ainda = à espera (o próprio timestamp da app não conta)", Steam.LoginDecision(A2, null, new[] { L(A2, stamp), L(A1, 1793307462) }, stamp, true).State == Steam.LoginState.Waiting);
        }

        // D: as pastas desempacotadas de versões antigas em %ProgramData%\CS2IPChanger\net (aqui numa pasta temporária)
        {
            var root = Path.Combine(Path.GetTempPath(), "srt-core-net-old-" + Guid.NewGuid().ToString("N"));
            var bas = Path.Combine(root, "net");
            var outside = Path.Combine(root, "fora");
            try
            {
                foreach (var d in new[] { @"CS2IPChanger\aaa", @"CS2IPChanger\bbb", @"CS2IPChanger\ccc\sub", @"CS2IPChanger-Recording\ddd" })
                {
                    Directory.CreateDirectory(Path.Combine(bas, d));
                    File.WriteAllText(Path.Combine(bas, d, "x.dll"), "x");
                }
                Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "importante.txt"), "fica");
                var linked = Junction(Path.Combine(bas, "CS2IPChanger", "jjj"), outside) && Junction(Path.Combine(bas, "link-app"), outside);
                var current = Path.Combine(bas, "CS2IPChanger", "bbb") + "\\";
                var stale = SRT.Program.StaleUnpacked(bas, current).Select(p => Path.GetRelativePath(bas, p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                var want = new[] { @"CS2IPChanger\aaa", @"CS2IPChanger\ccc", @"CS2IPChanger-Recording\ddd" };
                check("Arranque: versões antigas escolhidas (nunca a atual nem junções)", stale.SequenceEqual(want.OrderBy(p => p, StringComparer.Ordinal)) && linked, string.Join(", ", stale));
                check("Arranque: a correr fora da base, nada se apaga", SRT.Program.StaleUnpacked(bas, Path.Combine(root, "outra")).Count == 0 && SRT.Program.StaleUnpacked(bas, null).Count == 0);
                check("Arranque: base que é ela própria uma junção, nada", !Junction(Path.Combine(root, "netlink"), bas) || SRT.Program.StaleUnpacked(Path.Combine(root, "netlink"), Path.Combine(root, "netlink", "CS2IPChanger", "bbb")).Count == 0);
                // Uma junção dentro de uma pasta antiga: sai a junção, o que está do outro lado fica
                Junction(Path.Combine(bas, "CS2IPChanger", "aaa", "dentro"), outside);
                var gone = SRT.Program.StaleUnpacked(bas, current).All(SRT.Program.DeleteTree);
                check("Arranque: pastas antigas apagadas, a atual e o que está fora ficam", gone && !Directory.Exists(Path.Combine(bas, "CS2IPChanger", "aaa")) && Directory.Exists(Path.Combine(bas, "CS2IPChanger", "bbb"))
                    && File.Exists(Path.Combine(outside, "importante.txt")));
            }
            finally
            {
                foreach (var j in new[] { Path.Combine(bas, "CS2IPChanger", "jjj"), Path.Combine(bas, "link-app"), Path.Combine(root, "netlink"), Path.Combine(bas, "CS2IPChanger", "aaa", "dentro") })
                    try { if (Directory.Exists(j)) Directory.Delete(j); } catch { }
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
