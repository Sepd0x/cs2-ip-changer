#nullable enable
using System.IO;
using SRT;
using SRT.Core;

// 26 nov.: os achados da auditoria do Codex (ServerRouteToolCodex/CODEX-RELATORIO.md) confirmados no main, um teste por correção.
// A3 e A4 são os testes que o Codex escreveu (tests/CodexAuditTests.cs da pasta dele), com a API de agora.
static class CodexAuditTests
{
    public static void Run(Action<string, bool, string> check)
    {
        Segunda(check);

        // A2: Whole PC só confirmava a metade de baixo do IPv4 (0/1) depois de escrever as duas rotas
        check("Codex A2: Whole PC só confirmado com as duas metades no telemóvel",
            Guard.BothHalvesOn(7, 7, 7) && !Guard.BothHalvesOn(7, 3, 7) && !Guard.BothHalvesOn(3, 7, 7), "");
        var guard = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Core", "Network", "Guard.cs"));
        check("Codex A2: depois das escritas relê as duas sondas",
            guard.Contains("fullOk = BothHalvesOn(Net.BestInterface(Net.PcProbeIp), Net.BestInterface(Net.PcProbeIpHigh), egress.Index);")
            && !guard.Contains("fullOk = Net.BestInterface(Net.PcProbeIp) == egress.Index;"), "");

        // A3: uma regra com o nome temporário de um Apply interrompido é um bloqueio que ficou
        const string updating = " (updating 0123456789)";
        var remaining = new[] { Firewall.Cs2RuleName + updating };
        var stayedOn = Guard.OnAtStart(false, Array.Empty<string>(), false, Firewall.BlockLeft(remaining), cs2Running: true, noPhone: false);
        check("Codex A3: o arranque mantém um bloqueio temporário com o jogo aberto", stayedOn, $"proteção mantida = {stayedOn}");
        var dns = new[] { Firewall.DnsUdpRuleName + updating };
        check("Codex A3: o fecho reconhece a regra DNS temporária que tem de remover", Firewall.Has(dns, Firewall.DnsUdpRuleName), "");
        check("Codex A3: o nome base tira só a cauda do Apply interrompido",
            Firewall.BaseName(Firewall.RuleName + updating) == Firewall.RuleName && Firewall.BaseName(Firewall.RuleName) == Firewall.RuleName && Firewall.BaseName(null) == null, "");
        check("Codex A3: a regra do teste de IP, mesmo temporária, não conta como bloqueio",
            !Firewall.BlockLeft(new[] { Firewall.EchoRuleName + updating }), "");

        // A4: duas marcas de ban com hosts e portas diferentes bloqueavam também os pares cruzados
        var rules = Firewall.BannedIpRules(new[] { "192.0.2.10:27015", "192.0.2.20:27016" });
        var crossed = rules.Any(r => r.RemoteIps.Contains("192.0.2.10") && r.RemotePorts!.Split(',').Contains("27016"))
            || rules.Any(r => r.RemoteIps.Contains("192.0.2.20") && r.RemotePorts!.Split(',').Contains("27015"));
        check("Codex A4: duas marcas de ban preservam os pares de endereço e porta", !crossed && rules.Count == 2, $"outro servidor bloqueado = {crossed}");
        var same = Firewall.BannedIpRules(new[] { "192.0.2.10:27015", "192.0.2.20:27015" });
        check("Codex A4: a mesma porta continua numa regra só", same.Count == 1 && same[0].Name == Firewall.BannedIpRuleName && same[0].RemoteIps.Count == 2, "");
        check("Codex A4: as regras de cada porta são da família da regra de ban (sentido, fecho, restore)",
            Firewall.IsBannedIpRule(Firewall.BannedIpPortName(27016)) && Firewall.IsBannedIpRule(Firewall.BannedIpPortName(27016) + updating)
            && Firewall.IsBannedIpRule(Firewall.BannedIpRuleName) && !Firewall.IsBannedIpRule(Firewall.RuleName)
            && Firewall.Meaning(Firewall.BannedIpPortName(27016)) == Firewall.Meaning(Firewall.BannedIpRuleName)
            && Firewall.IsAppRule(Firewall.BannedIpPortName(27016)) && !Firewall.BlockLeft(new[] { Firewall.BannedIpPortName(27016) }), "");
        var restore = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tools", "restore.cmd"));
        check("Codex A4: o restore.cmd tira as regras de cada porta (nome com Pickroute - *)", restore.Contains("'Pickroute - *'"), "");

        // A5: sem a lista de regiões, o Allow all ficava desligado e a regra guardada não saía pela página
        check("Codex A5: Allow all ligado sem a lista quando há regiões bloqueadas",
            MainWindow.AllowAllEnabled(false, false, 3, false) && MainWindow.AllowAllEnabled(false, false, 0, true) && !MainWindow.AllowAllEnabled(false, false, 0, false)
            && MainWindow.AllowAllEnabled(true, false, 0, false), "");
        var regions = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Regions.cs"));
        check("Codex A5: sem a lista tira a regra pelo nome do jogo e relê antes de dizer que saiu",
            regions.Contains("AllowAllWithoutList(_regionGame)") && regions.Contains("Firewall.Remove(game.RuleName);") && regions.Contains("Firewall.IsRegionRule(n, game)"), "");

        // A1 (decisão de 26 nov.: aceitar a proposta do Codex): Android por USB, sem ADB, sem IP de casa e o telemóvel no Wi-Fi de casa.
        // A app não consegue ver o Wi-Fi do telemóvel nem comparar IPs: fica bloqueado e não confirmado, nunca Protected
        var android = new Adapter { Index = 7, Name = "Ethernet 3", Description = "Remote NDIS based Internet Sharing Device", IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var unconfirmed = MainWindow.MobileUnconfirmed(hasHome: false, android, iphone: false, adbReady: false, homeIpFromHome: false);
        var distrust = MainWindow.DistrustNow(sameAsHome: false, phoneWifiOn: false, noHome: false, unconfirmed);
        check("Codex A1: USB sem ADB, sem IP de casa e o telemóvel no Wi-Fi de casa fica bloqueado e não confirmado",
            unconfirmed && distrust == Distrust.Unconfirmed && Guard.EgressFor(distrust, android) == null, $"{unconfirmed} {distrust}");
        check("Codex A1: com a depuração USB autorizada ou um IP de casa medido em casa, volta a confiar",
            MainWindow.DistrustNow(false, false, false, MainWindow.MobileUnconfirmed(false, android, false, adbReady: true, false)) == Distrust.None
            && MainWindow.DistrustNow(false, false, false, MainWindow.MobileUnconfirmed(false, android, false, false, homeIpFromHome: true)) == Distrust.None
            && Guard.EgressFor(Distrust.None, android) == android, "");
        check("Codex A1: as outras razões continuam primeiro (o mesmo IP, o Wi-Fi do telemóvel visto pelo ADB)",
            MainWindow.DistrustNow(true, false, false, true) == Distrust.SameIp && MainWindow.DistrustNow(false, true, false, true) == Distrust.PhoneWifi, "");
        check("Codex A1: a frase diz o que fazer e o Phone setup é o botão cheio",
            MainWindow.UnconfirmedBlockedSub("Servers and CS2").Contains("Phone setup") && MainWindow.UnconfirmedBlockedSub("x").Contains("home network")
            && !string.IsNullOrEmpty(DistrustText.Of(Distrust.Unconfirmed))
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Phone.cs")).Contains("_last.Distrust == Distrust.Unconfirmed")
            && File.ReadAllText(Path.Combine(SecurityTests.Repo(), "src", "Views", "MainWindow.Protection.cs")).Contains("DistrustNow(_distrust, PhoneWifiOn, _noHome, MobileUnconfirmedNow(_last))"), "");
    }

    // A6 a A16 (a segunda parte do relatório do Codex, 26 nov. ~19h30). O A9 não tem correção (ver publish/plano-1-0-0/23-criticos-correcoes).
    static void Segunda(Action<string, bool, string> check)
    {
        // A6: só Administradores, SYSTEM, TrustedInstaller, CREATOR OWNER e OWNER RIGHTS podem escrever na pasta
        var other = new System.Security.Principal.SecurityIdentifier("S-1-5-21-1-2-3-1105");   // um grupo ou utilizador qualquer do domínio
        check("Codex A6: escrita de um grupo que não é de confiança conta como escrita sem direitos de administrador",
            !Adb.TrustedWriter(other) && !Adb.TrustedWriter(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null))
            && !Adb.TrustedWriter(null)
            && Adb.TrustedWriter(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null))
            && Adb.TrustedWriter(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null)), "");

        // A7 (teste do Codex): uma tarefa desativada no Agendador aparece desligada
        const string exe = @"C:\Exemplo\app.exe";
        const string sid = "S-1-5-21-1-2-3-1001";
        var xml = Autostart.TaskXml(exe, sid);
        check("Codex A7: uma tarefa desativada aparece desligada",
            Autostart.IsOnFor(xml, exe, sid, @"Exemplo\Conta") && !Autostart.IsOnFor(xml.Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>"), exe, sid, @"Exemplo\Conta"), "");

        // A8: a transferência desiste sem receber nada durante o prazo
        TimeoutException? stalled = null;
        try { Updater.CopyWithIdle(new Parada(), new MemoryStream(), TimeSpan.FromMilliseconds(200), 1000, null, default).GetAwaiter().GetResult(); }
        catch (TimeoutException ex) { stalled = ex; }
        var copied = Updater.CopyWithIdle(new MemoryStream(new byte[5000]), new MemoryStream(), TimeSpan.FromSeconds(5), 10_000, null, default).GetAwaiter().GetResult();
        check("Codex A8: a transferência parada desiste com um erro, e uma normal copia tudo", stalled != null && copied == 5000, stalled?.Message ?? "sem erro");

        // A11: o Copy all copia o que a página mostra (com Hide logins, mascarado)
        var line = new LogEntry { Time = new DateTime(2026, 11, 26, 19, 0, 0), Text = "Home IP 82.154.10.20, login atlas_alt" };
        line.Shown = "Home IP •••, login •••";
        check("Codex A11: o Copy all copia o texto mascarado", MainWindow.LogCopyText(new[] { line }) == "19:00:00  Home IP •••, login •••", MainWindow.LogCopyText(new[] { line }));

        // A12 (teste do Codex): a validação aceita o ID que a conversão de ActiveUser produz
        var highId = SteamIds.FromId32(3_000_000_000u);
        check("Codex A12: a validação aceita todo o intervalo dos SteamID64",
            SteamIds.IsId64(highId) && SteamIds.IsId64("76561197960265729") && !SteamIds.IsId64(SteamIds.Base.ToString())
            && !SteamIds.IsId64("76561202255233024") && !SteamIds.IsId64("7656119796026572٩") && !SteamIds.IsId64("1234"), highId);

        // A13 (teste do Codex): uma duração enorme é recusada, sem exceção
        TimeSpan? huge = TimeSpan.Zero;
        try { huge = Fmt.ParseDuration("100000000d"); } catch (OverflowException) { }
        check("Codex A13: duração acima do máximo é recusada", huge == null && Fmt.ParseDuration("3d 4h") == TimeSpan.FromHours(76) && Fmt.ParseDuration("3651d") == null, "");

        // A14: o IP de casa vai mascarado para o Log
        check("Codex A14: a linha do IP igual ao de casa não escreve o IP inteiro",
            !MainWindow.SameIpText("82.154.10.20", true).Contains("82.154.10.20") && !MainWindow.SameIpText("82.154.10.20", false).Contains("82.154.10.20")
            && MainWindow.SameIpText("82.154.10.20", false).Contains("82.154."), "");

        // A10 e A15 (teste do Codex): a pausa das estatísticas
        PausaDasEstatisticas(check);

        // A16 (teste do Codex): a segunda leitura de um registo UTF-16
        var log = Path.Combine(Path.GetTempPath(), $"ligacao-exemplo-{Guid.NewGuid():N}.txt");
        try
        {
            var watcher = new SteamLogWatcher();
            File.WriteAllText(log, "[2026-01-01 12:00:00] RecvMsgClientLogOnResponse() : processing complete\r\n", System.Text.Encoding.Unicode);
            watcher.Poll(log);
            File.AppendAllText(log, "[2026-01-01 12:01:00] ConnectionDisconnected(1)\r\n", System.Text.Encoding.Unicode);
            var events = watcher.Poll(log);
            check("Codex A16: a segunda leitura de um registo UTF-16 deteta a desconexão",
                events.Any(e => e.Kind == "dropped") && watcher.Online == false, $"eventos = {events.Count}, online = {watcher.Online}");
        }
        finally { try { File.Delete(log); } catch { } }
    }

    static void PausaDasEstatisticas(Action<string, bool, string> check)
    {
        var file = Path.Combine(Path.GetTempPath(), $"estatisticas-exemplo-{Guid.NewGuid():N}.json");
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var phone = typeof(Telemetry).GetField("_phoneEnv", fields)!;
        var originalPhone = phone.GetValue(null);
        var originalTransport = Telemetry.TestSend;
        var oldBrandSent = false;
        Telemetry.TestSend = (url, body) =>
        {
            if (url.EndsWith("/env", StringComparison.Ordinal))
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                oldBrandSent = json.RootElement.TryGetProperty("brand", out var b) && b.GetString() == "google";
            }
            return Task.FromResult(true);
        };
        try
        {
            Telemetry.Start(false, () => (null!, null!, new()), true, file);
            Telemetry.Screen("settings");
            Telemetry.SetOff(true);
            // só o intervalo desligado avança, sem depender da velocidade da máquina
            typeof(Telemetry).GetField("_screenSince", fields)!.SetValue(null, DateTime.UtcNow.AddHours(-1));
            Telemetry.Phone(new PhoneInfo(null, "device", null, null, null, null, Brand: "google"));
            Telemetry.Phone(null);
            Telemetry.SetOff(false);
            Telemetry.Screen(null);
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var minutes = saved.RootElement.GetProperty("Screens").TryGetProperty("settings", out var value) ? value.GetDouble() : 0;
            check("Codex A10: os minutos de página excluem a pausa das estatísticas", minutes < 1, $"minutos atribuídos = {minutes:F1}");
            var send = (Task)typeof(Telemetry).GetMethod("SendEnv", fields)!.Invoke(null, new object[] { false })!;
            send.GetAwaiter().GetResult();
            check("Codex A15: o relatório exclui o telefone só observado durante a pausa", !oldBrandSent, $"marca incluída = {oldBrandSent}");
        }
        finally
        {
            Telemetry.SetOff(true);
            Telemetry.Start(false, () => (null!, null!, new()), false, file);
            phone.SetValue(null, originalPhone);
            Telemetry.TestSend = originalTransport;
            try { File.Delete(file); File.Delete(file + ".prev"); } catch { }
        }
    }

    /// <summary>Um corpo de resposta que nunca entrega nada (o servidor mandou os cabeçalhos e calou-se).</summary>
    sealed class Parada : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
