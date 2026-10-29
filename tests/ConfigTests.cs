using System.IO;
using System.IO.Compression;
using SRT.Core;

// Configs do CS2 entre contas: só em pastas temporárias com um userdata inventado. Nunca toca no Steam deste PC.
static class ConfigTests
{
    const string A = "76561198000000001", B = "76561198000000002", C = "76561198000000003";

    static string Convars(string sens, string size, string gap) => Cs2Config.Crlf($$"""
        "config"
        {
        	"convars"
        	{
        		"cl_crosshairgap"		"{{gap}}"
        		"cl_crosshairsize"		"{{size}}"
        		"cl_crosshairstyle"		"4"
        		"cl_crosshaircolor"		"1"
        		"sensitivity"		"{{sens}}"
        		"viewmodel_fov"		"68"
        	}
        }
        """);

    static string Keys(string mouse4) => Cs2Config.Crlf($$"""
        "config"
        {
        	"bindings"
        	{
        		"MOUSE4"		"{{mouse4}}"
        		"e"		"+use"
        	}
        }
        """);

    static string Video(string w, string h) => Cs2Config.Crlf($$"""
        "video.cfg"
        {
        	"Version"		"16"
        	"setting.defaultres"		"{{w}}"
        	"setting.defaultresheight"		"{{h}}"
        	"setting.fullscreen"		"1"
        }
        """);

    static void Put(string userData, string id, string rel, string text)
    {
        var p = Path.Combine(userData, Cs2Config.Id32(id), "730", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    static string Get(string userData, string id, string rel)
    {
        var p = Path.Combine(userData, Cs2Config.Id32(id), "730", rel);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    // Convars com as chaves dadas, no formato do CS2
    static string ConvarsOf(params (string K, string V)[] kv) =>
        Cs2Config.Crlf("\"config\"\n{\n\t\"convars\"\n\t{\n" + string.Concat(kv.Select(x => $"\t\t\"{x.K}\"\t\t\"{x.V}\"\n")) + "\t}\n}\n");

    // Uma junção (mklink /J não precisa de admin); false se não deu para a criar
    static bool Junction(string link, string target, List<string> links)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit(10000);
            if (!Directory.Exists(link) || !File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint)) return false;
            links.Add(link);
            return true;
        }
        catch { return false; }
    }

    static void Unlink(string link, List<string> links) { Directory.Delete(link); links.Remove(link); }   // só a junção

