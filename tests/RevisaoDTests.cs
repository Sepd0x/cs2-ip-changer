using System.IO;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área D: Configs, Profiles, Settings e o resto): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoDTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão D: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        Configs(check, Src);
        SettingsAndTray(check, Src);
    }

    static void SettingsAndTray(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var xaml = Src("MainWindow.xaml");

        // D8. O aviso ao fechar para a área de notificação não promete proteção
        check("Revisão D8: o aviso do X não diz que a proteção continua", !MainWindow.TrayHintText.Contains("carries on") && !MainWindow.TrayHintText.Contains("Protection") && !MainWindow.TrayHintText.Contains("New IP"), MainWindow.TrayHintText);
        check("Revisão D8: o aviso usa esse texto", Src("Views", "MainWindow.Tray.cs").Contains("_tray?.Balloon(\"CS2 IP Changer is still running\", TrayHintText);"), "");
        check("Revisão D8: o tooltip de Close to the notification area não diz que protege", !xaml.Contains("keeps protecting") && xaml.Contains("ToolTip=\"The X hides the window and the app keeps running in the notification area (next to the clock)."), "");

        // D10. Start with Windows não diz "with protection off"
        check("Revisão D10: Start with Windows diz que a proteção arranca como está em Settings > Protection",
            !xaml.Contains("with protection off (see Protection)") && xaml.Contains("Text=\"Opens in the notification area when you sign in. Protection starts as set in Settings > Protection.\""), "");

        // D11. Open Windows Firewall: o erro não é tapado e o nome das regras serve sempre
        var settings = Src("Views", "MainWindow.Settings.cs");
        var fw = settings[settings.IndexOf("void OpenFirewall_Click")..];
        fw = fw[..fw.IndexOf("\n    }")];
        check("Revisão D11: uma falha a abrir a Firewall para ali", fw.Contains("catch (Exception ex) { Toast(\"Couldn't open the Firewall: \" + ex.Message, Tone.Error); return; }"), "");
        check("Revisão D11: o toast dá o começo de todos os nomes, não só o do kill switch",
            MainWindow.FirewallRulesHint == $"In Outbound Rules, the app's rules start with '{AppIds.RulePrefix}'." && fw.Contains("Toast(FirewallRulesHint, true);") && !fw.Contains("Firewall.RuleName"), "");

        // D12. O interruptor Hide usernames mostra a opção, não o efeito do OBS
        check("Revisão D12: Hide usernames mostra a opção gravada", settings.Contains("OptStreamer.IsChecked = Rec.On || _s.StreamerMode;") && !settings.Contains("OptStreamer.IsChecked = HideLogins;"), "");
        check("Revisão D12: um clique noutra opção não grava Hide usernames", Src("Views", "MainWindow.Accounts.cs").Contains("if (sender == OptStreamer) _s.StreamerMode = OptStreamer.IsChecked == true;"), "");

        // D13. Desligar o Hide usernames do OBS não diz que o programa fechou
        check("Revisão D13: desligar a opção com o OBS aberto diz isso", MainWindow.StreamingLogLine(false, false) == "Hide usernames while OBS or Streamlabs is open: off. Usernames show again.", MainWindow.StreamingLogLine(false, false));
        check("Revisão D13: abrir e fechar o OBS ficam como eram",
            MainWindow.StreamingLogLine(false, true) == "The streaming program closed: usernames show again." && MainWindow.StreamingLogLine(true, true).StartsWith("A streaming program is open"), "");
        check("Revisão D13: o WatchStreaming usa essa frase", Src("Views", "MainWindow.Recording.cs").Contains("Log.Info(StreamingLogLine(open, _s.AutoStreamer));"), "");

        // D16. O aviso de dados chega mesmo com o limite abaixo do que já se gastou
        check("Revisão D16: limite abaixo do gasto avisa", MainWindow.WarnNow(5_000_000_000, 3, null, "2026-11"), "");
        check("Revisão D16: uma vez por mês, e nada sem limite ou abaixo dele",
            !MainWindow.WarnNow(5_000_000_000, 3, "2026-11", "2026-11") && !MainWindow.WarnNow(5_000_000_000, 0, null, "2026-11") && !MainWindow.WarnNow(2_000_000_000, 3, null, "2026-11") && MainWindow.WarnNow(3_000_000_000, 3, "2026-10", "2026-11"), "");
        check("Revisão D16: o CountData usa essa regra", Src("Views", "MainWindow.Tray.cs").Contains("if (WarnNow(_s.DataMonthBytes, _s.DataWarnGb, _s.DataWarnedMonth, _s.DataMonth))"), "");

        // D19. Make the others look like diz que também leva a privacidade
        var ident = xaml[xaml.IndexOf("x:Name=\"ProfIdentBtn\"")..];
        ident = ident[..ident.IndexOf("/>")];
        check("Revisão D19: o tooltip do Preview diz a privacidade", ident.Contains("bio and country (and privacy, if you signed in to it) into a new template"), "");
    }

    const string KeysRel = @"local\cfg\cs2_user_keys_0_slot0.vcfg", ConvRel = @"local\cfg\cs2_user_convars_0_slot0.vcfg";
    static string Keys(string mouse4) => Cs2Config.Crlf($"\"config\"\n{{\n\t\"bindings\"\n\t{{\n\t\t\"MOUSE4\"\t\t\"{mouse4}\"\n\t}}\n}}\n");
    static string Conv(string size) => Cs2Config.Crlf($"\"config\"\n{{\n\t\"convars\"\n\t{{\n\t\t\"sensitivity\"\t\t\"2\"\n\t\t\"cl_crosshairsize\"\t\t\"{size}\"\n\t}}\n}}\n");

    /// <summary>Uma junção (mklink /J não pede admin); false se não deu para a criar.</summary>
    static bool Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        p?.WaitForExit(10000);
        return Directory.Exists(link);
    }

    static void Configs(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var cfgSrc = Src("Views", "MainWindow.Configs.cs");
        const string A = "76561198000000041", B = "76561198000000042", C = "76561198000000043";
        var tmp = Path.Combine(Path.GetTempPath(), "srt-revD-" + Guid.NewGuid().ToString("N")[..8]);
        var ud = Path.Combine(tmp, "Steam", "userdata");
        string P(string id, string rel) => Path.Combine(ud, Cs2Config.Id32(id), "730", rel);
        void Put(string id, string rel, string text) { Directory.CreateDirectory(Path.GetDirectoryName(P(id, rel))!); File.WriteAllText(P(id, rel), text); }
        string Get(string id, string rel) => File.Exists(P(id, rel)) ? File.ReadAllText(P(id, rel)) : null;
        string Size(string id) => Get(id, ConvRel) is string t ? Cs2Config.Flatten(t, CfgPart.Convars).GetValueOrDefault("cl_crosshairsize") : null;
        var link = P(C, @"local\cfg");
        try
        {
            Put(A, KeysRel, Keys("+jump"));
            Put(A, ConvRel, Conv("2"));
            Put(B, KeysRel, Keys("+old"));
            Put(B, ConvRel, Conv("5"));
            var store = new Cs2ConfigStore(ud, Path.Combine(tmp, "configs")) { Cs2Running = () => false };

            // D1. A pasta do CS2 de uma conta é uma junção: não é "No CS2 config yet ... (or gets the sync)"
            var outside = Path.Combine(tmp, "fora");
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(P(C, "local"));
            if (Junction(link, outside))
            {
                check("Revisão D1: o store sabe que a pasta do CS2 da conta é um link", store.IsLinked(C) && !store.IsLinked(A), "");
                var (xh, changed) = MainWindow.CfgRowText(store.Read(C), store.IsLinked(C), null);
                check("Revisão D1: a linha da conta diz que é um link e não promete a sync", xh == "CS2 folder is a link" && !changed.Contains("gets the sync"), $"{xh} · {changed}");
            }
            else Console.WriteLine("SALTO Revisão D1: não deu para criar a junção");
            check("Revisão D1: sem config (e sem link) a linha fica como era", MainWindow.CfgRowText(new CfgSet(), false, null) == ("No CS2 config yet", "Opens with CS2's defaults (or gets the sync)"), "");
            var loc = Enumerable.Range(1, 6).Select(i => new LocalAccount("7656119800000010" + i, "l" + i, "N" + i, false, 0)).ToList();
            var items = MainWindow.AccountItems(loc, new(), null, false, out _);
            var rows = items.Select((a, i) => new CfgAccountRow { Acc = a, GroupKey = CfgAccountRow.NoConfig, Linked = i >= 3 }).ToList();
            var shown = MainWindow.CfgDisplay(rows, "", new HashSet<string>(), null);
            check("Revisão D1: uma conta com a pasta num link nunca entra no grupo \"no CS2 config yet\"",
                shown.OfType<CfgGroupRow>().SingleOrDefault() is CfgGroupRow g && g.Rows.Count == 3 && g.Rows.All(r => !r.Linked) && shown.OfType<CfgAccountRow>().Count(r => r.Linked) == 3, "");
            check("Revisão D1: o texto do link fala da pasta do Steam ou do CS2", SafeFile.SteamLinked.StartsWith("a Steam or CS2 config folder goes through a link or junction"), SafeFile.SteamLinked);
            check("Revisão D1: guardar um snapshot de uma conta num link diz porquê, não \"open CS2 on it once\"",
                cfgSrc.Contains("if (Cfg.IsLinked(from.SteamId)) { Toast(\"That account's CS2 folder is a link or junction, so the app doesn't read it.\", false); return; }"), "");

            // D2. Undo last sync não é um interruptor: um segundo Undo não refaz a sync
            store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair" }, Reason = CfgTrigger.Manual }, "s1");
            var copied = Size(B);
            var n1 = store.UndoLast("u1");
            check("Revisão D2: o primeiro Undo repõe a mira", copied == "2" && n1 == 1 && Size(B) == "5", $"{copied} {n1} {Size(B)}");
            check("Revisão D2: depois do Undo não há nada para desfazer (o botão fica cinzento)", !store.CanUndo, "");
            var n2 = store.UndoLast("u1b");
            check("Revisão D2: um segundo Undo não volta a pôr a sync", n2 == 0 && Size(B) == "5", $"{n2} {Size(B)}");
            // Parte 2: um ficheiro que o Restore escreve e que não existia antes vai no backup como criado, e o Undo apaga-o
            store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" }, Reason = CfgTrigger.Copy }, "s2");
            var bk = store.Backups().First(b => b.SteamId == B && b.SyncId == "s2");
            File.Delete(P(B, KeysRel));
            var restored = store.Restore(bk, "r1") && Get(B, KeysRel) is string k && Cs2Config.SameText(k, Keys("+old"));
            store.UndoLast("u2");
            check("Revisão D2: o Undo de um Restore apaga o ficheiro que o Restore criou", restored && Get(B, KeysRel) == null, $"{restored} {Get(B, KeysRel) != null}");

            // D18. Copiar uma parte que a origem não tem não é "already has the same config"
            const string onlyConv = "76561198000000044";   // uma conta só com o ficheiro das convars
            Put(onlyConv, ConvRel, Conv("3"));
            var r1 = store.Push(new CfgJob { SourceId = onlyConv, Target = A, Categories = new() { "binds" }, Reason = CfgTrigger.Copy }, "s3");
            check("Revisão D18: só binds de uma origem sem binds: não mudou, e diz porquê", r1.Outcome == CfgOutcome.Skipped && (r1.Detail ?? "").Contains("binds"), $"{r1.Outcome} {r1.Detail}");
            var r2 = store.Push(new CfgJob { SourceId = onlyConv, Target = A, Categories = new() { "binds", "crosshair" }, Reason = CfgTrigger.Copy }, "s4");
            check("Revisão D18: com uma parte que a origem tem, essa copia na mesma", r2.Outcome == CfgOutcome.Copied && r2.Categories.SequenceEqual(new[] { "crosshair" }) && Size(A) == "3", $"{r2.Outcome} {Size(A)}");
        }
        finally
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }   // só a junção, não o que está do outro lado
            try { Directory.Delete(tmp, true); } catch { }
        }

        // D3. Undo com todas as contas a falhar
        check("Revisão D3: nenhuma conta voltou: não diz \"Undone on 0\"", MainWindow.UndoToast(0, new[] { "atlas" }) == "Nothing was undone: atlas stayed as they were. Restore them from Backups.", MainWindow.UndoToast(0, new[] { "atlas" }));
        check("Revisão D3: com uma conta de volta fica como era", MainWindow.UndoToast(1, new[] { "atlas", "orbit" }) == "Undone on 1 account. atlas, orbit stayed as they were: restore them from Backups.", MainWindow.UndoToast(1, new[] { "atlas", "orbit" }));

        // D4. Nada em What to copy: o Sync now e a linha da Sync dizem isso
        var syncNow = cfgSrc[cfgSrc.IndexOf("void CfgSyncNow_Click")..cfgSrc.IndexOf("CfgPlan(CfgTrigger.Manual")];
        check("Revisão D4: Sync now sem nada marcado manda marcar em Copy > What to copy", syncNow.Contains("if (_s.ConfigCategories.Count == 0) { Toast(\"Tick something in Copy > What to copy first: the sync copies what is ticked there.\", false); return; }"), "");
        check("Revisão D4: a linha da Sync diz que nada está marcado", MainWindow.CfgSyncLine(CfgMode.Main, false, "6 Nov 10:00 · Sync now: 1 copied.") == "Sync is on, but nothing is ticked in Copy > What to copy.", "");
        check("Revisão D4: com algo marcado, a linha da Sync fica como era",
            MainWindow.CfgSyncLine(CfgMode.Main, true, null) == "No sync yet." && MainWindow.CfgSyncLine(CfgMode.Off, false, null) == "Sync is off. To copy once, use Copy." && MainWindow.CfgSyncLine(CfgMode.Latest, true, "x") == "x", "");
        check("Revisão D4: mudar o What to copy refaz a linha da Sync", cfgSrc.Contains("SaveSettings();\n        ShowCfgResult(CfgSyncLine(_s.ConfigMode, _s.ConfigCategories.Count > 0, Cfg.State.LastResult));"), "");

        // D5. Sem "on the right" nas strings do C# (o teste 8 da organização só lia o XAML)
        var lit = new Regex(@"""(?:[^""\\\n]|\\.)*""");
        var place = new Regex(@"\bon the (right|left)\b");
        var where = Directory.EnumerateFiles(Path.Combine(SecurityTests.Repo(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"[\\/](bin|obj|Privado)[\\/]"))
            .SelectMany(f => lit.Matches(File.ReadAllText(f)).Select(m => m.Value).Where(s => place.IsMatch(s)).Select(s => Path.GetFileName(f) + ": " + s)).ToList();
        check("Revisão D5: as strings do C# não apontam com on the right / on the left", where.Count == 0, string.Join("; ", where));
        check("Revisão D5: o Copy aponta para Configs > Backups, o mesmo texto no código e no XAML",
            cfgSrc.Contains("\"Each account gets a backup first (Configs > Backups).\"") && Src("MainWindow.xaml").Contains("Text=\"Each account gets a backup first (Configs > Backups).\""), "");

        // D6. Compare escolhe B pela regra única das mesmas definições
        check("Revisão D6: Compare já não escolhe B pela comparação de texto", !cfgSrc.Contains("!Cs2Config.SameText(Cfg.Read(i.SteamId)") && cfgSrc.Contains("!Cs2Config.SameSettings(CfgPart.Convars, Cfg.Read(i.SteamId).Get(CfgPart.Convars), mainConv)"), "");
        check("Revisão D6: \"2.000000\" e \"2\" são as mesmas definições", Cs2Config.SameSettings(CfgPart.Convars, Conv("2.000000"), Conv("2")), "");

        // D7. Use as main config diz o que os When ligados fazem
        check("Revisão D7: com os dois When ligados fica como era", MainWindow.MainConfigToast("nightfall", true, true) == "nightfall is the main config now. New accounts and logins through the app get it (Configs page).", "");
        check("Revisão D7: só um When ligado diz só esse", MainWindow.MainConfigToast("x", true, false) == "x is the main config now. New accounts get it (Configs page)." && MainWindow.MainConfigToast("x", false, true) == "x is the main config now. Logins through the app get it (Configs page).", "");
        check("Revisão D7: sem When ligado não promete cópias", !MainWindow.MainConfigToast("x", false, false).Contains("get it") && MainWindow.MainConfigToast("x", false, false).Contains("Configs > Sync > When"), MainWindow.MainConfigToast("x", false, false));

        // D20. O snapshot chama à categoria o mesmo que o resto da app
        CfgSnapshotRow Snap(params string[] parts) { var s = new CfgSnapshot { Name = "x", Created = DateTime.Now }; s.Parts.AddRange(parts); return new CfgSnapshotRow(s); }
        check("Revisão D20: snapshot só com o HUD diz \"HUD, radar and audio\"", Snap("Machine").What == "HUD, radar and audio", Snap("Machine").What);
        check("Revisão D20: com outras partes, o HUD não parece três itens", Snap("Keys", "Machine").What == "binds, HUD/radar/audio", Snap("Keys", "Machine").What);

        // N1. O Copy manda para o grupo certo
        check("Revisão N1: Copy sem nada marcado manda para Copy > What to copy", !cfgSrc.Contains("Sync > What to copy") && cfgSrc.Contains("Toast(\"Tick something in Copy > What to copy.\", false)"), "");
    }
}
