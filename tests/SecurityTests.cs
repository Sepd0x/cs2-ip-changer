using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using SRT;
using SRT.Core;

// Análise de segurança da beta.6 (31 out., G1 a G5): sem admin, Steam nem rede. Cada bloco diz o que protege.
static class SecurityTests
{
    static bool Mklink(string args, string check)
    {
        var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c mklink " + args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        p.WaitForExit(10000);
        return p.ExitCode == 0 && (File.Exists(check) || Directory.Exists(check));
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch { return true; }
    }

    // "--dll-search": corre num processo à parte (a pesquisa de DLLs muda para o processo todo)
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string name);
    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr h);

    public static int DllSearchChild(string name)
    {
        var before = LoadLibraryW(name);
        if (before != IntPtr.Zero) FreeLibrary(before);
        var locked = SRT.Program.LockDllSearch();
        var after = LoadLibraryW(name);
        var system = LoadLibraryW("version.dll");
        Console.WriteLine($"antes={(before != IntPtr.Zero ? 1 : 0)} trancado={(locked ? 1 : 0)} depois={(after != IntPtr.Zero ? 1 : 0)} system32={(system != IntPtr.Zero ? 1 : 0)}");
        return 0;
    }

    public static void Run(CoreTests.CheckFn check)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "srt-seg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            // G1: uma DLL posta ao lado do exe (ou na pasta atual) deixa de ser carregada pelo nome; as do System32 continuam
            {
                var name = "srtplantada" + Guid.NewGuid().ToString("N")[..6] + ".dll";
                var nextToExe = Path.Combine(AppContext.BaseDirectory, name);
                var inCwd = Path.Combine(tmp, name);
                var sys = Path.Combine(Environment.SystemDirectory, "version.dll");
                File.Copy(sys, nextToExe);
                File.Copy(sys, inCwd);
                try
                {
                    foreach (var (where, cwd) in new[] { ("ao lado do exe", AppContext.BaseDirectory), ("na pasta atual", tmp) })
                    {
                        if (where == "na pasta atual") File.Delete(nextToExe);
                        var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, WorkingDirectory = cwd, ArgumentList = { "--dll-search", name } });
                        var output = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(20000);
                        check($"DLLs: plantada {where} carregava antes", output.Contains("antes=1"), output);
                        check($"DLLs: plantada {where} recusada depois do LockDllSearch", output.Contains("trancado=1") && output.Contains("depois=0") && output.Contains("system32=1"), output);
                    }
                }
                finally { try { File.Delete(nextToExe); } catch { } }
            }

            // G1: como admin, a app não arranca de uma pasta que um programa sem admin pode mudar (Downloads, Ambiente de trabalho)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var why = SRT.Program.FolderProblem(tmp, Path.Combine(tmp, "SRT_Dados"));
                check("Pasta: do utilizador recusada, com o que fazer", why != null && why.Contains(tmp) && why.Contains(@"C:\Program Files\CS2 IP Changer"), why);
                check("Pasta: Program Files passa", SRT.Program.FolderProblem(programFiles, Path.Combine(programFiles, "nao-existe-SRT_Dados")) == null);
                var data = Path.Combine(tmp, "SRT_Dados");
                Directory.CreateDirectory(data);
                var whyData = SRT.Program.FolderProblem(programFiles, data);
                check("Pasta: SRT_Dados movido (do utilizador) recusado", whyData != null && whyData.Contains(data) && whyData.Contains("moved instead of copied"), whyData);
            }

            // G4: as variáveis do runtime que carregam código (profiler, porta de diagnóstico, outro GC) nunca chegam à cópia que corre
            {
                var names = new[] { "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH_64", "COR_PROFILER", "COMPlus_GCName", "DOTNET_DiagnosticPorts",
                    "dotnet_EnableDiagnostics", "DOTNET_BUNDLE_EXTRACT_BASE_DIR", "PATH", "CORECLRX", "TEMP" };
                var got = SRT.Program.RuntimeVars(names);
                var want = new[] { "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH_64", "COR_PROFILER", "COMPlus_GCName", "DOTNET_DiagnosticPorts", "dotnet_EnableDiagnostics" };
                check("Ambiente: profiler, diagnóstico e GC apanhados; a pasta de extração e o resto ficam", got.SequenceEqual(want), string.Join(",", got));
                var psi = new ProcessStartInfo("x.exe");
                psi.Environment["CORECLR_PROFILER"] = "{00000000-0000-0000-0000-000000000001}";
                psi.Environment["CORECLR_PROFILER_PATH"] = @"C:\Users\x\evil.dll";
                psi.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = @"C:\ProgramData\CS2IPChanger\net";
                foreach (var k in SRT.Program.RuntimeVars(psi.Environment.Keys)) psi.Environment.Remove(k);
                check("Ambiente: o relançamento vai sem o profiler", !psi.Environment.ContainsKey("CORECLR_PROFILER") && !psi.Environment.ContainsKey("CORECLR_PROFILER_PATH")
                    && psi.Environment.ContainsKey("DOTNET_BUNDLE_EXTRACT_BASE_DIR") && psi.Environment.ContainsKey("PATH"));
            }

            // G2: nada se escreve através de uma junção ou de um hard link (o processo é admin)
            {
                var target = Path.Combine(tmp, "alvo");
                Directory.CreateDirectory(target);
                var link = Path.Combine(Paths.Data, "srt-teste-juncao-" + Guid.NewGuid().ToString("N")[..6]);
                if (Mklink($"/J \"{link}\" \"{target}\"", link))
                {
                    try
                    {
                        check("Links: Store.Save por uma junção em SRT_Dados recusado", Throws(() => Store.Save(Path.Combine(link, "x.json"), new { a = 1 })) && !File.Exists(Path.Combine(target, "x.json")));
                        check("Links: NoLink apanha a junção a meio do caminho", Throws(() => Paths.NoLink(Path.Combine(link, "sub", "y.txt"))));
                    }
                    finally { Directory.Delete(link); }
                    check("Links: apagar a junção não apaga o alvo", Directory.Exists(target));
                }
                else check("Links: junção de teste criada", false);
                var real = Path.Combine(tmp, "real.txt");
                File.WriteAllText(real, "x");
                var hard = Path.Combine(tmp, "hard.txt");
                if (Mklink($"/H \"{hard}\" \"{real}\"", hard))
                    check("Links: hard link recusado (e o original também, tem 2 nomes)", Throws(() => Paths.NoLink(hard, tmp)) && Throws(() => Paths.NoLink(real, tmp)));
                else check("Links: hard link de teste criado", false);
                var plain = Path.Combine(tmp, "normal.txt");
                File.WriteAllText(plain, "x");
                check("Links: ficheiro normal e ficheiro que não existe passam", !Throws(() => Paths.NoLink(plain, tmp)) && !Throws(() => Paths.NoLink(Path.Combine(tmp, "novo.txt"), tmp)));
                check("Links: Store.Save normal continua", !Throws(() => Store.Save(Path.Combine(tmp, "ok.json"), new { a = 1 })) && File.Exists(Path.Combine(tmp, "ok.json")));
            }

            // G2: o zip do ADB não escreve através de uma junção em platform-tools nem para fora da pasta
            {
                var zip = Path.Combine(tmp, "pt.zip");
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("platform-tools/adb.exe").Open())) w.Write("x");
                var to = Path.Combine(tmp, "dados");
                Directory.CreateDirectory(to);
                Adb.Extract(zip, to);
                check("ADB: zip normal extraído", File.Exists(Path.Combine(to, "platform-tools", "adb.exe")));
                var to2 = Path.Combine(tmp, "dados2");
                var elsewhere = Path.Combine(tmp, "outro-sitio");
                Directory.CreateDirectory(to2);
                Directory.CreateDirectory(elsewhere);
                if (Mklink($"/J \"{Path.Combine(to2, "platform-tools")}\" \"{elsewhere}\"", Path.Combine(to2, "platform-tools")))
                    check("ADB: platform-tools como junção recusado", Throws(() => Adb.Extract(zip, to2)) && !File.Exists(Path.Combine(elsewhere, "adb.exe")));
                else check("ADB: junção de teste criada", false);
                var slip = Path.Combine(tmp, "slip.zip");
                using (var z = ZipFile.Open(slip, ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("../fora.txt").Open())) w.Write("x");
                check("ADB: entrada do zip para fora da pasta recusada", Throws(() => Adb.Extract(slip, to)) && !File.Exists(Path.Combine(tmp, "fora.txt")));
            }

            // G2: o owned.json só manda apagar rotas com a forma das da app
            {
                var st = new Guard.OwnedState
                {
                    Routes = new() { "203.0.113.37/32|17|192.168.42.129", "0.0.0.0/1|17|192.168.42.129", "155.133.224.0/19|17|192.168.42.129",
                        "0.0.0.0/0|5|192.168.1.1", "10.0.0.0/8|5|192.168.1.1", "203.0.113.37/32|0|192.168.42.129", "203.0.113.37/32|-3|192.168.42.129",
                        "203.0.113.37/32|17|fe80::1", "203.0.113.037/32|17|192.168.42.129", "203.0.113.37/32|17", "lixo", null },
                    MetricIf = -7,
                };
                var t = Guard.Trusted(st, out var dropped);
                check("owned.json: só /32, metades do PC e blocos da Valve", t.Routes.SequenceEqual(new[] { "203.0.113.37/32|17|192.168.42.129", "0.0.0.0/1|17|192.168.42.129", "155.133.224.0/19|17|192.168.42.129" }),
                    string.Join(" ; ", t.Routes));
                check("owned.json: entradas estranhas contadas e métrica inválida largada", dropped >= 8 && t.MetricIf == -1, $"{dropped} · {t.MetricIf}");
                check("owned.json: métrica num índice real fica", Guard.Trusted(new Guard.OwnedState { MetricIf = 17 }, out _).MetricIf == 17);
            }

            // G5: o ipwho.is nunca é perguntado pela ligação de casa sobre o IP do telemóvel
            {
                check("ipwho.is: sem ligação indicada, nada é perguntado", Net.Lookup("203.0.113.99", -1).GetAwaiter().GetResult() == null);
                check("ipwho.is: por uma placa que o Windows não usaria, nada é perguntado", Net.Lookup("203.0.113.98", 999_999).GetAwaiter().GetResult() == null);
                check("País do trace: PT é Portugal", Net.CountryOf("PT") == "Portugal", Net.CountryOf("PT"));
                check("País do trace: XX, T1, minúsculas e lixo não são país", Net.CountryOf("XX") == null && Net.CountryOf("T1") == null && Net.CountryOf("pt") == null && Net.CountryOf("PT; x") == null);
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
