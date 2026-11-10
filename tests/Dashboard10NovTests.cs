using System.IO;
using SRT;
using SRT.Core;

// 10 nov.: a contagem da dashboard com os dados reais (sessão 9, publish/DASHBOARD-2026-11-10.md). Cada correção leva aqui o seu
// teste, que falhava antes. P1 a P8 são os da auditoria de código (ServerRouteToolCodex/lab/reports/dashboard-audit-2026-11-10.md).
static class Dashboard10NovTests
{
    public static void Run(Action<string, bool, string> check)
    {
        JoinSoNumServidor(check);
        IphoneConta(check);
        PartidaEmCasa(check);
        MinutosDoSinal(check);
        PcDoDono(check);
    }

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    // P1: "join" contava também o menu do CS2 (Connected to 'loopback'), por isso "entraram num servidor" era quase "abriram o CS2"
    static void JoinSoNumServidor(Action<string, bool, string> check)
    {
        check("Dashboard 10 nov. (P1): o menu do CS2 e um mapa local não são um join",
            !Cs2Console.IsJoin("CL:  Connected to 'loopback:0'") && !Cs2Console.IsJoin("Connected to 'loopback'")
            && !Cs2Console.IsJoin("CL:  Connected to 'de_dust2'") && !Cs2Console.IsJoin("CL:  Disconnected from '1.2.3.4:27015'") && !Cs2Console.IsJoin(null), "");
        check("Dashboard 10 nov. (P1): um servidor da comunidade e um da Valve são um join",
            Cs2Console.IsJoin("CL:  Connected to '38.3.226.237:27019'") && Cs2Console.IsJoin("CL:  Connected to '=[A:1:4005189648:51168]'"), "");
        var kick = Src("Views", "MainWindow.Kick.cs");
        check("Dashboard 10 nov. (P1): a app conta o join só com IsJoin",
            kick.Contains("if (Cs2Console.IsJoin(line)) Telemetry.Count(\"join\");") && !kick.Contains("                Telemetry.Count(\"join\");"), "");
    }

    // P2: no iPhone só se contava ip_changed; o método, o mesmo IP e a rede que não volta ficavam de fora (a taxa de IP novo dava 100 %)
    static void IphoneConta(Action<string, bool, string> check)
    {
        var novo = MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.BackNew, true, TimeSpan.FromSeconds(25));
        check("Dashboard 10 nov. (P2): um IP novo no iPhone conta o método (modo de avião), o tempo e 1 tentativa, como no Android",
            novo.SequenceEqual(new[] { "chg_air", "chgt_20_40", "chga_1" }), string.Join(",", novo));
        check("Dashboard 10 nov. (P2): o mesmo IP conta ip_same; a rede que não volta depois da queda conta ip_no_network",
            MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.BackSame, true, TimeSpan.FromSeconds(30)).SequenceEqual(new[] { "ip_same" })
            && MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.TimedOut, true, TimeSpan.FromMinutes(5)).SequenceEqual(new[] { "ip_no_network" }), "");
        check("Dashboard 10 nov. (P2): sem ver o modo de avião, ou ainda a meio, não conta nada",
            MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.TimedOut, false, TimeSpan.FromSeconds(91)).Count == 0
            && MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.Off, true, TimeSpan.FromSeconds(10)).Count == 0
            && MainWindow.IphoneChangeCounts(MainWindow.IphoneChange.Measuring, true, TimeSpan.FromSeconds(10)).Count == 0, "");
        check("Dashboard 10 nov. (P2): o Change IP do iPhone conta a cada passo",
            Src("Views", "MainWindow.Iphone.cs").Contains("foreach (var n in IphoneChangeCounts(step, sawDrop, sw.Elapsed)) Telemetry.Count(n);"), "");
    }

    // P8: as partidas jogadas pela rede de casa entravam como as do telemóvel, com o lag e o ping (46 de 49 partidas eram do PC do dono)
    static void PartidaEmCasa(Action<string, bool, string> check)
    {
        var lag = new NetReport { PingP50 = 62, JitterUp = 23, JitterDown = 4, UpLate = 58.2, Dropped = 3 };
        var casa = Telemetry.MatchCounts(lag, valve: true, home: true);
        check("Dashboard 10 nov. (P8): uma partida na rede de casa conta a partida e m_home, sem lag nem bandas",
            casa.SequenceEqual(new[] { "m_valve", "m_home" }), string.Join(",", casa));
        var tel = Telemetry.MatchCounts(lag, valve: false);
        check("Dashboard 10 nov. (P8): pelo telemóvel fica como antes", tel[0] == "m_comm" && tel.Contains("m_comm_lag") && !tel.Contains("m_home"), string.Join(",", tel));
        var m = TelemetryTests.Contract().GetProperty("bands").GetProperty("m").EnumerateArray().Select(x => x.GetString()).ToList();
        check("Dashboard 10 nov. (P8): o contrato do Worker aceita m_home", m.Contains("home"), string.Join(",", m));
        check("Dashboard 10 nov. (P8): a app diz se a partida foi em casa", Src("Views", "MainWindow.Kick.cs").Contains("Telemetry.Match(r, _kicks.OnValve, home);"), "");
    }

    // P7: o primeiro sinal ao vivo (30 s depois de abrir) somava logo 5 minutos de CS2 ou de proteção
    static void MinutosDoSinal(Action<string, bool, string> check)
    {
        check("Dashboard 10 nov. (P7): 5 minutos só quando o sinal anterior também o viu",
            Telemetry.LiveMinutes(true, true) == 5 && Telemetry.LiveMinutes(false, true) == 0 && Telemetry.LiveMinutes(true, false) == 0 && Telemetry.LiveMinutes(false, false) == 0, "");
        var t = Src("Core", "Telemetry.cs");
        check("Dashboard 10 nov. (P7): min_cs2 e min_prot passam pelo LiveMinutes",
            t.Contains("LiveMinutes(_cs2Was, cs2)") && t.Contains("LiveMinutes(_protWas, prot)") && !t.Contains("if (cs2) Count(\"min_cs2\", 5);"), "");
    }

    // P4: o segundo PC do dono, com uma release instalada, contava como um utilizador real
    static void PcDoDono(Action<string, bool, string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "srt-devpc-" + Guid.NewGuid().ToString("N"));
        var marker = Path.Combine(dir, "CS2 IP Changer", "dev-pc");
        try
        {
            var antes = Telemetry.DevPc(marker, devBuild: false);
            var dev = Telemetry.DevPc(marker, devBuild: true);
            var depois = Telemetry.DevPc(marker, devBuild: false);
            check("Dashboard 10 nov. (P4): a release só é do dono depois de a build de desenvolvimento correr nesse utilizador",
                !antes && dev && depois && File.Exists(marker) && File.ReadAllText(marker) == "", $"{antes} {dev} {depois}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        check("Dashboard 10 nov. (P4): com a marca, a release fica Mine (fora dos números de toda a gente)",
            Src("Core", "Telemetry.cs").Contains("if (DevPc(DevPcFile, Dev) && !_st.Mine) { _st.Mine = true; Save(); }"), "");
    }
}
