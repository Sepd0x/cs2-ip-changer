using System.IO;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 7 nov.: o que o dono trouxe depois de usar a beta.12, parte da app (tempo dos jogadores, miras em Configs, o zip de diagnóstico).
// Cada correção leva aqui o seu teste, que falhava antes.
static class Beta13AppTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        check("Beta13 app: o repositório encontra-se", Directory.Exists(Path.Combine(repo, "src")), repo);
        TempoDosJogadores(check);
        Miras(check);
        ZipDeDiagnostico(check);
    }

    // ------------------------------------------------------------ 3. O zip de diagnóstico de 7 nov.

    static void ZipDeDiagnostico(Action<string, bool, string> check)
    {
        // registo.log 2827 (00:16:48): "Servers: asking a server failed (InvalidDataException: Invalid A2S response)". O Exchange só aceita
        // 0x41 (challenge) ou 0x49 (info); depois de responder a um challenge, um segundo 0x41 (challenge novo, ou o primeiro em duplicado)
        // era tomado como a resposta e dava esta exceção. Um servidor que pede o challenge duas vezes tem de responder.
        {
            using var srv = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            var port = ((System.Net.IPEndPoint)srv.Client.LocalEndPoint!).Port;
            byte[] Str(string t) => System.Text.Encoding.UTF8.GetBytes(t + "\0");
            byte[] FF = { 0xFF, 0xFF, 0xFF, 0xFF };
            var info = FF.Concat(new byte[] { 0x49, 17 }).Concat(Str("Dois challenges")).Concat(Str("de_mirage")).Concat(Str("csgo")).Concat(Str("CS2")).Concat(new byte[] { 0xDA, 0x02, 0, 10, 0 }).ToArray();
            var pedidos = new List<int>();
            var fake = Task.Run(async () =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var r = await srv.ReceiveAsync();
                    pedidos.Add(r.Buffer.Length);
                    // 1.º pedido: challenge 1; 2.º (com o challenge 1): challenge 2; 3.º (com o challenge 2): a resposta
                    if (i < 2) await srv.SendAsync(FF.Concat(new byte[] { 0x41, (byte)(i + 1), 2, 3, 4 }).ToArray(), r.RemoteEndPoint);
                    else await srv.SendAsync(r.Buffer.Length == 29 && r.Buffer[25] == 2 ? info : FF.Concat(new byte[] { 0x41, 9, 9, 9, 9 }).ToArray(), r.RemoteEndPoint);
                }
            });
            try
            {
                var q = A2S.Query("127.0.0.1", port, "127.0.0.1", 1500).GetAwaiter().GetResult();
                check("Zip: um servidor que pede o challenge duas vezes responde", q.Name == "Dois challenges", q.Name);
                check("Zip: cada pedido leva só o último challenge", pedidos.SequenceEqual(new[] { 25, 29, 29 }), string.Join(",", pedidos));
            }
            catch (Exception ex) { check("Zip: um servidor que pede o challenge duas vezes responde", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // registo.log 2793 (00:04:09): "one account was left as it is (the file has something the app can't keep exactly, so it was left as it is)"
        var naoVolta = Cs2LaunchOptions.Patch("\"UserLocalConfigStore\"\n{\n\t\"a\"\t\t\"{\"\n}\n");
        var linha = MainWindow.LeftText(naoVolta.Why);
        check("Zip: a linha das launch options não diz 'left as it is' duas vezes",
            !naoVolta.Changed && Regex.Matches(linha, "left as it is").Count == 1 && linha.EndsWith("(the file has something the app can't keep exactly)."), linha);

        // registo.log 2803 e 2805 (00:06:04): o mesmo IP de casa dito duas vezes, um Error com o IP e logo um Warn sem ele
        check("Zip: o IP igual ao de casa fica só na linha de erro com o IP",
            Guard.ProblemLine(DistrustText.Of(Distrust.SameIp), Distrust.SameIp) == null
            && MainWindow.SameIpText("1.2.3.4", unprotected: false).Contains("same as your home IP"), "");
        check("Zip: os outros motivos de bloqueio continuam no Log",
            Guard.ProblemLine(DistrustText.Of(Distrust.PhoneWifi), Distrust.PhoneWifi) == DistrustText.Of(Distrust.PhoneWifi) + " Servers blocked."
            && Guard.ProblemLine(DistrustText.Of(Distrust.Unchecked), Distrust.Unchecked) != null && Guard.ProblemLine(null, Distrust.None) == null, "");

        // registo.log 2845 a 2876 (01:08:22): Sync now com o CS2 aberto escreveu 31 linhas iguais, uma por conta
        var nomes = Enumerable.Range(1, 31).Select(i => "conta" + i).ToList();
        check("Zip: as cópias à espera do CS2 ficam numa linha", MainWindow.QueuedLine("Sync now", nomes) == "Config (Sync now): CS2 is open, so the copies to 31 accounts wait until it closes.",
            MainWindow.QueuedLine("Sync now", nomes));
        check("Zip: uma cópia à espera diz a conta", MainWindow.QueuedLine("Sync now", new() { "ana" }) == "Config (Sync now): CS2 is open, so the copy to ana waits until it closes."
            && MainWindow.QueuedLine("Sync now", new()) == null, "");
        check("Zip: o ReportCfg já não escreve uma linha por conta à espera",
            !File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Configs.cs")).Contains("so the copy to {who} waits"), "");
    }

    // ------------------------------------------------------------ 1. Tempo dos jogadores (A2S_PLAYER dá segundos, em float)

    static void TempoDosJogadores(Action<string, bool, string> check)
    {
        static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

        // O A2S manda segundos: 2280,7 s são 38 minutos, não 2280 minutos nem 38 segundos
        check("Jogadores: o tempo do A2S lê-se em segundos", Math.Abs(A2S.PlayerTime(2280.7f).TotalMinutes - 38.01) < 0.01, A2S.PlayerTime(2280.7f).ToString());

        // Valores estranhos que alguns servidores mandam: nunca uma exceção (antes um valor enorme deitava fora a lista toda)
        bool Seguro(float f, out TimeSpan t) { try { t = A2S.PlayerTime(f); return true; } catch { t = default; return false; } }
        check("Jogadores: NaN, infinito e negativo dão zero",
            Seguro(float.NaN, out var a) && a == TimeSpan.Zero && Seguro(float.PositiveInfinity, out var b) && b == TimeSpan.Zero && Seguro(-12f, out var c) && c == TimeSpan.Zero, "");
        var enorme = Seguro(1e30f, out var e);
        check("Jogadores: um tempo absurdo do servidor não deita a lista fora", enorme && e > TimeSpan.Zero, enorme ? e.ToString() : "exceção");

        // No cartão do jogador: curto, sempre com unidade, nunca "0:38" (lia-se 38 segundos) nem "1:05" (lia-se 1 minuto)
        var curtos = new (double s, string want)[] { (0, "<1m"), (59, "<1m"), (59.9, "<1m"), (60, "1m"), (2280.7, "38m"), (3599, "59m"), (3600, "1h 00m"), (3900, "1h 05m"), (45000, "12h 30m") };
        foreach (var (s, want) in curtos)
            check($"Jogadores: {s} s no cartão é \"{want}\"", PlayerRow.ShortTime(S(s)) == want, PlayerRow.ShortTime(S(s)));

        // Na dica: por extenso, como o resto da app ("1 h 05 min")
        var longos = new (double s, string want)[] { (0, "under 1 min"), (59, "under 1 min"), (60, "1 min"), (2280.7, "38 min"), (3599, "59 min"), (3600, "1 h"), (3900, "1 h 05 min"), (45000, "12 h 30 min") };
        foreach (var (s, want) in longos)
            check($"Jogadores: {s} s na dica é \"{want}\"", PlayerRow.LongTime(S(s)) == want, PlayerRow.LongTime(S(s)));

        var todos = curtos.Select(x => PlayerRow.ShortTime(S(x.s))).Concat(longos.Select(x => PlayerRow.LongTime(S(x.s)))).ToList();
        check("Jogadores: nenhum tempo é só um número", todos.All(t => Regex.IsMatch(t, "[a-z]")), string.Join(" | ", todos));

        // O cartão tem uma coluna fixa para o tempo: "12h 30m" (7 letras de Cascadia Mono a 11 px, 0,6 em cada) cabe sem tocar nos pontos
        var xaml = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "MainWindow.xaml"));
        var chip = xaml[xaml.IndexOf("x:Key=\"PlayerChip\"", StringComparison.Ordinal)..];
        chip = chip[..chip.IndexOf("</DataTemplate>", StringComparison.Ordinal)];
        var cols = Regex.Matches(chip, "<ColumnDefinition Width=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var largura = cols.Count == 4 && double.TryParse(cols[3], System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 0;
        check("Jogadores: o tempo mais comprido cabe no cartão", largura >= PlayerRow.ShortTime(S(45000)).Length * 11 * 0.6 + 8, $"coluna {largura}, precisa {PlayerRow.ShortTime(S(45000)).Length * 6.6 + 8:0.#}");

        var row = new PlayerRow { Name = "ana", Score = 12, Time = S(3900) };
        check("Jogadores: a dica diz o tempo por extenso", row.Tip.Contains("1 h 05 min on the server") && row.TimeText == "1h 05m", row.Tip.Replace("\n", " / ") + " · " + row.TimeText);
    }

    // ------------------------------------------------------------ 2. Miras em Configs

    static CfgSet Convars(params (string K, string V)[] kv)
    {
        var s = new CfgSet();
        s.Files[CfgPart.Convars] = Cs2Config.Crlf("\"config\"\n{\n\t\"convars\"\n\t{\n" + string.Concat(kv.Select(x => $"\t\t\"{x.K}\"\t\t\"{x.V}\"\n")) + "\t}\n}\n");
        return s;
    }

    /// <summary>Uma mira do sistema novo do CS2 (desde 22-23 set. 2026: tamanhos em píxeis no ecrã onde foi feita).</summary>
    static Crosshair Nova(string style, string len, string gap, string thick, string height, string outline = "0", string dot = "false") =>
        Cs2Config.ReadCrosshair(Convars(("cl_crosshairstyle", style), ("cl_crosshair_length", len), ("cl_crosshair_gap", gap), ("cl_crosshair_thickness", thick),
            ("cl_crosshair_screen_height", height), ("cl_crosshair_drawoutline", outline), ("cl_crosshairdot", dot),
            ("cl_crosshaircolor_r", "0"), ("cl_crosshaircolor_g", "255"), ("cl_crosshaircolor_b", "255"), ("cl_crosshaircolor_a", "255"),
            // as chaves antigas ficam no ficheiro, sem uso (como no ficheiro real de um PC com o CS2 de nov. 2026)
            ("cl_crosshairsize", "3"), ("cl_crosshairthickness", "0.5"), ("cl_crosshairgap", "-1")));

    /// <summary>Uma mira do sistema antigo (ficheiros que o CS2 escreveu antes de 22 set. 2026).</summary>
    static Crosshair Antiga(string size, string gap, string thick, string outline = "false", string outlineThick = "1") =>
        Cs2Config.ReadCrosshair(Convars(("cl_crosshairstyle", "4"), ("cl_crosshairsize", size), ("cl_crosshairgap", gap), ("cl_crosshairthickness", thick),
            ("cl_crosshaircolor", "4"), ("cl_crosshairusealpha", "true"), ("cl_crosshairalpha", "255"), ("cl_crosshair_drawoutline", outline),
            ("cl_crosshair_outlinethickness", outlineThick), ("cl_crosshairdot", "false")));

    static string Px(CrosshairPx p) => $"length {p.Length} · gap {p.Gap} · thick {p.Thickness}";

    static void Miras(Action<string, bool, string> check)
    {
        // Fontes (ver corrigido-app.md): notas do CS2 de 23 set. 2026 ("thickness = 1, length = 8, and gap = 4"; 720p com 2 px passa a 4 px
        // a 1440p); valores por omissão novos (comprimento 8, gap 4, espessura 2, altura 1080, estilo 7 Dynamic Quad); sistema antigo em
        // 1/480 da altura do ecrã, arredondado ao par, e gap = 4 + valor em píxeis ("size 2 was 4 pixels long at 1080 and 3 pixels at 768").
        void Mede(string nome, Crosshair c, int altura, int len, int gap, int thick)
        {
            var p = Cs2Config.CrosshairPixels(c, altura);
            check($"Mira: {nome} a {altura}p tem {len}/{gap}/{thick} px", p.Length == len && p.Gap == gap && p.Thickness == thick, Px(p));
        }

        // O exemplo das notas do CS2: 1 px de espessura, barras de 8 px, gap de 4 px
        Mede("exemplo da Valve", Nova("4", "8", "4", "1", "1080"), 1080, 8, 4, 1);
        // A mudança de resolução: feita a 720p com espessura 2, a 1440p fica com 4
        Mede("feita a 720p", Nova("4", "8", "4", "2", "720"), 1440, 16, 8, 4);
        // A mira por omissão do CS2 (Dynamic Quad)
        Mede("por omissão do CS2", Nova("7", "8", "4", "2", "1080"), 1080, 8, 4, 2);
        // b1t (NAVI), código CSqvLJtn4UrvzStpGJP9PjwVCYhsA44tkbwDWbeiLZKGhF: comprimento 2, espessura 2, gap 1, feita a 960
        Mede("do b1t", Nova("4", "2", "1", "2", "960"), 960, 2, 1, 2);
        Mede("do b1t", Nova("4", "2", "1", "2", "960"), 1920, 4, 2, 4);
        // Valores de um ficheiro real do CS2 de nov. 2026 (amarela, feita a 1536): a 1080p fica 7,7 -> 8, 4,2 -> 4, 2,1 -> 2
        Mede("feita a 1536", Nova("4", "11", "6", "3", "1536"), 1536, 11, 6, 3);
        Mede("feita a 1536", Nova("4", "11", "6", "3", "1536"), 1080, 8, 4, 2);
        // Sistema antigo: size 2 dá 4 px a 1080 e 3 px a 768; gap -2 dá 2 px; espessura 1 dá 2,25 -> 2 px
        Mede("antiga size 2", Antiga("2", "-2", "1"), 1080, 4, 2, 2);
        Mede("antiga size 2", Antiga("2", "-2", "1"), 768, 3, 2, 2);
        Mede("antiga por omissão do CS:GO", Antiga("5", "1", "0.5"), 1080, 11, 5, 1);

        // Contorno: o novo tem 0, 1 (todo) e 2 (meio contorno: só em cima e à esquerda), 1 px; o antigo é true/false com espessura
        check("Mira: contorno 2 é meio contorno", Cs2Config.CrosshairPixels(Nova("4", "8", "4", "2", "1080", outline: "2")).Outline == 2, "");
        check("Mira: contorno 1 é contorno todo, de 1 px", Cs2Config.CrosshairPixels(Nova("4", "8", "4", "2", "1080", outline: "1")) is { Outline: 1, OutlineWidth: 1 }, "");
        check("Mira: contorno 0 é sem contorno", Cs2Config.CrosshairPixels(Nova("4", "8", "4", "2", "1080", outline: "0")).Outline == 0, "");
        check("Mira: contorno antigo true conta", Cs2Config.CrosshairPixels(Antiga("2", "-2", "1", outline: "true", outlineThick: "2")) is { Outline: 1, OutlineWidth: 2 }, "");
        var corContorno = Cs2Config.ReadCrosshair(Convars(("cl_crosshair_length", "8"), ("cl_crosshair_drawoutline", "1"),
            ("cl_crosshairoutline_r", "255"), ("cl_crosshairoutline_g", "0"), ("cl_crosshairoutline_b", "128"), ("cl_crosshairoutline_a", "200")));
        check("Mira: a cor do contorno novo lê-se", corContorno is { OutlineR: 255, OutlineG: 0, OutlineB: 128, OutlineA: 200 }, corContorno?.ToString());
        // Estilo 6 do sistema novo: só o ponto
        var ponto = Cs2Config.CrosshairPixels(Nova("6", "8", "4", "3", "1080", dot: "false"));
        check("Mira: estilo 6 (Dot Only) é só o ponto", !ponto.Lines && ponto.Dot, ponto.ToString());
        check("Mira: estilo 6 no sistema antigo não existe (é uma cruz)", Cs2Config.CrosshairPixels(Antiga("2", "-2", "1")).Lines, "");

        // A imagem: as proporções do CS2 e tudo dentro da caixa (antes as barras grandes eram cortadas e ficavam mais curtas que o gap)
        foreach (var box in new[] { 26.0, 32.0 })
        {
            foreach (var (nome, c) in new[] { ("feita a 1536", Nova("4", "11", "6", "3", "1536")), ("por omissão", Nova("7", "8", "4", "2", "1080")),
                         ("grande", Nova("4", "40", "20", "4", "1080", outline: "1")), ("antiga do CS:GO", Antiga("5", "1", "0.5", outline: "true")) })
            {
                var px = Cs2Config.CrosshairPixels(c, 1080);
                var shapes = MainWindow.CrosshairShapes(c, box);
                var dentro = shapes.All(s => s.R.Left >= -0.01 && s.R.Top >= -0.01 && s.R.Right <= box + 0.01 && s.R.Bottom <= box + 0.01);
                var direita = shapes.Where(s => !s.Outline && s.R.Left > box / 2 && s.R.Height < s.R.Width).OrderBy(s => s.R.Left).FirstOrDefault();
                // o gap mede-se do centro da cruz: o meio da barra de baixo
                var baixo = shapes.FirstOrDefault(s => !s.Outline && s.R.Top > box / 2 && s.R.Height > s.R.Width);
                var centro = baixo == null ? box / 2 : baixo.R.Left + baixo.R.Width / 2;
                var g0 = direita == null ? double.NaN : direita.R.Left - centro;
                var prop = direita == null ? double.NaN : direita.R.Width / Math.Max(g0, 0.5);
                var quer = px.Length / (double)Math.Max(px.Gap, 1);
                // com espessura ímpar o centro cai a meio de um píxel: meio píxel de arredondamento no gap é o que o ecrã permite
                var perto = direita != null && new[] { g0 - 0.5, g0, g0 + 0.5 }.Any(g => g > 0 && Math.Abs(direita.R.Width / g - quer) / quer < 0.2);
                check($"Mira: a imagem {nome} cabe na caixa de {box}", dentro, string.Join(" ", shapes.Select(s => s.R.ToString())));
                check($"Mira: a imagem {nome} tem as proporções do CS2 ({box})", perto, $"imagem {prop:0.00}, CS2 {quer:0.00}");
            }
            // Uma mira pequena de pro fica ao dobro, como antes: barras de 4 px na imagem para 2 px no jogo
            var b1t = MainWindow.CrosshairShapes(Nova("4", "2", "1", "2", "960"), box);
            var barra = b1t.Where(s => !s.Outline && s.R.Left > box / 2).OrderBy(s => s.R.Left).FirstOrDefault();
            check($"Mira: a mira do b1t aparece ao dobro ({box})", barra != null && Math.Abs(barra.R.Width - 2 * 2 * 1080 / 960.0) < 0.6, barra?.R.ToString());
        }
        // Meio contorno: o contorno sai à esquerda e em cima de cada barra, nunca à direita nem em baixo
        var meio = MainWindow.CrosshairShapes(Nova("4", "8", "4", "2", "1080", outline: "2"), 32);
        var pares = meio.Where(s => s.Outline).Zip(meio.Where(s => !s.Outline)).ToList();
        check("Mira: meio contorno só em cima e à esquerda", pares.Count > 0 && pares.All(p => p.First.R.Left < p.Second.R.Left && p.First.R.Top < p.Second.R.Top
            && Math.Abs(p.First.R.Right - p.Second.R.Right) < 0.01 && Math.Abs(p.First.R.Bottom - p.Second.R.Bottom) < 0.01), string.Join(" ", meio.Select(s => s.R.ToString())));
        var soPonto = MainWindow.CrosshairShapes(Nova("6", "8", "4", "3", "1080"), 32);
        check("Mira: Dot Only desenha só o ponto", soPonto.Count(s => !s.Outline) == 1, soPonto.Count.ToString());
    }
}
