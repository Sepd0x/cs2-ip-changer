using System.Diagnostics;
using System.IO;
using System.Text;
using SRT;
using SRT.Core;

// Auditoria do que a beta.8 não cobriu (relatório externo sobre a beta.7, 1 nov.): só o que foi confirmado no código.
static class AuditTests
{
    static bool Mklink(string args, string check)
    {
        var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c mklink " + args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        p.WaitForExit(10000);
        return p.ExitCode == 0 && (File.Exists(check) || Directory.Exists(check));
    }

    public static void Run(CoreTests.CheckFn check)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "srt-aud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try { Audit(check, tmp); }
        finally { try { SRT.Program.DeleteTree(tmp); } catch { } }
    }

    static void Audit(CoreTests.CheckFn check, string tmp)
    {
        // cs2.exe: o caminho vem do processo com o acesso mínimo (nunca PROCESS_VM_READ, que o Process.MainModule pede)
        {
            var self = Steam.ImagePath(Environment.ProcessId);
            check("Auditoria: caminho de um processo sem ler a memória dele", self != null && string.Equals(Path.GetFullPath(self), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase), self ?? "null");
            check("Auditoria: processo que não existe dá null", Steam.ImagePath(-1) == null && Steam.ImagePath(int.MaxValue - 3) == null);
        }

        // SafeFile: escrever e apagar pelo handle, no sítio certo, sem nunca seguir um link
        {
            var dir = Path.Combine(tmp, "cfg");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "a.cfg");
            SafeFile.Write(f, Encoding.UTF8.GetBytes("um"));
            SafeFile.Write(f, Encoding.UTF8.GetBytes("dois"));
            check("SafeFile: escreve e substitui, sem restos", File.ReadAllText(f) == "dois" && Directory.GetFiles(dir).Length == 1);

            // Um resto .srt-tmp que é um link para um ficheiro de fora: apagado como link, o de fora fica igual
            var outside = Path.Combine(tmp, "fora.txt");
            File.WriteAllText(outside, "intacto");
            // O link simbólico de ficheiro pede admin ou o modo de programador; sem isso estas partes saltam
            var linked = Mklink($"\"{f}.srt-tmp\" \"{outside}\"", f + ".srt-tmp");
            SafeFile.Write(f, Encoding.UTF8.GetBytes("três"));
            if (!linked) Console.WriteLine("SALTO SafeFile: resto que é link (precisa de admin ou do modo de programador)");
            else check("SafeFile: resto que é link é apagado como link, o alvo fica", File.ReadAllText(outside) == "intacto" && File.ReadAllText(f) == "três" && !File.Exists(f + ".srt-tmp"));

            // Apagar um link apaga o link, nunca o que ele aponta
            var l2 = Path.Combine(dir, "b.cfg");
            var linked2 = Mklink($"\"{l2}\" \"{outside}\"", l2);
            if (!linked2) Console.WriteLine("SALTO SafeFile: apagar um link (precisa de admin ou do modo de programador)");
            else check("SafeFile: apagar um link não toca no alvo", SafeFile.Delete(dir, "b.cfg") && File.Exists(outside) && !File.Exists(l2));

            // Pasta real esperada diferente (o que acontece quando a pasta passa a junção depois do Pin): nada é escrito nem apagado
            var other = Path.Combine(tmp, "outra");
            Directory.CreateDirectory(other);
            var junction = Path.Combine(tmp, "junc");
            var j = Mklink($"/J \"{junction}\" \"{other}\"", junction);
            var refused = false;
            try { SafeFile.Write(junction, "x.cfg", Encoding.UTF8.GetBytes("não")); } catch (IOException) { refused = true; }
            File.WriteAllText(Path.Combine(other, "y.cfg"), "fica");
            var heldOk = false;
            using (var held = SafeFile.Hold(dir)) heldOk = string.Equals(held.Real, Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
            var holdRefused = false;
            try { using var hj = SafeFile.Hold(junction); } catch (IOException) { holdRefused = true; }
            check("SafeFile: segurar uma pasta real sim, uma junção não", heldOk && holdRefused);
            check("SafeFile: pela junção não escreve nem apaga", j && refused && !File.Exists(Path.Combine(other, "x.cfg")) && !SafeFile.Delete(junction, "y.cfg") && File.Exists(Path.Combine(other, "y.cfg")));

            // Ler como admin uma pasta que outros mudam: link, hard link e ficheiro normal
            var real = Path.Combine(dir, "console.log");
            File.WriteAllText(real, "linhas");
            using (var s = SafeFile.OpenRead(real)) check("SafeFile: lê um ficheiro normal", new StreamReader(s).ReadToEnd() == "linhas");
            var ls = Path.Combine(dir, "link.log");
            var lh = Path.Combine(dir, "hard.log");
            var okSymbolic = Mklink($"\"{ls}\" \"{outside}\"", ls);
            var okHard = Mklink($"/H \"{lh}\" \"{outside}\"", lh);
            bool Refuses(string p) { try { using var s = SafeFile.OpenRead(p); return false; } catch (IOException) { return true; } }
            check("SafeFile: não lê por hard link", okHard && Refuses(lh));
            if (!okSymbolic) Console.WriteLine("SALTO SafeFile: não lê por link simbólico (precisa de admin ou do modo de programador)");
            else check("SafeFile: não lê por link simbólico", Refuses(ls));
        }

        // loginusers.vdf numa pasta que qualquer programa teu muda: só SteamID64 a sério (o ID vira nome de ficheiro e URL)
        {
            var vdf = "\"users\"\n{\n \"76561198000000001\"\n {\n  \"AccountName\" \"bom\"\n }\n \"76561198000000002?xml=1&a=/../../../../Windows/x\"\n {\n  \"AccountName\" \"mau\"\n }\n \"123\"\n {\n  \"AccountName\" \"curto\"\n }\n}\n";
            var list = Steam.ParseAccounts(vdf);
            check("Auditoria: loginusers.vdf só aceita SteamID64", list.Count == 1 && list[0].SteamId == "76561198000000001", string.Join(",", list.Select(a => a.SteamId)));
            check("Auditoria: avatar só dos servidores de imagens do Steam",
                Steam.AvatarUrl("https://avatars.fastly.steamstatic.com/abc_full.jpg") && Steam.AvatarUrl("https://avatars.steamstatic.com/a.jpg")
                && !Steam.AvatarUrl("http://avatars.steamstatic.com/a.jpg") && !Steam.AvatarUrl("https://evilsteamstatic.com/a.jpg")
                && !Steam.AvatarUrl("https://steamstatic.com.evil.net/a.jpg") && !Steam.AvatarUrl("https://user@avatars.steamstatic.com/a.jpg")
                && !Steam.AvatarUrl("https://avatars.steamstatic.com:8443/a.jpg") && !Steam.AvatarUrl(null));
        }

        // Open Windows Firewall: mmc.exe do System32 com o wf.msc, nunca "abrir .msc" pelo shell (o HKCU diz o que abre um .msc)
        {
            var psi = MainWindow.FirewallConsole();
            check("Auditoria: Firewall abre pelo mmc.exe do System32, sem shell",
                !psi.UseShellExecute && psi.FileName.Equals(Path.Combine(Environment.SystemDirectory, "mmc.exe"), StringComparison.OrdinalIgnoreCase)
                && psi.ArgumentList.Count == 1 && psi.ArgumentList[0].Equals(Path.Combine(Environment.SystemDirectory, "wf.msc"), StringComparison.OrdinalIgnoreCase));
            check("Auditoria: classes COM da Firewall pelo CLSID fixo", Type.GetTypeFromCLSID(Firewall.PolicyClsid) != null && Firewall.RuleClsid != Guid.Empty);
        }

        // IPs dos servidores: só dígitos (um espaço partia o argumento do netsh), sem zeros à esquerda
        {
            check("Auditoria: IP com espaço, sinal ou zero à esquerda é recusado",
                MainWindow.ValidIp("203.0.113.7") && !MainWindow.ValidIp("203.0.113. 7") && !MainWindow.ValidIp("203.0.113.+7")
                && !MainWindow.ValidIp("203.0.113.07") && !MainWindow.ValidIp("203.0.113.") && !MainWindow.ValidIp("203.0.113.7 ") && MainWindow.ValidIp("8.8.8.8"));
        }

        // ipwho.is: o IP de casa guardado (de outra rede, talvez) não é perguntado a partir daqui
        {
            check("Auditoria: IP de casa guardado não vai ao ipwho.is", !MainWindow.LookUpHome("198.51.100.4", null, cached: true)
                && MainWindow.LookUpHome("198.51.100.4", null, cached: false) && !MainWindow.LookUpHome("198.51.100.4", "198.51.100.4", cached: false));
        }
    }
}
