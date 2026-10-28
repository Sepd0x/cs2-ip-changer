using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using SRT.Core;

// Testes com admin: só regras com nome de teste a bloquear IPs de documentação (RFC 5737, de ninguém),
// e uma rota para 203.0.113.77. Tudo é apagado no fim, mesmo que um teste falhe.
static class AdminTests
{
    const string Kill = "CS2IPChanger TESTE kill";
    const string Prog = "CS2IPChanger TESTE programa";
    const string Fake = "ServerRouteTool v9 - TESTE restore.cmd";
    static readonly string[] Ips = { "203.0.113.9", "203.0.113.10" };
    static int _fails;
    static StreamWriter _out;

    static void Check(string name, bool ok, string detail = "")
    {
        var line = $"{(ok ? "OK  " : "FALHA")} {name} {detail}";
        Console.WriteLine(line);
        _out.WriteLine(line);
        _out.Flush();
        if (!ok) _fails++;
    }

    static dynamic Rules() => ((dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true))).Rules;

    static int CountStartingWith(string prefix)
    {
        var n = 0;
        foreach (dynamic r in Rules()) if (((string)r.Name).StartsWith(prefix)) n++;
        return n;
    }

    public static async Task<int> Run(string outPath)
    {
        _out = new StreamWriter(outPath, false);
        try
        {
            var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Check("Corre como admin", admin);
            if (!admin) return 1;
            try { await Body(); await OpenWithNotElevated(); AdbFolder(); }
            catch (Exception ex) { Check("Exceção", false, ex.ToString()); }
            finally { await Cleanup(); }
            _out.WriteLine(_fails == 0 ? "TUDO OK" : $"{_fails} FALHA(S)");
            return _fails;
        }
        finally { _out.Dispose(); }
    }

    static async Task Body()
    {
        // 1) Aplicar e verificar
        Firewall.Apply(Kill, Ips, null, "192.0.2.5", "Teste do CS2 IP Changer (apagar à vontade)");
        Check("Regra criada e verificada", Firewall.Verify(Kill, Ips, "192.0.2.5"));
        Check("Só uma regra com o nome, sem restos temporários", CountStartingWith(Kill) == 1, $"{CountStartingWith(Kill)}");

        // 2) Trocar o IP permitido (o caso do telemóvel que volta com outro IP local)
        Firewall.Apply(Kill, Ips, null, "192.0.2.6", "Teste do CS2 IP Changer (apagar à vontade)");
        Check("Novo IP permitido verificado", Firewall.Verify(Kill, Ips, "192.0.2.6"));
        Check("IP antigo já não conta", !Firewall.Verify(Kill, Ips, "192.0.2.5"));
        Check("Continua uma só regra", CountStartingWith(Kill) == 1, $"{CountStartingWith(Kill)}");

        // 3) Nunca há um momento sem regra: conta as regras enquanto o Apply corre 30 vezes
        var min = int.MaxValue;
        var stop = false;
        var watcher = Task.Run(() => { while (!Volatile.Read(ref stop)) min = Math.Min(min, CountStartingWith(Kill)); });
        for (var i = 0; i < 30; i++) Firewall.Apply(Kill, Ips, null, i % 2 == 0 ? "192.0.2.7" : "192.0.2.8", "Teste do CS2 IP Changer (apagar à vontade)");
        Volatile.Write(ref stop, true);
        await watcher;
        Check("Sem buraco durante 30 trocas", min >= 1, $"mínimo de regras visto: {min}");
        Check("Depois das trocas, uma só regra", CountStartingWith(Kill) == 1, $"{CountStartingWith(Kill)}");

        // 4) Regra mexida à mão deixa de contar
        Firewall.Apply(Kill, Ips, null, "192.0.2.6", "Teste do CS2 IP Changer (apagar à vontade)");
        dynamic r = Rules().Item(Kill);
        r.Protocol = 6; // só TCP: o UDP do CS2 passaria
        Check("Regra só TCP não passa", !Firewall.Verify(Kill, Ips, "192.0.2.6"));
        Firewall.Apply(Kill, Ips, null, "192.0.2.6", "Teste do CS2 IP Changer (apagar à vontade)");
        r = Rules().Item(Kill);
        r.LocalAddresses = "10.9.9.9";
        Check("Regra com a origem mexida não passa", !Firewall.Verify(Kill, Ips, "192.0.2.6"));
        Firewall.Apply(Kill, Ips, null, "192.0.2.6", "Teste do CS2 IP Changer (apagar à vontade)");
        r = Rules().Item(Kill);
        r.Direction = 1; // entrada
        Check("Regra virada para entrada não passa", !Firewall.Verify(Kill, Ips, "192.0.2.6"));
        r.Direction = 2;
        r.Enabled = false;
        Check("Regra desligada não passa", !Firewall.Verify(Kill, Ips, "192.0.2.6"));

        // 5) Regra de programa (como a do cs2.exe)
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Firewall.Apply(Prog, null, notepad, "192.0.2.5", "Teste do CS2 IP Changer (apagar à vontade)");
        Check("Regra de programa verificada", Firewall.Verify(Prog, null, "192.0.2.5", notepad));
        Check("Outro programa não passa", !Firewall.Verify(Prog, null, "192.0.2.5", Path.Combine(Environment.SystemDirectory, "calc.exe")));

        // 6) A linha do restore.cmd apaga as regras com o nome da app
        Firewall.Apply(Fake, new[] { "203.0.113.99" }, null, null, "Teste do CS2 IP Changer (apagar à vontade)");
        Check("Regra falsa com o nome da app criada", Firewall.Remaining().Contains(Fake));
        var ps = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            "-NoProfile -Command \"$r = Get-NetFirewallRule -DisplayName 'ServerRouteTool v9*' -ErrorAction SilentlyContinue; if ($r) { $r | Remove-NetFirewallRule; Write-Host ('Removed ' + @($r).Count + ' rule(s).') } else { Write-Host 'No CS2 IP Changer rules found.' }\"")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
        var psOut = await ps.StandardOutput.ReadToEndAsync();
        ps.WaitForExit();
        Check("restore.cmd apaga a regra", !Firewall.Remaining().Contains(Fake), psOut.Trim());

        // 7) Rotas guardadas em disco e o Restore que confirma
        var wifi = Net.Scan().FirstOrDefault(a => a.Up && a.Gateway != null && !a.IsPhone);
        if (wifi == null) { Check("Placa com gateway para o teste da rota", false); return; }
        var add = await Net.AddRoute("203.0.113.77/32", wifi);
        Check("Rota de teste criada", add.Ok, add.Output?.Trim());
        Store.Save(Paths.File("owned.json"), new Guard.OwnedState
        {
            Routes = new() { $"203.0.113.77/32|{wifi.Index}|{wifi.Gateway}" },
            MetricIf = -1,
            Boot = DateTime.Now.AddMilliseconds(-Environment.TickCount64),
        });
        var left = await new Guard().RestoreAll(Array.Empty<string>());
        var still = (await Net.ListRoutes()).Any(x => x.Prefix == "203.0.113.77/32");
        Check("Restore apaga a rota de uma sessão anterior", !still);
        Check("Restore confirma que não ficou nada", left == null, left ?? "");
    }

    // alpha.4: "Open with CS2" abre o programa sem os direitos de admin da app (o Bloco de Notas, fechado no fim)
    static async Task OpenWithNotElevated()
    {
        var np = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var before = Process.GetProcessesByName("notepad").Select(p => p.Id).ToHashSet();
        string err = null;
        var t = new Thread(() => err = ShellLaunch.Open(np));
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Check("Shell abriu o Bloco de Notas", err == null, err ?? "");
        Process opened = null;
        for (var i = 0; i < 40 && opened == null; i++)
        {
            await Task.Delay(250);
            opened = Process.GetProcessesByName("notepad").FirstOrDefault(p => !before.Contains(p.Id));
        }
        Check("Bloco de Notas novo encontrado", opened != null);
        if (opened == null) return;
        var elevated = IsElevated(opened.Id);
        Check("Aberto sem admin (token não elevado)", elevated == false, elevated == null ? "não deu para ler o token" : "");
        try { if (!opened.CloseMainWindow() || !opened.WaitForExit(3000)) opened.Kill(); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    static bool? IsElevated(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(h, 0x0008, out var tok)) return null;   // TOKEN_QUERY
            try { return GetTokenInformation(tok, 20, out var e, 4, out _) ? e != 0 : null; }   // TokenElevation
            finally { CloseHandle(tok); }
        }
        finally { CloseHandle(h); }
    }

    // A pasta do adb só para admins: uma criada antes por outro (permissões do ProgramData) é posta de lado e refeita
    static void AdbFolder()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger-TESTE");
        try
        {
            Directory.CreateDirectory(root);
            Check("adb: pasta criada à maneira normal não é só de admin", !Adb.AdminOnly(root));
            Adb.SafeDir(root);
            Check("adb: refeita só para admins", Adb.AdminOnly(root));
            Check("adb: a antiga ficou de lado", Directory.GetDirectories(Path.GetDirectoryName(root)!, "CS2IPChanger-TESTE.untrusted-*").Length == 1);
            Adb.SafeDir(root);
            Check("adb: a boa fica como está", Directory.GetDirectories(Path.GetDirectoryName(root)!, "CS2IPChanger-TESTE.untrusted-*").Length == 1);
            var adb = Adb.Find();
            if (adb != null)
                Check("adb: corre da cópia segura", adb.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger", "adb"), StringComparison.OrdinalIgnoreCase)
                    && Adb.AdminOnly(Path.GetDirectoryName(adb)!), adb);
        }
        finally
        {
            foreach (var d in Directory.GetDirectories(Path.GetDirectoryName(root)!, "CS2IPChanger-TESTE*")) try { Directory.Delete(d, true); } catch { }
        }
    }

    static async Task Cleanup()
    {
        foreach (var n in new[] { Kill, Prog, Fake }) Firewall.Remove(n);
        foreach (var rt in (await Net.ListRoutes()).Where(x => x.Prefix == "203.0.113.77/32")) await Net.DeleteRoute(rt);
        var leftRules = CountStartingWith("CS2IPChanger TESTE") + CountStartingWith(Fake);
        var leftRoute = (await Net.ListRoutes()).Any(x => x.Prefix == "203.0.113.77/32");
        Check("Limpeza: sem regras nem rota de teste", leftRules == 0 && !leftRoute, $"regras {leftRules}, rota {leftRoute}");
    }
}