    static string Listing(string dir) => string.Join("|", Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f).Select(f => Path.GetFileName(f) + "=" + File.ReadAllText(f)));

    // Casos da auditoria: links e junções, backups adulterados, nomes de snapshots, a mira nova, a fila, só de leitura
    static void Run2(Action<string, bool, string> check, string root, List<string> links)
    {
        const string D = "76561198000000004", E = "76561198000000005", F = "76561198000000006";
        var kL = Cs2Config.PathsOf(CfgPart.Keys).Local;
        var cL = Cs2Config.PathsOf(CfgPart.Convars).Local;
        var vL = Cs2Config.PathsOf(CfgPart.Video).Local;
        var mL = Cs2Config.PathsOf(CfgPart.Machine).Local;
        var ud = Path.Combine(root, "steam2", "userdata");
        Put(ud, D, kL, Keys("+jump")); Put(ud, D, vL, Video("1920", "1080"));
        Put(ud, E, kL, Keys("<unbound>")); Put(ud, E, vL, Video("1280", "960"));
        var outside = Path.Combine(root, "fora");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "cs2_video.txt"), "SEGREDO");
        File.WriteAllText(Path.Combine(outside, "cs2_user_keys_0_slot0.vcfg"), Keys("fora"));
        var store = new Cs2ConfigStore(ud, Path.Combine(root, "store2")) { Cs2Running = () => false };
        var d730 = Path.Combine(ud, Cs2Config.Id32(D), "730");
        var e730 = Path.Combine(ud, Cs2Config.Id32(E), "730");

        // A fonte com local\cfg em junção para fora do userdata: nada é lido nem copiado (a app é admin)
        var srcCfg = Path.Combine(d730, "local", "cfg");
        Directory.Move(srcCfg, srcCfg + "-real");
        if (Junction(srcCfg, outside, links))
        {
            var r = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "video", "binds" }, Reason = "copy" }, "j1");
            check("Configs: fonte em junção não é lida", store.Read(D).Empty && r.Outcome == CfgOutcome.Skipped && store.Changed(D) == null && !store.HasConfig(D)
                && Cs2Config.SameText(Get(ud, E, vL), Video("1280", "960")), r.Outcome.ToString());
            Unlink(srcCfg, links);
        }
        else Console.WriteLine("SALTO Configs: fonte em junção (mklink /J não correu)");
        Directory.Move(srcCfg + "-real", srcCfg);

        // O destino com local\cfg em junção: cópia e restauro recusados, nada escrito do outro lado
        var first = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "copy" }, "j2");
        var eBackup = store.Backups().First(b => b.SteamId == E);
        var dstCfg = Path.Combine(e730, "local", "cfg");
        var before = Listing(outside);
        Directory.Move(dstCfg, dstCfg + "-real");
        if (Junction(dstCfg, outside, links))
        {
            var r = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "video", "binds" }, Reason = "copy" }, "j3");
            var threw = false;
            try { store.Restore(eBackup, "j4"); } catch (IOException) { threw = true; }
            check("Configs: destino em junção recusado", first.Outcome == CfgOutcome.Copied && r.Outcome == CfgOutcome.Failed && threw && Listing(outside) == before, $"{r.Outcome} {r.Detail}");
            Unlink(dstCfg, links);
        }
        else Console.WriteLine("SALTO Configs: destino em junção (mklink /J não correu)");
        Directory.Move(dstCfg + "-real", dstCfg);

        // Uma pasta "remote" que ainda não existe, criada como junção: recusada também
        Put(ud, E, "remotecache.vdf", "\"730\"\n{\n}\n");
        Put(ud, D, kL, Keys("+duck"));
        if (Junction(Path.Combine(e730, "remote"), outside, links))
        {
            var r = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "copy" }, "j3b");
            check("Configs: remote em junção recusado", r.Outcome == CfgOutcome.Failed && Listing(outside) == before && Cs2Config.SameText(Get(ud, E, kL), Keys("+jump")), $"{r.Outcome} {r.Detail}");
            Unlink(Path.Combine(e730, "remote"), links);
        }
        else Console.WriteLine("SALTO Configs: remote em junção (mklink /J não correu)");
        File.Delete(Path.Combine(e730, "remotecache.vdf"));

        // O próprio userdata em junção: recusado ao escrever e ao ler
        store.SaveSnapshot("S", store.Read(D), "D");
        var udLink = Path.Combine(root, "steam3", "userdata");
        if (Junction(udLink, ud, links))
        {
            var s3 = new Cs2ConfigStore(udLink, Path.Combine(root, "store2")) { Cs2Running = () => false };
            var r = s3.Push(new CfgJob { Snapshot = "S", Target = E, Categories = new() { "video" }, Reason = "copy" }, "j5");
            check("Configs: userdata em junção recusado", r.Outcome == CfgOutcome.Failed && s3.Read(D).Empty && Cs2Config.SameText(Get(ud, E, vL), Video("1280", "960")), $"{r.Outcome} {r.Detail}");
            // A leitura recusada fica no Log com a razão, sem caminho nem SteamID, e uma vez só por conta
            var warns = new List<string>();
            void OnLog(LogEntry e) { if (e.Level == LogLevel.Warn) warns.Add(e.Text); }
            Log.Added += OnLog;
            try { s3.Read(F); s3.Read(F); } finally { Log.Added -= OnLog; }
            var w = warns.Where(x => x.StartsWith("Config: an account's CS2 config couldn't be read")).ToList();
            check("Configs: leitura recusada fica no Log, sem caminho", w.Count == 1 && w[0].Contains("link or junction") && !w[0].Contains('\\') && !w[0].Contains(F) && !w[0].Contains(Cs2Config.Id32(F)), string.Join(" | ", warns));
            Unlink(udLink, links);
        }
        else Console.WriteLine("SALTO Configs: userdata em junção (mklink /J não correu)");

        // A pasta do Steam em junção (mudada para outro disco) continua a funcionar
        var steamLink = Path.Combine(root, "steam4");
        if (Junction(steamLink, Path.Combine(root, "steam2"), links))
        {
            var s4 = new Cs2ConfigStore(Path.Combine(steamLink, "userdata"), Path.Combine(root, "store4")) { Cs2Running = () => false };
            var r = s4.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "video" }, Reason = "copy" }, "j6");
            check("Configs: pasta do Steam em junção funciona", r.Outcome == CfgOutcome.Copied && !s4.Read(D).Empty && Cs2Config.SameText(Get(ud, E, vL), Video("1920", "1080")), $"{r.Outcome} {r.Detail}");
            Unlink(steamLink, links);
        }
        else Console.WriteLine("SALTO Configs: pasta do Steam em junção (mklink /J não correu)");

        // Um .srt-tmp deixado na pasta: substituído, nunca aberto, e não fica nenhum
        File.WriteAllText(Path.Combine(dstCfg, "cs2_user_keys_0_slot0.vcfg.srt-tmp"), "lixo");
        var tmp = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "copy" }, "j7");
        check("Configs: .srt-tmp antigo substituído", tmp.Outcome == CfgOutcome.Copied && Cs2Config.SameText(Get(ud, E, kL), Keys("+duck"))
            && Directory.GetFiles(e730, "*.srt-tmp", SearchOption.AllDirectories).Length == 0, tmp.Outcome.ToString());

        // Um backup.json adulterado com caminhos para fora: nada é apagado nem escrito fora dos ficheiros do CS2
        var victimUd = Path.Combine(ud, "victim.txt");
        var victimAbs = Path.Combine(root, "victim.txt");
        var victimX = Path.Combine(e730, "x", "b.txt");
        File.WriteAllText(victimUd, "V1"); File.WriteAllText(victimAbs, "V2");
        Directory.CreateDirectory(Path.GetDirectoryName(victimX)!); File.WriteAllText(victimX, "V3");
        var tb = store.Backups().First(b => b.SteamId == E && b.SyncId == "j7");
        Directory.CreateDirectory(Path.Combine(tb.Dir, "x"));
        File.WriteAllText(Path.Combine(tb.Dir, "x", "a.txt"), "MAU");
        tb.Files.AddRange(new[] { "x\\a.txt", "..\\..\\victim.txt", "../../victim.txt", "local/../../victim.txt", victimAbs, "C:victim.txt" });
        tb.Created.AddRange(new[] { "x\\b.txt", "..\\..\\victim.txt", victimAbs, "local\\cfg\\..\\..\\x\\b.txt" });
        Store.Save(Path.Combine(tb.Dir, "backup.json"), tb);
        var tampered = store.Backups().First(b => b.SteamId == E && b.SyncId == "j7");
        var restored = store.Restore(tampered, "j8");
        check("Configs: backup adulterado não apaga fora", restored && File.ReadAllText(victimUd) == "V1" && File.ReadAllText(victimAbs) == "V2" && File.ReadAllText(victimX) == "V3", "");
        check("Configs: backup adulterado não escreve fora", !File.Exists(Path.Combine(e730, "x", "a.txt")) && Cs2Config.SameText(Get(ud, E, kL), Keys("+jump")), Get(ud, E, kL) ?? "null");

        // Um backup ou um snapshot em junção para fora: a app (admin) não lê o que está do outro lado nem o põe numa conta
        var prot = Path.Combine(root, "protegido");
        Directory.CreateDirectory(Path.Combine(prot, "local", "cfg"));
        File.WriteAllText(Path.Combine(prot, kL), Keys("protegido"));
        File.WriteAllText(Path.Combine(prot, "keys.vcfg"), Keys("protegido"));
        Store.Save(Path.Combine(prot, "snapshot.json"), new CfgSnapshot { Name = "Evil", Created = DateTime.Now, Parts = new() { "Keys" } });
        var eKeys = Get(ud, E, kL);
        var bLink = Path.Combine(root, "store2", "backups", "junc-test");
        if (Junction(bLink, prot, links))
        {
            var jb = new CfgBackup { SteamId = E, At = DateTime.Now, Files = new() { kL }, Dir = bLink };
            var ok = store.Restore(jb, "j8b");
            check("Configs: backup em junção não é lido", ok && Get(ud, E, kL) == eKeys, Get(ud, E, kL) ?? "null");
            Unlink(bLink, links);
        }
        else Console.WriteLine("SALTO Configs: backup em junção (mklink /J não correu)");
        var sLink = Path.Combine(root, "store2", "snapshots", "Evil");
        if (Junction(sLink, prot, links))
        {
            var ez = Path.Combine(root, "evil-out.zip");
            var listed = store.FindSnapshot("Evil") != null;
            var ep = store.Push(new CfgJob { Snapshot = "Evil", Target = E, Categories = new() { "binds" }, Reason = "copy" }, "j8c");
            store.ExportSnapshot("Evil", ez);
            bool zipClean;
            using (var z = ZipFile.OpenRead(ez)) zipClean = z.Entries.All(x => x.Name != "keys.vcfg");
            check("Configs: snapshot em junção não é lido", listed && store.ReadSnapshot("Evil").Empty && ep.Outcome != CfgOutcome.Copied && Get(ud, E, kL) == eKeys && zipClean,
                $"{listed} {ep.Outcome} {zipClean}");
            Unlink(sLink, links);
        }
        else Console.WriteLine("SALTO Configs: snapshot em junção (mklink /J não correu)");

        // Nome comprido já existente: importar não pendura e cada um fica na sua pasta
        var longName = new string('L', 45);
        store.SaveSnapshot(longName, store.Read(D), "D");
        var lz = Path.Combine(root, "long.zip");
        store.ExportSnapshot(longName, lz);
        string i1 = null, i2 = null;
        var ended = Task.Run(() => { i1 = store.ImportSnapshot(lz); i2 = store.ImportSnapshot(lz); }).Wait(5000);
        var longs = store.Snapshots().Where(s => s.Name.StartsWith("LLLL")).ToList();
        check("Configs: importar nome comprido não pendura", ended && i1 != null && i2 != null && i1 != i2 && longs.Count == 3 && longs.Select(s => s.Dir).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
            $"{ended} {i1} | {i2} | {longs.Count}");

        // Dois nomes que dão a mesma pasta não se apagam um ao outro
        store.SaveSnapshot("Retakes/A", store.Read(D), "D");
        store.SaveSnapshot("Retakes_A", store.Read(E), "E");
        var ra = store.FindSnapshot("Retakes/A");
        var rb = store.FindSnapshot("Retakes_A");
        check("Configs: snapshots com a mesma pasta ficam os dois", ra != null && rb != null && !string.Equals(ra.Dir, rb.Dir, StringComparison.OrdinalIgnoreCase)
            && Cs2Config.SameText(store.ReadSnapshot("Retakes/A").Get(CfgPart.Keys), Keys("+duck")) && Cs2Config.SameText(store.ReadSnapshot("Retakes_A").Get(CfgPart.Keys), Keys("+jump")), "");

        // Zip com um ficheiro desconhecido grande: ignorado sem o abrir. Zip com ficheiros a mais: recusado
        var big = Path.Combine(root, "big.zip");
        using (var z = ZipFile.Open(big, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("readme.png").Open()) s.Write(new byte[2 * 1024 * 1024]);
            using (var w = new StreamWriter(z.CreateEntry("keys.vcfg").Open())) w.Write(Keys("+big"));
        }
        var bi = store.ImportSnapshot(big);
        check("Configs: zip com outro ficheiro grande importa", store.ReadSnapshot(bi).Files.Count == 1, bi);
        var many = Path.Combine(root, "many.zip");
        using (var z = ZipFile.Open(many, ZipArchiveMode.Create))
        {
            for (var i = 0; i < 65; i++) using (var w = new StreamWriter(z.CreateEntry($"f{i}.txt").Open())) w.Write("x");
            using (var w = new StreamWriter(z.CreateEntry("keys.vcfg").Open())) w.Write(Keys("+many"));
        }
        var manyThrew = false;
        try { store.ImportSnapshot(many); } catch (InvalidDataException) { manyThrew = true; }
        check("Configs: zip com ficheiros a mais recusado", manyThrew, "");

        // A mira nova do CS2 (estilo 7): chaves próprias, em píxeis; as antigas ficam no ficheiro sem uso
        var x7 = new CfgSet();
        x7.Files[CfgPart.Convars] = ConvarsOf(("cl_crosshairstyle", "7"), ("cl_crosshair_length", "8"), ("cl_crosshair_gap", "4"), ("cl_crosshair_thickness", "2"),
            ("cl_crosshaircolor_r", "0"), ("cl_crosshaircolor_g", "255"), ("cl_crosshaircolor_b", "0"), ("cl_crosshaircolor_a", "255"), ("cl_crosshair_drawoutline", "1"),
            ("cl_crosshairsize", "3.9"), ("cl_crosshairthickness", "0.6"), ("cl_crosshair_screen_height", "1080"));
        var sum7 = Cs2Config.CrosshairSummary(x7);
        check("Configs: mira nova (estilo 7)", sum7 == "length 8 · gap 4 · thick 2 · #00FF00 · outline" && Cs2Config.ReadCrosshair(x7).Pixels, sum7);

        // "1.000000" e "1" são o mesmo valor; só a diferença real aparece
        var n1 = new CfgSet(); n1.Files[CfgPart.Convars] = ConvarsOf(("sensitivity", "1.000000"), ("cl_crosshairgap", "-3"));
        var n2 = new CfgSet(); n2.Files[CfgPart.Convars] = ConvarsOf(("sensitivity", "1"), ("cl_crosshairgap", "-2"));
        var dn = Cs2Config.Diff(n1, n2);
        check("Configs: 1.000000 igual a 1 no diff", dn.Count == 1 && dn[0].Key == "cl_crosshairgap", string.Join(" | ", dn.Select(x => x.Key)));

        // Binds: tecla que falta de um lado e "<unbound>" do outro é igual (não aparece no Compare)
        var k1 = new CfgSet(); k1.Files[CfgPart.Keys] = Cs2Config.Crlf("\"config\"\n{\n\t\"bindings\"\n\t{\n\t\t\"MOUSE4\"\t\t\"<unbound>\"\n\t\t\"e\"\t\t\"+use\"\n\t\t\"f\"\t\t\"+lookatweapon\"\n\t}\n}\n");
        var k2 = new CfgSet(); k2.Files[CfgPart.Keys] = Cs2Config.Crlf("\"config\"\n{\n\t\"bindings\"\n\t{\n\t\t\"e\"\t\t\"+use\"\n\t\t\"f\"\t\t\"<unbound>\"\n\t}\n}\n");
        var dk = Cs2Config.Diff(k1, k2);
        check("Configs: falta de um lado e unbound do outro não é diferença", dk.Count == 1 && dk[0].Key == "f" && dk[0].B == "unbound", string.Join(" | ", dk.Select(x => $"{x.Key}:{x.A}/{x.B}")));
        var kNoFile = Cs2Config.Diff(k1, new CfgSet());
        check("Configs: sem ficheiro de binds do outro lado ainda aparece", kNoFile.Any(x => x.Key == "MOUSE4" && x.B == "no file"), string.Join(" | ", kNoFile.Select(x => $"{x.Key}:{x.B}")));

        // Vídeo: a frequência e o monitor são deste PC, não da conta (144 Hz a correr a 60); a resolução vai
        static string Vid(string w, string hz, string mon) => Cs2Config.Crlf("\"video.cfg\"\n{\n\t\"Version\"\t\t\"16\"\n\t\"setting.defaultres\"\t\t\"" + w + "\"\n" +
            (hz == null ? "" : "\t\"setting.refreshrate_numerator\"\t\t\"" + hz + "\"\n\t\"setting.refreshrate_denominator\"\t\t\"1\"\n") +
            (mon == null ? "" : "\t\"setting.monitor_index\"\t\t\"" + mon + "\"\n") + "\t\"setting.fullscreen\"\t\t\"1\"\n}\n");
        var kept = Cs2Config.Flatten(Cs2Config.KeepDisplay(Vid("1280", "60", "0"), Vid("1920", "144", "1")), CfgPart.Video);
        check("Configs: vídeo copiado fica com a frequência e o monitor do destino", kept["setting.refreshrate_numerator"] == "144" && kept["setting.monitor_index"] == "1" && kept["setting.defaultres"] == "1280",
            string.Join(" ", kept.Select(x => x.Key + "=" + x.Value)));
        var fresh = Cs2Config.Flatten(Cs2Config.KeepDisplay(Vid("1280", "60", "0"), null), CfgPart.Video);
        check("Configs: conta sem vídeo: sem frequência nem monitor (o CS2 deteta)", !fresh.ContainsKey("setting.refreshrate_numerator") && !fresh.ContainsKey("setting.monitor_index") && fresh["setting.fullscreen"] == "1",
            string.Join(" ", fresh.Keys));
        check("Configs: vídeo que só difere no ecrã não é reescrito", Cs2Config.SameSettings(CfgPart.Video, Cs2Config.KeepDisplay(Vid("1280", "60", "0"), Vid("1280", "144", "1")), Vid("1280", "144", "1")), "");
        check("Configs: vídeo que o leitor não guarda exato vai como está", Cs2Config.KeepDisplay("lixo { sem aspas", Vid("1", "2", "3")) == "lixo { sem aspas", "");
        check("Configs: nome com espaços e enchimento do Steam não deixa espaço duplo", SRT.CfgAccountRow.ShownName(" \u3164sepd ") == "sepd" && SRT.CfgAccountRow.ShownName("  ") == "this account", "");

        // Só a mira (ou só o jogo) numa conta sem ficheiro: vai só esse lado
        var solo = Cs2Config.MergeConvars(Convars("5", "9", "9"), null, true, false);
        var fs = solo == null ? null : Cs2Config.Flatten(solo, CfgPart.Convars);
        check("Configs: só a mira numa conta sem ficheiro", fs != null && !fs.ContainsKey("sensitivity") && fs["cl_crosshairsize"] == "9", solo ?? "null");
        var soloG = Cs2Config.MergeConvars(Convars("5", "9", "9"), null, false, true);
        var fg = soloG == null ? null : Cs2Config.Flatten(soloG, CfgPart.Convars);
        check("Configs: só o jogo numa conta sem ficheiro", fg != null && fg["sensitivity"] == "5" && !fg.Keys.Any(Cs2Config.IsCrosshairKey), soloG ?? "null");

        // Mira para uma conta cujo ficheiro não se mistura: falha, não "já igual"
        Put(ud, D, cL, Convars("1", "2", "3"));
        Put(ud, E, cL, Convars("2", "5", "1").Replace("\"viewmodel_fov\"\t\t\"68\"", "\"viewmodel_fov\"\t\t68"));
        var mf = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "crosshair" }, Reason = "copy" }, "j9");
        check("Configs: mira que não se mistura falha", mf.Outcome == CfgOutcome.Failed, $"{mf.Outcome} {mf.Detail}");
        File.Delete(Path.Combine(e730, cL));

        // Um ficheiro só de leitura fica como está, o resto copia, sem .srt-tmp
        var eV = Path.Combine(e730, vL);
        Put(ud, D, vL, Video("800", "600"));
        Put(ud, D, kL, Keys("+walk"));
        File.SetAttributes(eV, FileAttributes.ReadOnly);
        var ro = store.Push(new CfgJob { SourceId = D, Target = E, Categories = Cs2Config.Categories.ToList(), Reason = "copy" }, "j10");
        File.SetAttributes(eV, FileAttributes.Normal);
        check("Configs: só de leitura fica, o resto copia", ro.Outcome == CfgOutcome.Copied && !ro.Categories.Contains("video") && ro.Categories.Contains("binds")
            && Cs2Config.SameText(Get(ud, E, vL), Video("1920", "1080")) && Directory.GetFiles(e730, "*.srt-tmp", SearchOption.AllDirectories).Length == 0, $"{ro.Outcome} {string.Join(",", ro.Categories)}");
        File.SetAttributes(eV, FileAttributes.ReadOnly);
        var ro2 = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "video" }, Reason = "copy" }, "j11");
        File.SetAttributes(eV, FileAttributes.Normal);
        check("Configs: só de leitura não passa por igual", ro2.Outcome == CfgOutcome.Skipped, $"{ro2.Outcome} {ro2.Detail}");

        // HUD: valores que o CS2 reescreve sozinho não contam como mudança
        static string Machine(string party, string hud) => ConvarsOf(("cachedvalue_count_partybrowser", party), ("cl_hud_color", hud));
        Put(ud, D, mL, Machine("100", "1"));
        Put(ud, E, mL, Machine("200", "1"));
        var nb = store.Backups().Count(b => b.SteamId == E);
        var mh = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "hud" }, Reason = "copy" }, "j12");
        check("Configs: HUD só com cache é igual", mh.Outcome == CfgOutcome.Same && store.Backups().Count(b => b.SteamId == E) == nb
            && !Cs2Config.Diff(store.Read(D), store.Read(E)).Any(x => x.Category == "hud"), mh.Outcome.ToString());
        Put(ud, D, mL, Machine("100", "3"));
        var mh2 = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "hud" }, Reason = "copy" }, "j13");
        check("Configs: HUD mudado copia", mh2.Outcome == CfgOutcome.Copied, mh2.Outcome.ToString());

        // Fila: uma cópia automática não apaga o que a conta gravou no jogo depois; uma Copy pedida entra
        var cs2 = true;
        store.Cs2Running = () => cs2;
        Put(ud, D, kL, Keys("+lookatweapon"));
        var q = store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "closed", At = DateTime.Now.AddMinutes(-5) }, "j14");
        Put(ud, E, kL, Keys("+jumpthrow"));
        cs2 = false;
        var qf = store.FlushPending("j15");
        check("Configs: fila não apaga o que o jogo gravou", q.Outcome == CfgOutcome.Queued && qf.Count == 1 && qf[0].Outcome == CfgOutcome.Skipped
            && Cs2Config.SameText(Get(ud, E, kL), Keys("+jumpthrow")), string.Join(",", qf.Select(x => x.Outcome)));
        cs2 = true;
        store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "copy" }, "j16");
        Put(ud, E, kL, Keys("+jumpthrow2"));
        cs2 = false;
        var qf2 = store.FlushPending("j17");
        check("Configs: Copy em fila entra na mesma", qf2.Count == 1 && qf2[0].Outcome == CfgOutcome.Copied && Cs2Config.SameText(Get(ud, E, kL), Keys("+lookatweapon")), string.Join(",", qf2.Select(x => x.Outcome)));

        // Um Sync now que ficou na fila de uma execução anterior: a conta jogada depois sem a app ganha
        var qRoot = Path.Combine(root, "storeQ");
        var sq = new Cs2ConfigStore(ud, qRoot) { Cs2Running = () => true };
        var old = sq.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "manual", At = DateTime.Now.AddMinutes(-5) }, "j16b");
        Put(ud, E, kL, Keys("+jumpthrow3"));
        var sq2 = new Cs2ConfigStore(ud, qRoot) { Cs2Running = () => false };
        var qf3 = sq2.FlushPending("j16c");
        check("Configs: Sync now de outra execução não apaga o jogo", old.Outcome == CfgOutcome.Queued && qf3.Count == 1 && qf3[0].Outcome == CfgOutcome.Skipped
            && Cs2Config.SameText(Get(ud, E, kL), Keys("+jumpthrow3")), string.Join(",", qf3.Select(x => x.Outcome)));

        // Sync desligado ou conta opt-out: as cópias da sincronização saem da fila, a Copy pedida fica
        cs2 = true;
        store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "login" }, "j18");
        store.Push(new CfgJob { SourceId = E, Target = D, Categories = new() { "binds" }, Reason = "copy" }, "j18");
        var dropped = store.DropQueued(_ => true);
        check("Configs: desligar tira da fila só a sincronização", dropped == 1 && store.State.Pending.Count == 1 && store.State.Pending[0].Reason == "copy", $"{dropped} {store.State.Pending.Count}");
        store.State.Pending.Clear();
        store.SaveState();
        cs2 = false;

        // Desfazer com uma conta que falha: as outras voltam, e diz qual ficou
        Put(ud, E, kL, Keys("e-old"));
        Put(ud, F, kL, Keys("f-old"));
        Put(ud, D, kL, Keys("+sync"));
        store.Push(new CfgJob { SourceId = D, Target = E, Categories = new() { "binds" }, Reason = "manual" }, "u9");
        store.Push(new CfgJob { SourceId = D, Target = F, Categories = new() { "binds" }, Reason = "manual" }, "u9");
        var fK = Path.Combine(ud, Cs2Config.Id32(F), "730", kL);
        File.SetAttributes(fK, FileAttributes.ReadOnly);
        var failed = new List<string>();
        var un = store.UndoLast("u10", failed);
        File.SetAttributes(fK, FileAttributes.Normal);
        check("Configs: desfazer continua depois de uma conta falhar", un == 1 && failed.SequenceEqual(new[] { F }) && Cs2Config.SameText(Get(ud, E, kL), Keys("e-old"))
            && Cs2Config.SameText(Get(ud, F, kL), Keys("+sync")), $"{un} {string.Join(",", failed)}");

        // Backups: "1 file", "4 files", "2 new files"
        string Row(int files, int created)
        {
            var b = new CfgBackup { Reason = "copy" };
            for (var i = 0; i < files; i++) b.Files.Add("f" + i);
            for (var i = 0; i < created; i++) b.Created.Add("c" + i);
            return new SRT.CfgBackupRow(b, "x").Detail;
        }
        check("Configs: plural nos backups", Row(1, 0).EndsWith("· 1 file") && Row(4, 0).EndsWith("· 4 files") && Row(0, 2).EndsWith("· 2 new files") && Row(0, 1).EndsWith("· 1 new file"),
            $"{Row(1, 0)} | {Row(4, 0)} | {Row(0, 2)}");
    }

    public static void Run(Action<string, bool, string> check, Action<string, bool, string> env)
    {
        // Só leitura: os ficheiros reais deste PC são lidos e reescritos pelo parser sem perder nada (é o que a mistura da mira precisa)
        if (Steam.InstallDir() is string steam && Directory.Exists(Path.Combine(steam, "userdata")))
        {
            var real = new Cs2ConfigStore(Path.Combine(steam, "userdata"), Path.Combine(Path.GetTempPath(), "srt-config-read-" + Guid.NewGuid().ToString("N")[..8]));
            var ids = Directory.GetDirectories(Path.Combine(steam, "userdata")).Select(d => long.TryParse(Path.GetFileName(d), out var n) ? (n + 76561197960265728).ToString() : null)
                .Where(id => id != null && real.HasConfig(id)).ToList();
            var bad = ids.SelectMany(id => real.Read(id).Files.Where(f => !Vcfg.RoundTrips(f.Value)).Select(f => $"{Cs2Config.Id32(id)}/{f.Key}")).ToList();
            env("Configs: ficheiros reais lidos sem perdas", ids.Count > 0 && bad.Count == 0, $"{ids.Count} conta(s) com config" + (bad.Count > 0 ? ", falham: " + string.Join(", ", bad) : ""));
            if (ids.Count > 1) Console.WriteLine($"     diff real: {Cs2Config.Diff(real.Read(ids[0]), real.Read(ids[1])).Count} diferença(s); mira: {Cs2Config.CrosshairSummary(real.Read(ids[0]))}");
            try { Directory.Delete(real.Root, true); } catch { }
        }

        var root = Path.Combine(Path.GetTempPath(), "srt-config-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var ud = Path.Combine(root, "userdata");
        var links = new List<string>();
        try
        {
            // A: tudo, com nuvem. B: outra config, com nuvem. C: conta nova, sem pasta 730.
            var (kL, kC, kR) = Cs2Config.PathsOf(CfgPart.Keys);
            var (cL, cC, cR) = Cs2Config.PathsOf(CfgPart.Convars);
            var vL = Cs2Config.PathsOf(CfgPart.Video).Local;
            Put(ud, A, kL, Keys("+jump")); Put(ud, A, cL, Convars("1.2", "2", "-3")); Put(ud, A, vL, Video("1920", "1080"));
            Put(ud, A, "remotecache.vdf", "\"730\"\n{\n}\n");
            Put(ud, B, kL, Keys("<unbound>")); Put(ud, B, kC, Keys("<unbound>")); Put(ud, B, kR, Cs2Config.Lf(Keys("<unbound>")));
            Put(ud, B, cL, Convars("2", "5", "1")); Put(ud, B, cR, Cs2Config.Lf(Convars("2", "5", "1"))); Put(ud, B, vL, Video("1280", "960"));
            Put(ud, B, "remotecache.vdf", "\"730\"\n{\n}\n");
            foreach (var (id, h) in new[] { (A, -3), (B, -1) })
                foreach (var f in Directory.GetFiles(Path.Combine(ud, Cs2Config.Id32(id)), "*", SearchOption.AllDirectories)) File.SetLastWriteTime(f, DateTime.Now.AddHours(h));

            var store = new Cs2ConfigStore(ud, Path.Combine(root, "store")) { Cs2Running = () => false };
            check("Configs: id32 do SteamID64", Cs2Config.Id32(A) == "39734273", Cs2Config.Id32(A));
            check("Configs: conta lida", store.Read(A).Files.Count == 3, "");
            check("Configs: sem sync, nada para desfazer", !store.CanUndo, "");

            // Latest change wins: a conta mudada há menos tempo é a fonte
            check("Configs: mais recente = B", store.PickLatest(new[] { A, B, C }) == B, store.PickLatest(new[] { A, B, C }) ?? "nenhuma");

            // Só binds de A para B: o resto de B fica; nuvem e _lastclouded acompanham
            var r = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" }, Reason = "copy" }, "s1", "A");
            check("Configs: copiou binds", r.Outcome == CfgOutcome.Copied && r.Categories.SequenceEqual(new[] { "binds" }), $"{r.Outcome} {string.Join(",", r.Categories)}");
            check("Configs: depois de copiar, dá para desfazer", store.CanUndo, "");
            check("Configs: binds de B = A", Cs2Config.SameText(Get(ud, B, kL), Keys("+jump")), "");
            check("Configs: cópia na nuvem e _lastclouded", Cs2Config.SameText(Get(ud, B, kR), Keys("+jump")) && Cs2Config.SameText(Get(ud, B, kC), Keys("+jump")) && !Get(ud, B, kR).Contains('\r'), "");
            check("Configs: o resto de B ficou", Cs2Config.SameText(Get(ud, B, cL), Convars("2", "5", "1")) && Cs2Config.SameText(Get(ud, B, vL), Video("1280", "960")), "");

            // Backup antes de escrever, com o conteúdo antigo
            var bk = store.Backups().Where(b => b.SteamId == B).ToList();
            check("Configs: backup criado", bk.Count == 1 && bk[0].Files.Contains(kL) && bk[0].Files.Contains(kR), $"{bk.Count}");
            check("Configs: backup tem o antigo", bk.Count == 1 && Cs2Config.SameText(File.ReadAllText(Path.Combine(bk[0].Dir, kL)), Keys("<unbound>")), "");

            // Igual: não escreve nem faz backup
            var same = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" } }, "s1b");
            check("Configs: igual não escreve", same.Outcome == CfgOutcome.Same && store.Backups().Count(b => b.SteamId == B) == 1, same.Outcome.ToString());

            // Só a mira: a sensibilidade de B fica
            store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair" } }, "s2");
            var fb = Cs2Config.Flatten(Get(ud, B, cL), CfgPart.Convars);
            check("Configs: mira copiada, sensibilidade fica", fb["cl_crosshairsize"] == "2" && fb["cl_crosshairgap"] == "-3" && fb["sensitivity"] == "2", $"size {fb["cl_crosshairsize"]} sens {fb["sensitivity"]}");
            check("Configs: ficheiro misturado em CRLF", Get(ud, B, cL).Contains("\r\n"), "");

            // Desfazer a última (s2): volta a mira de B
            var n = store.UndoLast("u1");
            fb = Cs2Config.Flatten(Get(ud, B, cL), CfgPart.Convars);
            check("Configs: desfazer repõe", n == 1 && fb["cl_crosshairsize"] == "5" && fb["cl_crosshairgap"] == "1", $"{n} size {fb["cl_crosshairsize"]}");

            // Restaurar o backup da cópia de binds
            var first = store.Backups().Where(b => b.SteamId == B && b.SyncId == "s1").Single();
            check("Configs: restaurar", store.Restore(first, "u2") && Cs2Config.SameText(Get(ud, B, kL), Keys("<unbound>")), "");

            // O que a app escreveu não conta como mudança no jogo
            store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair", "game" } }, "s3");
            check("Configs: o que a app escreveu não conta como mudança", store.Changed(B) < DateTime.Now.AddMinutes(-30), $"{store.Changed(B)}");

            // CS2 aberto: fica em fila, nada escrito; fecha: aplica
            var cs2 = true;
            store.Cs2Running = () => cs2;
            var q = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "video" } }, "s4");
            check("Configs: com CS2 aberto fica em fila", q.Outcome == CfgOutcome.Queued && Cs2Config.SameText(Get(ud, B, vL), Video("1280", "960")) && store.State.Pending.Count == 1, q.Outcome.ToString());
            check("Configs: fila não corre com CS2 aberto", store.FlushPending("s5").Count == 0 && store.State.Pending.Count == 1, "");
            check("Configs: desfazer recusa com CS2 aberto", store.UndoLast("u3") == -1, "");
            cs2 = false;
            var fl = store.FlushPending("s6");
            check("Configs: fila aplicada ao fechar", fl.Count == 1 && fl[0].Outcome == CfgOutcome.Copied && Cs2Config.SameText(Get(ud, B, vL), Video("1920", "1080")) && store.State.Pending.Count == 0, "");

            // Conta nova sem pasta: cria local, não cria nuvem (sem remotecache.vdf); desfazer apaga o que criou
            var nc = store.Push(new CfgJob { SourceId = A, Target = C, Categories = Cs2Config.Categories.ToList() }, "s7");
            check("Configs: conta nova recebe", nc.Outcome == CfgOutcome.Copied && Cs2Config.SameText(Get(ud, C, kL), Keys("+jump")) && Get(ud, C, vL) != null, nc.Outcome.ToString());
            check("Configs: conta nova sem cópia na nuvem", Get(ud, C, kR) == null && Get(ud, C, cR) == null, "");
            store.UndoLast("u4");
            check("Configs: desfazer apaga o que criou", Get(ud, C, kL) == null && Get(ud, C, vL) == null, "");

            // Comparar, em palavras
            var d = Cs2Config.Diff(store.Read(A), store.Read(C));
            check("Configs: diff com conta vazia", d.Any(x => x.Label == "Sensitivity" && x.A == "1.2" && x.B == "no file"), $"{d.Count}");
            var a2 = new CfgSet(); a2.Files[CfgPart.Convars] = Convars("1.2", "2", "-3"); a2.Files[CfgPart.Keys] = Keys("+jump"); a2.Files[CfgPart.Video] = Video("1920", "1080");
            var b2 = new CfgSet(); b2.Files[CfgPart.Convars] = Convars("2", "2", "-3"); b2.Files[CfgPart.Keys] = Keys("<unbound>"); b2.Files[CfgPart.Video] = Video("2560", "1440");
            var d2 = Cs2Config.Diff(a2, b2);
            check("Configs: diff sensibilidade", d2.Any(x => x.Label == "Sensitivity" && x.A == "1.2" && x.B == "2"), string.Join(" | ", d2.Select(x => $"{x.Label}: {x.A} > {x.B}")));
            check("Configs: diff bind", d2.Any(x => x.Label == "Key MOUSE4" && x.A == "+jump" && x.B == "unbound"), "");
            check("Configs: diff resolução", d2.Any(x => x.Label == "Resolution" && x.A == "1920x1080" && x.B == "2560x1440"), "");
            check("Configs: diff só o que muda", d2.Count == 3, $"{d2.Count}");
            check("Configs: resumo da mira", Cs2Config.CrosshairSummary(a2) == "size 2 · gap -3 · thick ? · green", Cs2Config.CrosshairSummary(a2));

            // O CS2 grava a tecla \ sem escape ("\"): lida tal como está, e a mistura da mira não a estraga
            var bs = "\"config\"\r\n{\r\n\t\"bindings\"\r\n\t{\r\n\t\t\"\\\"\t\t\"slot12\"\r\n\t\t\"e\"\t\t\"+use\"\r\n\t}\r\n}\r\n";
            var bsMap = Cs2Config.Flatten(bs, CfgPart.Keys);
            check("Configs: tecla barra sem escape", bsMap.Count == 2 && bsMap.TryGetValue("\\", out var slot) && slot == "slot12" && bsMap["e"] == "+use", string.Join(",", bsMap.Select(x => x.Key + "=" + x.Value)));
            check("Configs: tecla barra ida e volta", Vcfg.RoundTrips(bs) && Cs2Config.SameText(Vcfg.Write(Vcfg.Parse(bs)), bs), "");
            var cv = Convars("1", "2", "3").Replace("\"viewmodel_fov\"", "\"cl_dir\"\t\t\"C:\\x\"\r\n\t\t\"viewmodel_fov\"");
            var mixed = Cs2Config.MergeConvars(Convars("5", "9", "9"), cv, true, false);
            check("Configs: mistura mantém a barra", mixed != null && mixed.Contains("\"C:\\x\"") && Cs2Config.Flatten(mixed, CfgPart.Convars)["cl_crosshairsize"] == "9", mixed ?? "null");

            // Snapshot: guardar, exportar, importar (ida e volta)
            store.SaveSnapshot("Main", store.Read(A), "A");
            var zip = Path.Combine(root, "main.zip");
            store.ExportSnapshot("Main", zip);
            using (var z = ZipFile.OpenRead(zip))
                check("Configs: zip sem nome da conta", !new StreamReader(z.GetEntry("snapshot.json")!.Open()).ReadToEnd().Contains("\"From\""), "");
            var imported = store.ImportSnapshot(zip);
            var back = store.ReadSnapshot(imported);
            check("Configs: zip ida e volta", imported == "Main (2)" && Cs2Config.SameText(back.Get(CfgPart.Convars), Convars("1.2", "2", "-3")) && Cs2Config.SameText(back.Get(CfgPart.Keys), Keys("+jump")), imported);
            // Um zip com caminhos estranhos: só os ficheiros conhecidos entram
            var evil = Path.Combine(root, "evil.zip");
            using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("../../escape.vcfg").Open())) w.Write(Keys("x"));
                using (var w = new StreamWriter(z.CreateEntry("/abs/escape.vcfg").Open())) w.Write(Keys("x"));
                using (var w = new StreamWriter(z.CreateEntry("..\\..\\escape.vcfg").Open())) w.Write(Keys("x"));
                using (var w = new StreamWriter(z.CreateEntry("keys.vcfg").Open())) w.Write(Keys("+attack"));
            }
            var ev = store.ImportSnapshot(evil);
            // Procura em toda a árvore do teste (raiz, store e snapshots), não só na raiz
            check("Configs: zip estranho só traz o conhecido", Directory.GetFiles(root, "escape.vcfg", SearchOption.AllDirectories).Length == 0 && store.ReadSnapshot(ev).Files.Count == 1, ev);
            var snapJob = store.Push(new CfgJob { Snapshot = "Main", Target = B, Categories = new() { "binds" } }, "s8");
            check("Configs: snapshot aplicado", snapJob.Outcome is CfgOutcome.Copied or CfgOutcome.Same && Cs2Config.SameText(Get(ud, B, kL), Keys("+jump")), snapJob.Outcome.ToString());

            // Backups: só os mais novos ficam
            store.KeepBackups = 2;
            var outs = new List<CfgOutcome>();
            for (var i = 0; i < 4; i++) outs.Add(store.Push(new CfgJob { SourceId = i % 2 == 0 ? null : A, Snapshot = i % 2 == 0 ? ev : null, Target = B, Categories = new() { "binds" } }, "k" + i).Outcome);
            var kept = store.Backups().Where(b => b.SteamId == B).Select(b => b.SyncId).OrderBy(s => s, StringComparer.Ordinal).ToList();
            check("Configs: limite de backups", outs.All(o => o == CfgOutcome.Copied) && kept.SequenceEqual(new[] { "k2", "k3" }), string.Join(",", outs) + " | " + string.Join(",", kept));

            // Modos e "não mexer nesta conta"
            var all = new[] { A, B, C };
            var cats = new[] { "binds", "video" };
            var p1 = Cs2Config.Plan("main", A, null, null, "manual", null, all, new HashSet<string> { C }, cats);
            check("Configs: opt-out respeitado", p1.Count == 1 && p1[0].Target == B && p1[0].SourceId == A, string.Join(",", p1.Select(j => j.Target)));
            check("Configs: conta nova opt-out fica", Cs2Config.Plan("main", A, null, null, "new", C, all, new HashSet<string> { C }, cats).Count == 0, "");
            var p2 = Cs2Config.Plan("previous", A, B, null, "login", C, all, new HashSet<string>(), cats);
            check("Configs: modo anterior", p2.Count == 1 && p2[0].SourceId == B && p2[0].Target == C, "");
            var p3 = Cs2Config.Plan("latest", A, null, B, "closed", null, all, new HashSet<string>(), cats);
            check("Configs: latest ao fechar", p3.Count == 2 && p3.All(j => j.SourceId == B) && !p3.Any(j => j.Target == B), "");
            check("Configs: main não age ao fechar", Cs2Config.Plan("main", A, null, null, "closed", null, all, new HashSet<string>(), cats).Count == 0, "");
            check("Configs: desligado não faz nada", Cs2Config.Plan("off", A, B, B, "manual", null, all, new HashSet<string>(), cats).Count == 0, "");
            check("Configs: login na própria fonte", Cs2Config.Plan("main", A, null, null, "login", A, all, new HashSet<string>(), cats).Count == 0, "");
            // Uma conta com Sync desligado nunca é a fonte (só a conta principal escolhida à mão)
            check("Configs: fonte opt-out não copia", Cs2Config.Plan("previous", A, C, null, "login", A, all, new HashSet<string> { C }, cats).Count == 0
                && Cs2Config.Plan("latest", A, null, C, "closed", null, all, new HashSet<string> { C }, cats).Count == 0, "");
            check("Configs: principal opt-out continua fonte", Cs2Config.Plan("main", C, null, null, "manual", null, all, new HashSet<string> { C }, cats).Count == 2, "");
            // Entrar na conta que estava em uso (a anterior é ela própria): nada a copiar
            check("Configs: login na conta anterior não copia", Cs2Config.Plan("previous", null, A, null, "login", A, all, new HashSet<string>(), cats).Count == 0, "");

            Run2(check, root, links);
        }
        catch (Exception ex) { check("Configs: sem exceções", false, ex.ToString()); }
        finally
        {
            foreach (var l in links) try { Directory.Delete(l); } catch { }   // só a junção; o que está do outro lado fica
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
