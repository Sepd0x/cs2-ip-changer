using System.IO;
using System.Text.RegularExpressions;
using SRT.Core;

// Auditoria de 4 nov. 2026 (equipa telemetria): quando a app manda o relatório, a melhor ligação, as regiões em códigos da Valve,
// os limites abaixo dos do servidor e o corpo que o teste ponta a ponta do Worker usa (worker/stats/test/app-report.json).
static class TelemetryTests
{
    /// <summary>Beta.12: o contrato da telemetria (worker/stats/contract.json), o mesmo ficheiro que o Worker lê.</summary>
    public static System.Text.Json.JsonElement Contract() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(SecurityTests.Repo(), "worker", "stats", "contract.json"))).RootElement;

    static List<string> Names(System.Text.Json.JsonElement e) => e.EnumerateArray().Select(x => x.GetString()).ToList();

    /// <summary>Beta.12 (untangle B4): o que a app manda cabe no contrato, sem cópias à mão do lado da app.</summary>
    static void ContractChecks(Action<string, bool, string> check)
    {
        var c = Contract();
        var bands = c.GetProperty("bands");
        List<string> Band(string fam) => Names(bands.GetProperty(fam));
        bool All<T>(string fam, IEnumerable<T> inputs, Func<T, string> f) => inputs.Select(f).All(Band(fam).Contains);
        var ints = Enumerable.Range(-5, 400).ToList();
        var secs = Enumerable.Range(0, 400).Select(s => TimeSpan.FromSeconds(s * 30)).ToList();
        check("Contrato: as bandas de ping, jitter, perda e atraso estão no contrato",
            All("ping", ints, Telemetry.PingBand) && All("jit", ints, Telemetry.JitterBand) && All("loss", ints, Telemetry.LossBand)
            && All("late", ints.Select(i => i / 10.0), Telemetry.LateBand), "");
        check("Contrato: as bandas de tempo estão no contrato",
            All("sess", secs, Telemetry.SessionBand) && All("st", secs.Select(s => s / 60), Telemetry.StartupBand) && All("tp", secs, Telemetry.ProtectedBand)
            && All("chgt", secs.Select(s => s / 30), Telemetry.ChangeBand), "");
        check("Contrato: tentativas, quedas e dados móveis estão no contrato",
            All("chga", ints, Telemetry.AttemptsBand) && All("drop", ints, Telemetry.DropsBand)
            && ints.Select(i => Telemetry.DataBand(i * 10_000_000L)).All(Names(c.GetProperty("data")).Contains), "");
        check("Contrato: as verificações (chk_) são as do contrato", Telemetry.CheckKeys.Values.OrderBy(k => k).SequenceEqual(Names(c.GetProperty("checks")).OrderBy(k => k)), "");
        check("Contrato: as marcas do telemóvel estão no contrato",
            new[] { "Samsung", "redmi", "HMD", "Fairphone", "google" }.Select(Telemetry.BrandOf).All(Names(c.GetProperty("brands")).Contains), "");
        check("Contrato: as definições contadas são as do contrato, todas on/off ou texto curto da app",
            Telemetry.SettingNames.SequenceEqual(Names(c.GetProperty("settings")))
            && Telemetry.SettingNames.All(n => typeof(SRT.Settings).GetProperty(n)?.PropertyType is Type t && (t == typeof(bool) || t == typeof(string))), "");
        var lim = c.GetProperty("limits");
        check("Contrato: os minutos e o intervalo dos relatórios são os do contrato",
            Telemetry.MaxMinutes == lim.GetProperty("maxMinutes").GetInt32() && Telemetry.Gap == TimeSpan.FromMinutes(lim.GetProperty("reportGapMinutes").GetInt32()), "");
        check("Contrato: a app manda abaixo dos limites do servidor",
            Telemetry.MaxClicks <= lim.GetProperty("clicks").GetInt32() && Telemetry.MaxCounts <= lim.GetProperty("counts").GetInt32()
            && Telemetry.MaxErrors <= lim.GetProperty("errors").GetInt32() && Telemetry.MaxHeat <= lim.GetProperty("heat").GetInt32()
            && Telemetry.MaxBody <= lim.GetProperty("bodyBytes").GetInt32(), "");
    }

    public static void Run(Action<string, bool, string> check)
    {
        ContractChecks(check);
        var t = new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc);

        // D1: o relatório periódico (de 30 em 30 min com algo novo), nunca dentro dos 10 min do servidor
        check("Telemetria D1: 30 min depois, com minutos novos, manda", Telemetry.Periodic(t.AddMinutes(-30), t, 29.5, 0, 0), "");
        check("Telemetria D1: 29 min depois ainda não", !Telemetry.Periodic(t.AddMinutes(-29), t, 29, 3, 1), "");
        check("Telemetria D1: 2 h depois sem nada novo não manda", !Telemetry.Periodic(t.AddHours(-2), t, 0.3, 0, 0), "");
        check("Telemetria D1: num dia UTC novo manda (passados 10 min)", Telemetry.Periodic(new DateTime(2026, 11, 3, 23, 45, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 1, 0, DateTimeKind.Utc), 0, 0, 0), "");
        check("Telemetria D1: dia novo mas a menos de 10 min do último espera", !Telemetry.Periodic(new DateTime(2026, 11, 3, 23, 55, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 1, 0, DateTimeKind.Utc), 6, 0, 0), "");
        check("Telemetria D1: nunca enviado manda", Telemetry.Periodic(default, t, 0, 0, 0), "");
        check("Telemetria D1: o intervalo fica entre 30 e 60 min e acima dos 10 do servidor", Telemetry.Every >= TimeSpan.FromMinutes(30) && Telemetry.Every <= TimeSpan.FromMinutes(60) && Telemetry.Gap == TimeSpan.FromMinutes(10), "");

        // D5: ao fechar, suspender ou terminar sessão: normal depois dos 10 min, marcado exit dentro deles, nada sem novidades
        check("Telemetria D5: fechar 5 min depois do último manda um relatório de saída", Telemetry.AtClose(t.AddMinutes(-5), t, 4.6, 2, 0) == (true, true), "");
        check("Telemetria D5: fechar 12 min depois manda um relatório normal", Telemetry.AtClose(t.AddMinutes(-12), t, 11, 0, 0) == (true, false), "");
        check("Telemetria D5: sem nada novo não manda", Telemetry.AtClose(t.AddMinutes(-3), t, 0.2, 0, 0) == (false, false), "");
        check("Telemetria D5: SendAtClose continua a ser a regra normal dos 10 min", Telemetry.SendAtClose(t.AddMinutes(-11), t, 5, 0, 0) && !Telemetry.SendAtClose(t.AddMinutes(-9), t, 5, 0, 0), "");

        // D1: o código ouve a suspensão e o fim de sessão do Windows, e o tempo a dormir não conta como minutos
        var src = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Core", "Telemetry.cs"));
        check("Telemetria D1: ouve PowerModeChanged (Suspend e Resume) e SessionEnding",
            src.Contains("SystemEvents.PowerModeChanged") && src.Contains("PowerModes.Suspend") && src.Contains("PowerModes.Resume") && src.Contains("SystemEvents.SessionEnding"), "");
        check("Telemetria D1: a build de desenvolvimento já não tem cadência à parte", !src.Contains("_devLive"), "");

        // D3: a melhor ligação vista
        check("Telemetria D3: usb ganha a none e a hotspot", Telemetry.BestLink("none", "usb") == "usb" && Telemetry.BestLink("usb", "hotspot") == "usb" && Telemetry.BestLink("hotspot", "none") == "hotspot", "");
        check("Telemetria D3: sem nada visto fica a ligação de agora; um valor estranho nunca ganha", Telemetry.BestLink(null, "none") == "none" && Telemetry.BestLink("other", "wifi6") == "other", "");

        // D4: as regiões vão como códigos da Valve
        var fra = new RegionInfo("frankfurt-germany", "Frankfurt", "", "Germany", "Europe", new[] { "dfra", "fra" }, new[] { "155.133.226.1" });
        var mad = new RegionInfo("madrid-spain", "Madrid", "", "Spain", "Europe", new[] { "mad" }, new[] { "155.133.246.1" });
        var codes = Telemetry.RegionCodes(new[] { fra, mad }, new[] { "frankfurt-germany", "madrid-spain", "nowhere", null });
        check("Telemetria D4: Frankfurt vai como fra (o código mais curto) e Madrid como mad", codes.SequenceEqual(new[] { "fra", "mad" }), string.Join(",", codes));
        check("Telemetria D4: os códigos passam no filtro do servidor (index.js REGION)", codes.All(c => Regex.IsMatch(c, "^[a-z]{3,4}[0-9]?$")), "");
        check("Telemetria D4: sem a lista de regiões não manda nada", Telemetry.RegionCodes(null, new[] { "frankfurt-germany" }).Count == 0 && Telemetry.RegionCodes(new[] { fra }, null).Count == 0, "");

        // D6: tamanhos, regiões e definições só num dia novo ou quando mudam
        check("Telemetria D6: os extras vão num dia novo ou quando mudam", Telemetry.ExtrasDue("2026-11-03", "a", "2026-11-04", "a") && Telemetry.ExtrasDue("2026-11-04", "a", "2026-11-04", "b") && !Telemetry.ExtrasDue("2026-11-04", "a", "2026-11-04", "a"), "");
        // Revisão: depois de Delete my stats o ID novo leva os extras (e a marca idk) no mesmo dia; a ordem das listas não conta
        var bandsA = new Dictionary<string, int> { ["servers"] = 6, ["accounts"] = 2 };
        var bandsB = new Dictionary<string, int> { ["accounts"] = 2, ["servers"] = 6 };
        var sigOld = Telemetry.ExtrasSig("a".PadRight(32, '0'), bandsA, new[] { "fra", "mad" }, new[] { "Mode" }, "1", 2, null);
        var sigNew = Telemetry.ExtrasSig("b".PadRight(32, '0'), bandsA, new[] { "fra", "mad" }, new[] { "Mode" }, "1", 2, null);
        var sigSame = Telemetry.ExtrasSig("a".PadRight(32, '0'), bandsB, new[] { "mad", "fra" }, new[] { "Mode" }, "1", 2, null);
        check("Telemetria D6 (revisão): um ID novo no mesmo dia volta a mandar os extras", Telemetry.ExtrasDue("2026-11-04", sigOld, "2026-11-04", sigNew), "");
        check("Telemetria D6 (revisão): as mesmas listas noutra ordem não mandam outra vez", !Telemetry.ExtrasDue("2026-11-04", sigOld, "2026-11-04", sigSame), sigOld + " / " + sigSame);
        check("Telemetria D6 (revisão): uma banda que muda manda outra vez", Telemetry.ExtrasDue("2026-11-04", sigOld, "2026-11-04",
            Telemetry.ExtrasSig("a".PadRight(32, '0'), new Dictionary<string, int> { ["servers"] = 21, ["accounts"] = 2 }, new[] { "fra", "mad" }, new[] { "Mode" }, "1", 2, null)), "");
        var forgetSrc = src[src.IndexOf("public static async Task<bool> Forget()", StringComparison.Ordinal)..];
        forgetSrc = forgetSrc[..forgetSrc.IndexOf("return true;", StringComparison.Ordinal)];
        check("Telemetria D6 (revisão): Delete my stats também esquece os extras e a ligação do ID antigo",
            forgetSrc.Contains("_st.ExtrasDay = null") && forgetSrc.Contains("_st.ExtrasSig = null") && forgetSrc.Contains("_st.BestLink = null"), "");

        // D12: os limites da app ficam abaixo dos do servidor (150 botões, 100 contadores, 300 células, 16 KB)
        check("Telemetria D12: limites abaixo do servidor", Telemetry.MaxClicks < 150 && Telemetry.MaxCounts < 100 && Telemetry.MaxHeat < 300 && Telemetry.MaxErrors <= 30 && Telemetry.MaxBody < 16 * 1024, "");
        var big = Full();
        for (var i = 0; i < Telemetry.MaxHeat; i++) big.Heat[$"connection|c|{i % 24},{i / 24 % 16}"] = 1000 + i;
        for (var i = 0; i < Telemetry.MaxClicks; i++) big.Clicks[$"SomeLongButtonNameForTheTest{i}_Click"] = 99999;
        var fit = Telemetry.Fit(big, "chk_egress", "accounts");
        check("Telemetria D12: um relatório pesado cabe no limite e guarda os estados", System.Text.Encoding.UTF8.GetByteCount(fit) <= Telemetry.MaxBody && big.Counts.ContainsKey("chk_egress") && big.Counts.ContainsKey("accounts"),
            System.Text.Encoding.UTF8.GetByteCount(fit) + " B");
        check("Telemetria D12: corta primeiro o calor", big.Heat.Count < Telemetry.MaxHeat, big.Heat.Count + " células");

        // O corpo: sem nulos, exit só quando é saída
        var r = Full();
        var body = Telemetry.Body(r);
        check("Telemetria: o corpo normal não leva exit nem nulos", !body.Contains("\"exit\"") && !body.Contains("null"), "");
        r.Exit = true;
        check("Telemetria: o relatório de saída leva exit:true", Telemetry.Body(r).Contains("\"exit\":true"), "");

        // K3: o mesmo corpo que o Worker recebe no teste ponta a ponta (worker/stats/test/app-report.json)
        var fixture = Path.Combine(SecurityTests.Repo(), "worker", "stats", "test", "app-report.json");
        var full = Telemetry.Body(Full());
        if (Environment.GetEnvironmentVariable("SRT_WRITE_FIXTURE") == "1") File.WriteAllText(fixture, full + "\n");
        var same = File.Exists(fixture) && File.ReadAllText(fixture).Trim() == full;
        check("Telemetria K3: o corpo da app é o do teste do Worker (SRT_WRITE_FIXTURE=1 para o refazer)", same, same ? "" : "worker/stats/test/app-report.json desatualizado");
    }

    /// <summary>Um relatório com todos os campos que a app recolhe, com os nomes reais dos contadores e cliques.</summary>
    static Telemetry.Report Full()
    {
        var counts = new Dictionary<string, int>
        {
            ["chk_egress"] = 1, ["chk_firewall"] = 1, ["servers"] = Telemetry.Band(6), ["accounts"] = Telemetry.Band(3), ["flags"] = Telemetry.Band(0), ["open_with"] = Telemetry.Band(1),
            ["open_with_admin"] = Telemetry.Band(1), ["regions_blocked"] = Telemetry.Band(2),
        };
        foreach (var n in new[] { "first_start", "app_start", "start_hand", "unclean_exit", "ip_changed", "ip_same", "ip_no_network", "ip_home_back", "kick_seen", "vpn_seen", "ban_seen",
            "log_error", "crash", "st_1_2", "tp_10_30", "sess_15_30", "drop_1", "scl_connection_50", "fs_servers", "rej_try", "rej_back", "rej_wait", "cable_hint", "cs2_start", "join",
            "m_valve", "m_valve_lag", "ping_30_50", "jit_5_10", "jitd_lt5", "late_lt1", "loss_0", "chg_air", "chgt_5_10", "chga_1", "chgq_good", "chg_retry",
            "upd_seen", "upd_accepted", "upd_done", "updfrom_0_1_0_beta_9", "phone_setup_tetheroff", "phone_setup_done", "leak_ok", "leak_bad" })
            counts[n] = 1;
        counts["min_cs2"] = 25; counts["min_prot"] = 30;
        return new Telemetry.Report
        {
            Id = "0123456789abcdef0123456789abcdef", App = "0.1.0-beta.10", Os = "10.0.26200.0", Mode = "cs2open", Link = "usb", Minutes = 31, Dev = false, Install = true,
            Clicks = new() { ["ChangeIp_Click"] = 4, ["Rejoin_Click"] = 1, ["Hotkey_Rejoin"] = 2, ["Tray_open"] = 1 },
            Counts = counts,
            Errors = new() { new() { Type = "IOException", At = "Store.Save", N = 2 }, new() { Type = "Fatal_NullReferenceException", At = "MainWindow.QueryServers", N = 1 } },
            // beta.13: the cells are in the page (Telemetry.PageCell, scroll included); beta.12 and before sent "1,4" (Beta9Tests)
            Heat = new() { ["connection|c|s1,5"] = 3, ["connection|d|s12,9"] = 1, ["connection|r|s1,5"] = 1, ["accounts|c|s6,30"] = 2 },
            Screens = new() { ["connection"] = 20, ["servers"] = 5 },
            Paths = new() { ["connection>servers"] = 2 },
            Age = Telemetry.AgeBand(2), Active7 = 3, Settings = new() { "CloseToTray", "Mode" }, Regions = new() { "fra", "mad" }, Data = Telemetry.DataBand(600_000_000),
        };
    }
}
