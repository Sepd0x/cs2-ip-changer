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

    /// <summary>The repo's root, from this file's path at build time (tests read the app's own XAML and docs).</summary>
    internal static string Repo([System.Runtime.CompilerServices.CallerFilePath] string here = "") => Path.GetDirectoryName(Path.GetDirectoryName(here));

    static string WriteJson(string dir, string name, string json)
    {
        var f = Path.Combine(dir, name);
        File.WriteAllText(f, json);
        return f;
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

            // P1.2 (beta.8): se o Windows não trancar a pesquisa de DLLs, como admin a app não arranca; sem admin e o --restore seguem
            {
                var none = Array.Empty<string>();
                check("DLLs: trancado como admin arranca", SRT.Program.DllSearchProblem(true, true, none) == null);
                var why = SRT.Program.DllSearchProblem(false, true, none);
                check("DLLs: não trancado como admin recusa, com o que fazer", why != null && why.Contains("didn't start") && why.Contains("--restore"), why);
                check("DLLs: não trancado sem admin segue", SRT.Program.DllSearchProblem(false, false, none) == null);
                check("DLLs: não trancado, --restore segue", SRT.Program.DllSearchProblem(false, true, new[] { "--RESTORE" }) == null);
                check("DLLs: não trancado, outro argumento não salva", SRT.Program.DllSearchProblem(false, true, new[] { "--demo", "--net-moved" }) != null);
                check("DLLs: não trancado, --restore depois do relançamento segue", SRT.Program.DllSearchProblem(false, true, new[] { "--net-moved", "--restore" }) == null);
                check("DLLs: quase --restore não salva", new[] { "--restore=1", " --restore", "-restore", "restore" }.All(a => SRT.Program.DllSearchProblem(false, true, new[] { a }) != null));
            }

            // P1.3 (beta.8): a chave da Steam Web API fica cifrada (DPAPI) no settings.json; a da beta.7 em texto migra e some do disco
            {
                var f = Path.Combine(tmp, "settings.json");
                Store.Save(f, new Settings { SteamApiKey = "CHAVEDETESTE0123456789ABCDEF" });
                var raw = File.ReadAllText(f);
                var back = Store.Load(f, () => new Settings());
                check("API key: no disco não está em texto", !raw.Contains("CHAVEDETESTE") && raw.Contains("SteamApiKeySealed") && !raw.Contains("\"SteamApiKey\""), raw.Length.ToString());
                check("API key: lida de volta igual", back.SteamApiKey == "CHAVEDETESTE0123456789ABCDEF" && !back.ApiKeyLost && !back.ApiKeyMigrated);

                var old = Path.Combine(tmp, "settings-beta7.json");
                File.WriteAllText(old, "{ \"Mode\": \"servers\", \"SteamApiKey\": \" CHAVEANTIGA987 \" }");
                var mig = Store.Load(old, () => new Settings());
                check("API key: a da beta.7 em texto é lida e marcada para migrar", mig.SteamApiKey == "CHAVEANTIGA987" && mig.ApiKeyMigrated && !mig.ApiKeyLost);
                Store.Save(old, mig);
                Store.Save(old, mig);
                var after = File.ReadAllText(old) + File.ReadAllText(old + ".prev");
                check("API key: depois de 2 gravações, nem o ficheiro nem o .prev a têm em texto", !after.Contains("CHAVEANTIGA") && Store.Load(old, () => new Settings()).SteamApiKey == "CHAVEANTIGA987");

                var lost = Store.Load(WriteJson(tmp, "lost.json", "{ \"SteamApiKeySealed\": \"AQIDBAUGBwg=\" }"), () => new Settings());
                check("API key: cifrada noutro PC (ilegível) fica vazia e avisa", lost.SteamApiKey == null && lost.ApiKeyLost);
                var other = Store.Load(WriteJson(tmp, "other.json", $"{{ \"SteamApiKeySealed\": \"{Secret.Seal("X", "OutraCoisa")}\" }}"), () => new Settings());
                check("API key: um segredo de outro uso não serve", other.SteamApiKey == null && other.ApiKeyLost);
                var none = Store.Load(WriteJson(tmp, "none.json", "{ \"Mode\": \"servers\" }"), () => new Settings());
                check("API key: sem chave não avisa", none.SteamApiKey == null && !none.ApiKeyLost && !none.ApiKeyMigrated);
                foreach (var who in new[] { back, lost })
                {
                    var copy = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(who));
                    copy.ForgetApiKey();
                    var report = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(copy));
                    check($"API key: o bug report não leva o bloco cifrado nem o texto ({(who == lost ? "ilegível" : "normal")})", report["SteamApiKeySealed"] == null && report["SteamApiKey"] == null, report.ToJsonString().Length.ToString());
                }

                // Revisão: a ilegível não se perde ao gravar; colar uma nova substitui-a e apaga o aviso
                var lf = Path.Combine(tmp, "lost.json");
                Store.Save(lf, lost);
                check("API key: ilegível guardada tal como estava ao gravar", File.ReadAllText(lf).Contains("AQIDBAUGBwg=") && Store.Load(lf, () => new Settings()).ApiKeyLost);
                lost.SteamApiKey = "CHAVENOVA555";
                Store.Save(lf, lost);
                var relost = Store.Load(lf, () => new Settings());
                check("API key: colar uma nova substitui a ilegível e apaga o aviso", !lost.ApiKeyLost && relost.SteamApiKey == "CHAVENOVA555" && !relost.ApiKeyLost && !File.ReadAllText(lf).Contains("AQIDBAUGBwg="));

                // Revisão: os dois nomes no mesmo ficheiro, nas duas ordens: o texto antigo ganha e não há falso aviso
                var a1 = Store.Load(WriteJson(tmp, "both1.json", "{ \"SteamApiKey\": \"PLANA1\", \"SteamApiKeySealed\": \"AQIDBAUGBwg=\" }"), () => new Settings());
                var a2 = Store.Load(WriteJson(tmp, "both2.json", "{ \"SteamApiKeySealed\": \"AQIDBAUGBwg=\", \"SteamApiKey\": \"PLANA2\" }"), () => new Settings());
                check("API key: texto e cifra ilegível, nas duas ordens, fica o texto sem aviso", a1.SteamApiKey == "PLANA1" && a1.ApiKeyMigrated && !a1.ApiKeyLost && a2.SteamApiKey == "PLANA2" && a2.ApiKeyMigrated && !a2.ApiKeyLost,
                    $"{a1.SteamApiKey}/{a1.ApiKeyLost} {a2.SteamApiKey}/{a2.ApiKeyLost}");
                var blank = Store.Load(WriteJson(tmp, "blank.json", "{ \"SteamApiKey\": \"   \" }"), () => new Settings());
                check("API key: texto só com espaços não migra nem avisa", blank.SteamApiKey == null && !blank.ApiKeyMigrated && !blank.ApiKeyLost);

                // Revisão: um settings.json e um .prev da beta.7, os dois com a chave em texto
                var b7 = Path.Combine(tmp, "b7", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(b7)!);
                File.WriteAllText(b7, "{ \"SteamApiKey\": \"CHAVEB7NOVA\" }");
                File.WriteAllText(b7 + ".prev", "{ \"SteamApiKey\": \"CHAVEB7VELHA\" }");
                var s7 = Store.Load(b7, () => new Settings());
                Store.Save(b7, s7);
                Store.Save(b7, s7);
                check("API key: o .prev da beta.7 também perde o texto", !File.ReadAllText(b7).Contains("CHAVEB7") && !File.ReadAllText(b7 + ".prev").Contains("CHAVEB7"));
            }

            // beta.8 (o195): estatísticas anónimas obrigatórias. Aviso antes da app abrir (Accept ou Quit); ID novo cada dia, só aleatório
            {
                var tf = Path.Combine(tmp, "telemetry.json");
                (string, string, Dictionary<string, int>) Now() => ("cs2open", "none", new());
                Telemetry.Start(false, Now, false, tf);
                check("Telemetria: sem resposta não envia nem cria o ID", !Telemetry.Running && !File.Exists(tf));

                // Quit (ou Escape): a app fecha antes de criar seja o que for; nem settings, nem telemetry.json, nem a pasta
                var fresh = Path.Combine(tmp, "quit", "SRT_Dados");
                var asked = 0;
                var go = App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, Path.Combine(fresh, "settings.json"));
                check("Telemetria: Quit fecha sem enviar nem criar nada", !go && asked == 1 && !App.StatsJustAccepted && !Directory.Exists(Path.GetDirectoryName(fresh)!) && !Telemetry.Running);
                check("Telemetria: o demo não pergunta", App.StatsGate(new[] { "--demo" }, () => { asked++; return false; }, Path.Combine(fresh, "settings.json")) && asked == 1);
                // Quem vem da beta.7 (sem campo) e da beta.8 de teste (UsageStats sim ou não) vê o aviso; quem aceitou não volta a ver
                foreach (var (name, json) in new[] { ("b7", "{ \"Mode\": \"servers\" }"), ("b8-sim", "{ \"UsageStats\": true }"), ("b8-nao", "{ \"UsageStats\": false }"), ("estragado", "{ nao é json") })
                    check($"Telemetria: settings {name} vê o aviso", !App.StatsAcceptedIn(WriteJson(tmp, $"s-{name}.json", json)) && !Store.Load(Path.Combine(tmp, $"s-{name}.json"), () => new Settings()).StatsAccepted);
                var accepted = Path.Combine(tmp, "s-aceite.json");
                Store.Save(accepted, new Settings { StatsAccepted = true });
                check("Telemetria: Accept fica guardado e não volta a perguntar", App.StatsAcceptedIn(accepted)
                    && App.StatsGate(Array.Empty<string>(), () => { asked++; return false; }, accepted) && asked == 1);

                // O ID (beta.9): aleatório, feito uma vez por instalação e guardado; um de antes da beta.9 (o diário da beta.8, o fixo da beta.7) é trocado
                var id1 = Telemetry.InstallId(null, false);
                check("Telemetria: ID da instalação fica o mesmo (dias, arranques)", Telemetry.InstallId(id1, true) == id1 && System.Text.RegularExpressions.Regex.IsMatch(id1, "^[0-9a-f]{32}$"));
                check("Telemetria: ID aleatório, dois PCs não têm o mesmo", Telemetry.InstallId(null, false) != Telemetry.InstallId(null, false));
                check("Telemetria: ID de antes da beta.9 ou estragado é trocado", Telemetry.InstallId(id1, false) != id1 && Telemetry.InstallId("nope", true) != "nope");
                // telemetry.json da beta.7/8: o ID antigo é gravado por cima logo à 1.ª leitura; o Mine fica; depois fica sempre o mesmo
                File.WriteAllText(tf, "{ \"Id\": \"0123456789abcdef0123456789abcdef\", \"Day\": \"2026-11-01\", \"Mine\": true, \"Clicks\": { \"ChangeIp_Click\": 3 } }");
                Telemetry.EnsureId(tf);
                var t1 = File.ReadAllText(tf);
                Telemetry.EnsureId(tf);
                var t2 = File.ReadAllText(tf);
                var idOf = (string t) => System.Text.RegularExpressions.Regex.Match(t, "\"Id\": \"([0-9a-f]{32})\"").Groups[1].Value;
                check("Telemetria: o ID da beta.7/8 nunca mais é usado", !t1.Contains("0123456789abcdef") && t1.Contains("\"Mine\": true") && t1.Contains("\"Install\": true"), t1);
                check("Telemetria: depois fica o mesmo ID", idOf(t1) != "" && idOf(t1) == idOf(t2), t2);
                // revisão: nem no .prev (o Store.Save guarda o ficheiro de antes)
                var prev = File.Exists(tf + ".prev") ? File.ReadAllText(tf + ".prev") : "";
                check("Telemetria: o .prev também não guarda o ID de antes da beta.9", !prev.Contains("0123456789abcdef"), prev);
                // revisão: os tamanhos das listas vão por intervalos, nunca o número exato
                check("Telemetria: tamanhos das listas por intervalos", new[] { 0, 1, 2, 5, 6, 20, 21, 100, 101, 5000 }.Select(Telemetry.Band).SequenceEqual(new[] { 0, 1, 2, 2, 6, 6, 21, 21, 101, 101 }));
                // revisão: settings.json estragado com o .prev aceite não volta a mostrar o aviso (como o Store.Load)
                var est = WriteJson(tmp, "s-est.json", "{ estragado");
                File.WriteAllText(est + ".prev", "{ \"StatsAccepted\": true }");
                check("Telemetria: settings estragado com o .prev aceite não pergunta outra vez", App.StatsAcceptedIn(est) && !File.Exists(Directory.GetFiles(tmp, "s-est.json.broken*").FirstOrDefault() ?? "x"));
                check("Telemetria: depois de uma atualização, o aviso diz que Quit desliga a proteção que ficou", Telemetry.NoticeAfterUpdate.Contains("Quit also turns it off"));

                // O texto (beta.9, ID fixo = pseudónimo no RGPD): obrigatório, a lista do que vai, e nunca chama anónimo ao relatório com ID.
                // "anonymous" só onde é verdade: o mapa de calor e o relatório de ambiente, sem ID.
                var n = Telemetry.NoticeText;
                var primeira = n[..n.IndexOf('\n')];
                check("Telemetria: o aviso diz obrigatório e nunca chama anónimo ao relatório com ID", Telemetry.NoticeTitle == "Usage stats" && n.Contains("using it means")
                    && primeira.Contains("usage stats") && !primeira.Contains("anonymous", StringComparison.OrdinalIgnoreCase)
                    && n.Contains("random ID made for this install, with no name")
                    && n.Split('\n').Where(l => l.Contains("anonymous", StringComparison.OrdinalIgnoreCase)).All(l => l.Contains("coarse grid") || l.Contains("no ID")));
                check("Telemetria: o texto do Settings não chama anónimo ao relatório com ID", File.ReadAllText(Path.Combine(Repo(), "src", "MainWindow.xaml"))
                    is var xs && xs.Contains("Text=\"Usage stats, required while the app is in beta, with a random ID made for this install, with no name") && !xs.Contains("Anonymous usage stats"));
                check("Telemetria: o aviso diz o que vai e o que nunca vai", n.Contains("random ID made for this install") && n.Contains("Delete my stats") && n.Contains("country") && n.Contains("errors")
                    && n.Contains("Never IPs, servers, accounts") && n.Contains("Quit closes the app without changing anything"));
            }

            // o192 (beta.8): o instalador. Oferecido só quando a pasta da app é o problema; copia o próprio exe e o SRT_Dados sem links
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                check("Instalar: oferecido numa pasta do utilizador", Installer.Offer(tmp));
                check("Instalar: não oferecido já na pasta de instalação nem em Program Files", !Installer.Offer(tmp, tmp) && !Installer.Offer(programFiles));
                check("Instalar: destino numa pasta do utilizador recusado", Installer.TargetProblem(Path.Combine(tmp, "CS2 IP Changer")) != null && Installer.TargetProblem(tmp) != null);
                check("Instalar: destino novo em Program Files aceite", Installer.TargetProblem(Path.Combine(programFiles, "srt-nao-existe-" + Guid.NewGuid().ToString("N")[..6])) == null);
                check("Instalar: Install para uma pasta do utilizador não copia nada", Throws(() => Installer.Install(tmp, Path.Combine(tmp, "destino"), "0.1.0-beta.8")) && !Directory.Exists(Path.Combine(tmp, "destino")));

                // Revisão: a janela só abre na cópia de ProgramData, e só com uma pasta de origem completa que existe
                var stage = Path.Combine(tmp, "stage");
                Directory.CreateDirectory(stage);
                check("Instalar: a cópia preparada lê a pasta de origem", Installer.StagedSource(new[] { Installer.FromArg, tmp + "\\", SRT.Program.Moved }, stage + "\\", stage) == tmp);
                check("Instalar: fora da pasta preparada o argumento não conta", Installer.StagedSource(new[] { Installer.FromArg, tmp }, tmp, stage) == null);
                check("Instalar: origem relativa, que não existe ou em falta não conta", Installer.StagedSource(new[] { Installer.FromArg, "Downloads" }, stage, stage) == null
                    && Installer.StagedSource(new[] { Installer.FromArg, Path.Combine(tmp, "nao-existe") }, stage, stage) == null && Installer.StagedSource(new[] { Installer.FromArg }, stage, stage) == null);
                check("Instalar: a pasta preparada fica em ProgramData\\CS2IPChanger", Installer.StageDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase)
                    && Installer.StageDir.EndsWith(@"CS2IPChanger\install", StringComparison.OrdinalIgnoreCase));

                // Revisão: nunca leva uma versão instalada para trás
                check("Instalar: mais nova ou igual instalada fica; mais antiga ou nenhuma é substituída", Installer.KeepExe("0.1.0-beta.9", "0.1.0-beta.8") && Installer.KeepExe("0.1.0-beta.8", "0.1.0-beta.8+dev")
                    && !Installer.KeepExe("0.1.0-beta.7", "0.1.0-beta.8") && !Installer.KeepExe(null, "0.1.0-beta.8") && !Installer.KeepExe("lixo", "0.1.0-beta.8"));

                var inst = Path.Combine(tmp, "inst");
                Directory.CreateDirectory(inst);
                var exe = Path.Combine(inst, "CS2IPChanger.exe");
                using (var self = Installer.OpenSelf())
                {
                    check("Instalar: o exe aberto é o que está a correr", self.Length > 0);
                    Installer.CopyExe(self, exe);
                }
                check("Instalar: a cópia do exe é igual byte a byte", Updater.Sha256Of(exe) == Updater.Sha256Of(Environment.ProcessPath!));
                using (var busy = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var self = Installer.OpenSelf())
                {
                    var msg = "";
                    try { Installer.CopyExe(self, exe); } catch (Exception ex) { msg = ex.Message; }
                    check("Instalar: exe em uso no destino dá uma mensagem clara e não deixa .tmp", msg.Contains("in use") && !File.Exists(exe + ".tmp"), msg);
                }

                // SRT_Dados: ficheiros normais e subpastas vão; junção, hard link e link simbólico ficam de fora
                var src = Path.Combine(tmp, "dados-origem");
                var secret = Path.Combine(tmp, "segredo");
                Directory.CreateDirectory(Path.Combine(src, "profiles", "sessions"));
                Directory.CreateDirectory(secret);
                File.WriteAllText(Path.Combine(secret, "senha.txt"), "nao copiar");
                File.WriteAllText(Path.Combine(src, "settings.json"), "{}");
                File.WriteAllText(Path.Combine(src, "profiles", "sessions", "765.bin"), "x");
                var linked = Mklink($"/J \"{Path.Combine(src, "junta")}\" \"{secret}\"", Path.Combine(src, "junta"))
                    & Mklink($"/H \"{Path.Combine(src, "duro.txt")}\" \"{Path.Combine(secret, "senha.txt")}\"", Path.Combine(src, "duro.txt"));
                // O link simbólico pede admin ou o modo de programador; sem isso a parte dele salta
                var symbolic = Mklink($"\"{Path.Combine(src, "simbolico.txt")}\" \"{Path.Combine(secret, "senha.txt")}\"", Path.Combine(src, "simbolico.txt"));
                if (!symbolic) Console.WriteLine("SALTO Instalar: link simbólico de teste (precisa de admin ou do modo de programador)");
                check("Instalar: links de teste criados", linked);
                var dst = Path.Combine(tmp, "dados-destino");
                var (files, skipped) = Installer.CopyData(src, dst);
                var copied = Directory.GetFiles(dst, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dst, f)).OrderBy(f => f).ToList();
                check("Instalar: SRT_Dados copiado sem junção, hard link nem link simbólico", files == 2 && skipped == (symbolic ? 3 : 2)
                    && copied.SequenceEqual(new[] { Path.Combine("profiles", "sessions", "765.bin"), "settings.json" })
                    && !copied.Any(f => File.ReadAllText(Path.Combine(dst, f)).Contains("nao copiar")), $"{files}/{skipped}: {string.Join(", ", copied)}");
                check("Instalar: a origem fica como estava", File.Exists(Path.Combine(src, "settings.json")) && File.Exists(Path.Combine(secret, "senha.txt")));

                // Revisão: a própria pasta de origem trocada por uma junção não é seguida (nada de outros sítios vai parar a Program Files)
                var rootLink = Path.Combine(tmp, "dados-juncao");
                if (Mklink($"/J \"{rootLink}\" \"{secret}\"", rootLink))
                {
                    var r = Installer.CopyData(rootLink, Path.Combine(tmp, "dados-destino2"));
                    check("Instalar: SRT_Dados que é uma junção não é copiado", r == (-1, 0) && !Directory.Exists(Path.Combine(tmp, "dados-destino2")), r.ToString());
                    // Segunda revisão: e o InstallData diz claramente que não copiou nada, em vez de "1 ficheiro deixado de fora"
                    var from5 = Path.Combine(tmp, "orig5");
                    Directory.CreateDirectory(from5);
                    if (Mklink($"/J \"{Path.Combine(from5, "SRT_Dados")}\" \"{secret}\"", Path.Combine(from5, "SRT_Dados")))
                    {
                        var msg5 = "";
                        try { Installer.InstallData(from5, Path.Combine(tmp, "dest5")); } catch (IOException ex) { msg5 = ex.Message; }
                        check("Instalar: SRT_Dados recusado inteiro diz o que fazer e não deixa .partial", msg5.Contains("wasn't copied") && msg5.Contains("yourself")
                            && !Directory.Exists(Path.Combine(tmp, "dest5", "SRT_Dados.partial")) && !Directory.Exists(Path.Combine(tmp, "dest5", "SRT_Dados")), msg5);
                        Directory.Delete(Path.Combine(from5, "SRT_Dados"));
                    }
                    Directory.Delete(rootLink);
                }
                else check("Instalar: junção de teste criada", false);

                // Revisão: um owned.json posto na pasta antiga não passa a ser de confiança; restos de gravações também não vão
                var src3 = Path.Combine(tmp, "dados3");
                Directory.CreateDirectory(src3);
                foreach (var f in new[] { "owned.json", "running.flag", "settings.json.tmp", "settings.json.broken-20261101-120000", "settings.json", "settings.json.prev" })
                    File.WriteAllText(Path.Combine(src3, f), "{}");
                var dst3 = Path.Combine(tmp, "dados3-destino");
                Installer.CopyData(src3, dst3);
                var got3 = Directory.GetFiles(dst3).Select(Path.GetFileName).OrderBy(f => f).ToList();
                check("Instalar: owned.json, running.flag, .tmp e .broken ficam de fora", got3.SequenceEqual(new[] { "settings.json", "settings.json.prev" }), string.Join(", ", got3));

                // Revisão: uma cópia cortada a meio (.partial) não conta como SRT_Dados feito; com SRT_Dados no destino, fica o de lá
                var from4 = Path.Combine(tmp, "orig4");
                var to4 = Path.Combine(tmp, "dest4");
                Directory.CreateDirectory(Path.Combine(from4, "SRT_Dados"));
                File.WriteAllText(Path.Combine(from4, "SRT_Dados", "settings.json"), "{}");
                Directory.CreateDirectory(Path.Combine(to4, "SRT_Dados.partial"));
                File.WriteAllText(Path.Combine(to4, "SRT_Dados.partial", "meio.json"), "x");
                var d4 = Installer.InstallData(from4, to4);
                check("Instalar: um .partial antigo é refeito e acaba como SRT_Dados", d4 == (1, 0, false) && File.Exists(Path.Combine(to4, "SRT_Dados", "settings.json"))
                    && !File.Exists(Path.Combine(to4, "SRT_Dados", "meio.json")) && !Directory.Exists(Path.Combine(to4, "SRT_Dados.partial")), d4.ToString());
                // Segunda revisão: o SRT_Dados do destino está numa pasta do utilizador aqui (tmp), como um movido em vez de copiado
                var msg4 = "";
                try { Installer.InstallData(from4, to4); } catch (IOException ex) { msg4 = ex.Message; }
                check("Instalar: SRT_Dados no destino que outros podem mudar não é aceite, diz o que fazer", msg4.Contains("moved there instead of copied"), msg4);

                // Segunda revisão (C1): como admin ao lado de um exe que outros mudam (--restore em Downloads), nada se escreve no SRT_Dados
                var logFile = Paths.File("registo.log");
                var before = File.Exists(logFile) ? new FileInfo(logFile).Length : -1;
                Paths.ReadOnly = true;
                try
                {
                    Log.Info("teste ReadOnly: esta linha não vai para o ficheiro");
                    var after = File.Exists(logFile) ? new FileInfo(logFile).Length : -1;
                    check("Só leitura: o Log não escreve no ficheiro e as escritas no SRT_Dados falham", after == before
                        && Throws(() => Paths.NoLink(Paths.File("x.json"))) && Throws(() => Store.Save(Path.Combine(tmp, "ro.json"), new { a = 1 })) && !File.Exists(Path.Combine(tmp, "ro.json")));
                }
                finally { Paths.ReadOnly = false; }

                // Segunda revisão: a pasta preparada ocupada (janela Install aberta) é reconhecida; Stage numa pasta do utilizador recusa
                var busyDir = Path.Combine(tmp, "busy");
                Directory.CreateDirectory(busyDir);
                check("Instalar: sem cópia preparada não está ocupada", !Installer.StageBusy(busyDir));
                File.WriteAllText(Path.Combine(busyDir, "CS2IPChanger.exe"), "x");
                using (new FileStream(Path.Combine(busyDir, "CS2IPChanger.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    check("Instalar: cópia preparada em uso está ocupada", Installer.StageBusy(busyDir));
                if (new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                    using (var self = Installer.OpenSelf())
                    {
                        var stagedExe = Installer.Stage(self, Path.Combine(tmp, "stage-ok", "install"));
                        check("Instalar: a cópia preparada é igual e fica numa pasta só de admins", Updater.Sha256Of(stagedExe) == Updater.Sha256Of(Environment.ProcessPath!)
                            && Adb.AdminOnly(Path.GetDirectoryName(stagedExe)!) && !File.Exists(stagedExe + ".tmp"));
                    }
                else Console.WriteLine("SALTO Instalar: cópia preparada (precisa de admin)");

                var text = SRT.Program.InstallText(@"C:\Users\x\Downloads", @"C:\Program Files\CS2 IP Changer", false);
                check("Instalar: o texto diz porquê, para onde e que nada é movido", text.Contains(@"C:\Users\x\Downloads") && text.Contains(@"C:\Program Files\CS2 IP Changer")
                    && text.Contains("SRT_Dados") && text.Contains("Nothing in that folder is moved or deleted"));
                check("Instalar: com SRT_Dados no destino, diz que fica o de lá", SRT.Program.InstallText("a", "b", true).Contains("isn't copied"));
                check("Instalar: o texto diz quando fica uma versão mais nova e quando substitui uma antiga",
                    SRT.Program.InstallText("a", "b", false, "0.1.0-beta.9", "0.1.0-beta.8").Contains("Install keeps it")
                    && SRT.Program.InstallText("a", "b", false, "0.1.0-beta.7", "0.1.0-beta.8").Contains("replaces version 0.1.0-beta.7"));

                // Ponto 7: o resultado do --restore na janela da app diz o mesmo que a caixa do Windows dizia
                var ok = SRT.App.RestoredText(null, false);
                var bad = SRT.App.RestoredText("route 203.0.113.37/32", true);
                check("Mensagens: --restore limpo", ok.Title == "Network restored" && ok.Text.Contains("are gone") && !ok.Text.Contains("Another copy"));
                check("Mensagens: --restore com restos e outra cópia aberta", bad.Title == "Network not fully restored" && bad.Text.Contains("Couldn't remove: route 203.0.113.37/32")
                    && bad.Text.Contains("Restart the PC") && bad.Text.Contains("Another copy of the app is still running"));
                check("Mensagens: só outra cópia aberta não é limpo", SRT.App.RestoredText(null, true).Title == "Network not fully restored");
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

            // P1.5 (beta.8): o signatário do ADB é lido campo a campo (O=Google LLC e C=US), não por texto
            {
                bool G(string dn) => Adb.IsGoogleSubject(new System.Security.Cryptography.X509Certificates.X500DistinguishedName(dn));
                check("ADB: o nome real do certificado da Google passa", G("CN=Google LLC, O=Google LLC, L=Mountain View, S=California, C=US, SERIALNUMBER=3582691"));
                var quoted = new System.Security.Cryptography.X509Certificates.X500DistinguishedName("CN=Evil, OU=\"a, O=Google LLC, b\", O=Evil Corp, C=US");
                check("ADB: o texto antigo aceitava um OU entre aspas com O=Google LLC", System.Text.RegularExpressions.Regex.IsMatch(quoted.Name, @"(^|,\s*)O=Google LLC(,|$)"), quoted.Name);
                check("ADB: esse OU entre aspas já não passa", !Adb.IsGoogleSubject(quoted));
                check("ADB: dois O, O parecido, minúsculas, sem país ou outro país não passam",
                    !G("O=Google LLC, O=Evil, C=US") && !G("O=Google LLC Evil, C=US") && !G("O=google llc, C=US") && !G("CN=Google LLC, O=Google LLC") && !G("O=Google LLC, C=PT"));
                check("ADB: RDN com vários valores não passa", !G("CN=x+O=Google LLC, C=US"));
                check("ADB: sem O, O com espaço no fim e país em minúsculas não passam", !G("CN=Google LLC, C=US") && !G("O=\"Google LLC \", C=US") && !G("O=Google LLC, C=us"));
                var adb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CS2IPChanger", "adb", "adb.exe");
                if (File.Exists(adb)) check("ADB: o adb.exe instalado continua aceite", Adb.IsGoogleSigned(adb));
                check("ADB: um ficheiro sem assinatura não passa", !Adb.IsGoogleSigned(Path.Combine(tmp, "normal.txt")));
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
