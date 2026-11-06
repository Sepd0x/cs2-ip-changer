using SRT;
using SRT.Core;

// beta.8, parte 4: kicks e bans só nos servidores comunitários (cada tipo de desconexão) e o envio das estatísticas ao fechar
static class Part4Tests
{
    // Linhas reais da consola de 1 nov. 2026 (console.log do zip), com o IP do servidor trocado por um de documentação
    const string Srv = "203.0.113.20:27015";
    static readonly string[] RealBan =
    {
        $"11/01 18:41:19 [Client] CL:  Connected to '{Srv}'",
        "11/01 18:54:41 [SteamNetSockets] [#3363449582 UDP steamid:(hidden):27005 'server'] closed by peer (2006): NETWORK_DISCONNECT_STEAM_BANNED",
        "11/01 18:54:41 [NetSteamConn] Steam Net connection #3363449582 UDP steamid:(hidden):27005 'server' closed by peer, reason 2006: NETWORK_DISCONNECT_STEAM_BANNED",
        "11/01 18:54:41 [Networking] Netchan 'server' closed unexpectedly: NETWORK_DISCONNECT_STEAM_BANNED",
        $"11/01 18:54:41 [NetSteamConn] Closing Steam Net Connection on socket 'client' to {Srv}, handle #3363449582 (0 (null))",
        "11/01 18:54:41 [Client] Disconnection during connection phase. Sign-on state: 6 (SIGNONSTATE_FULL). Disconnect reason: NETWORK_DISCONNECT_STEAM_BANNED.",
        "11/01 18:54:41 [Client] Disconnected from server: NETWORK_DISCONNECT_STEAM_BANNED",
        "11/01 18:54:41 [Client] Disconnecting from server: NETWORK_DISCONNECT_STEAM_BANNED",
        "11/01 18:54:42 [Client] CL:  Connected to 'loopback:1'",
    };
    static readonly string[] RealTimeout =
    {
        $"11/01 18:38:01 [Client] CL:  Connected to '{Srv}'",
        "11/01 18:40:51 [SteamNetSockets] [#1666051611 UDP steamid:(hidden):27005 'server'] 20 reply timeouts, last recv 17105.5ms ago.  Sending keepalive.",
        "11/01 18:40:51 [SteamNetSockets] [#1666051611 UDP steamid:(hidden):27005 'server'] Timed out.  17284.5ms since last recv, 10013.5ms since timeout started, 20 consecutive failures",
        "11/01 18:40:51 [SteamNetSockets] [#1666051611 UDP steamid:(hidden):27005 'server'] problem detected locally (5003): Connection dropped",
        "11/01 18:40:51 [Networking] Netchan 'server' closed unexpectedly: Connection dropped",
        "11/01 18:40:51 [Client] Disconnection during connection phase. Sign-on state: 6 (SIGNONSTATE_FULL). Disconnect reason: NETWORK_DISCONNECT_TIMEDOUT.",
        "11/01 18:40:51 [Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT",
        "11/01 18:40:51 [Client] Disconnecting from server: NETWORK_DISCONNECT_TIMEDOUT",
        "11/01 18:40:51 [Client] CL:  Connected to 'loopback:1'",
    };
    // Mudança de mapa num servidor com mapa da workshop: descarrega o addon e volta a entrar no mesmo servidor
    static readonly string[] RealMapChange =
    {
        $"11/01 18:33:32 [Client] CL:  Connected to '{Srv}'",
        "11/01 18:38:00 [HostStateManager] CHostStateMgr::QueueNewRequest( Downloading Addons (3758045880), 3 )",
        "11/01 18:38:00 [Client] Disconnecting from server: NETWORK_DISCONNECT_LOOPDEACTIVATE",
        $"11/01 18:38:00 [NetSteamConn] Closing Steam Net Connection on socket 'client' to {Srv}, handle #557194791 (2055 NETWORK_DISCONNECT_LOOPDEACTIVATE)",
        "11/01 18:38:00 [SteamNetSockets] [#557194791 UDP steamid:(hidden):27005 'server'] closed by app, entering linger state (2055) NETWORK_DISCONNECT_LOOPDEACTIVATE",
    };
    static readonly string[] RealQuit =
    {
        "11/01 18:54:46 [Client] Disconnecting from server: NETWORK_DISCONNECT_EXITING",
        "11/01 18:54:46 [NetSteamConn] Closing Steam Net Connection on socket 'client' to loopback:1, handle #642140503 (1059 NETWORK_DISCONNECT_EXITING)",
        "11/01 18:54:46 [Server] Disconnect client '(hidden)' from server: NETWORK_DISCONNECT_SHUTDOWN (Server shutdown)",
    };

    static Disconnect Run(DateTime t, params string[] lines)
    {
        var k = new DisconnectTracker();
        foreach (var l in lines) k.Feed(l, t);
        return k.Take(t);
    }

    static string Join(string code, int signOn = 6) =>
        $"[Client] Disconnection during connection phase. Sign-on state: {signOn} ({(signOn == 6 ? "SIGNONSTATE_FULL" : "SIGNONSTATE_CONNECTED")}). Disconnect reason: NETWORK_DISCONNECT_{code}.";

    public static void Run(CoreTests.CheckFn check)
    {
        var t = new DateTime(2026, 11, 1, 18, 54, 41);
        var on = $"[Client] CL:  Connected to '{Srv}'";

        // Linhas reais de 1 nov.
        var ban = Run(t, RealBan);
        check("Kicks: ban real (STEAM_BANNED, já no jogo) é ban, no servidor certo, com código de ban",
            ban?.Kind == KickKind.Ban && ban.Server == Srv && ban.BanCode && ban.SignOn == 6 && ban.Reason == "on the server's ban list" && ban.WantsSwitch, $"{ban}");
        var to = Run(t, RealTimeout);
        check("Kicks: timeout real é a ligação, nunca kick nem ban", to?.Kind == KickKind.NoAnswer && !to.WantsSwitch && to.Server == Srv, $"{to}");
        var map = Run(t, RealMapChange);
        check("Kicks: mudança de mapa real (addon da workshop) não diz nada", map != null && map.Kind == KickKind.MapChange && !Cs2Console.Acts(map.Kind), $"{map}");
        var quit = Run(t, RealQuit);
        check("Kicks: sair do jogo (EXITING) não diz nada", quit?.Kind == KickKind.Quit && !Cs2Console.Acts(quit.Kind), $"{quit}");

        // Servidores da Valve: nem kick, nem ban, nem rejoin
        var valve = new DisconnectTracker();
        valve.Feed("[Client] CL:  Connected to '=[A:1:4005189648:51168]'", t);
        foreach (var l in new[] { Join("KICKED_VOTEDOFF"), Join("KICKBANADDED"), "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED_IDLE" }) valve.Feed(l, t);
        check("Kicks: num servidor da Valve (SDR) nada conta", valve.OnValve && valve.Server == null && valve.Take(t) == null);
        valve.Feed("[Client] CL:  Connected to 'loopback:1'", t);
        valve.Feed(on, t);
        valve.Feed(Join("KICKED"), t);
        check("Kicks: depois da Valve, num comunitário volta a contar", !valve.OnValve && valve.Take(t)?.Kind == KickKind.Kick);
        check("Kicks: o endereço da Valve não passa por servidor comunitário", Cs2Console.IsValveServer("CL:  Connected to '=[A:1:2705930261:29230]'")
            && !Cs2Console.IsValveServer(on) && Cs2Console.ConnectedServer("CL:  Connected to '=[A:1:2705930261:29230]'") == null);

        // Kick com motivo
        var idle = Run(t, on, "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED_IDLE");
        check("Kicks: AFK é kick com motivo, só rejoin", idle?.Kind == KickKind.Kick && idle.Reason == "kicked for being AFK" && !idle.WantsSwitch, $"{idle}");
        var vote = Run(t, on, Join("KICKED_VOTEDOFF"));
        check("Kicks: kick por votação diz o motivo", vote?.Kind == KickKind.Kick && vote.Reason == "kicked by a vote", $"{vote}");
        var said = Run(t, on, "You have been kicked for spamming the chat by Admin!", "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED");
        check("Kicks: o motivo que o plugin diz antes do kick (SimpleAdmin) fica", said?.Kind == KickKind.Kick && said.Reason == "kicked for spamming the chat", $"{said}");
        var afkWarn = Run(t, on, "You're Idle/AFK. Move or you'll be kicked in 10 seconds.", "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED");
        check("Kicks: aviso de AFK de um plugin antes de um kick simples", afkWarn?.Kind == KickKind.Kick && afkWarn.Reason == "kicked for being AFK", $"{afkWarn}");
        var mixed = Run(t, on, Join("KICKED"), "[Client] Disconnected from server: NETWORK_DISCONNECT_DISCONNECT_BY_USER");
        check("Kicks: kick seguido de DISCONNECT_BY_USER continua kick", mixed?.Kind == KickKind.Kick, $"{mixed}");

        // Ban com tempo
        var timed = Run(t, on, "You have been banned for cheating for 30 minutes by Admin!", Join("KICKBANADDED"));
        check("Kicks: ban com tempo (SimpleAdmin: minutos)", timed?.Kind == KickKind.Ban && timed.Minutes == 30 && timed.For == " for 30 minutes" && timed.WantsSwitch, $"{timed}");
        var perm = Run(t, on, "You have been banned permanently for wallhack by Admin!", Join("KICKBANADDED"));
        check("Kicks: ban permanente", perm?.Kind == KickKind.Ban && perm.Minutes == 0 && perm.For == " permanently", $"{perm}");
        var again = Run(t, on, Join("REJECT_BANNED", 2));
        check("Kicks: banido a voltar (REJECT_BANNED a entrar) é ban", again?.Kind == KickKind.Ban && again.BanCode && again.Reason == "the server refused you: banned", $"{again}");
        var joining = Run(t, on, Join("KICKED", 2));
        // Revisão F9 (decisão do dono): uma entrada recusada sem motivo não é marca de ban (whitelist, Prime only)
        check("Kicks: kick sem motivo ainda a entrar é uma entrada recusada, não ban", joining?.Kind == KickKind.Kick && joining.Why == KickReason.RefusedJoin && !joining.WantsSwitch, $"{joining}");
        var plainBan = Run(t, on, "You have been banned from this server.", "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED");
        check("Kicks: plugin que bane com kick simples, mas diz que é ban", plainBan?.Kind == KickKind.Ban, $"{plainBan}");

        // VPN e proxy
        var vpn = Run(t, on, "VPN/Proxy detected! Disconnecting...", "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED");
        check("Kicks: bloqueio de VPN (kick) leva a New IP + next account, sem marcar ban", vpn?.Kind == KickKind.Vpn && !vpn.BanCode && vpn.WantsSwitch, $"{vpn}");
        var vpnBan = Run(t, on, "You have been banned for VPN Detected for 18000 minutes by Console!", Join("KICKBANADDED"));
        check("Kicks: bloqueio de VPN com ban (AntiVPN + SimpleAdmin) marca ban com o tempo", vpnBan?.Kind == KickKind.Vpn && vpnBan.BanCode && vpnBan.Minutes == 18000, $"{vpnBan}");
        var vpnOld = new DisconnectTracker();
        vpnOld.Feed(on, t);
        vpnOld.Feed("VPN usage is not allowed on this server", t.AddSeconds(-60));
        vpnOld.Feed(Join("KICKED"), t);
        check("Kicks: uma frase de VPN de há 1 minuto já não conta", vpnOld.Take(t)?.Kind == KickKind.Kick);

        // Cheio
        var full = Run(t, on, Join("REJECT_SERVERFULL", 2));
        check("Kicks: servidor cheio a entrar", full?.Kind == KickKind.Full && full.Reason == "the server is full" && !full.WantsSwitch, $"{full}");
        var slot = Run(t, on, Join("REJECT_SERVERFULL", 6));
        check("Kicks: cheio já no jogo é um slot reservado", slot?.Kind == KickKind.Full && slot.Reason.Contains("reserved slot"), $"{slot}");

        // Rede
        var never = Run(t, on, "[Client] Disconnected from server: NETWORK_DISCONNECT_CONNECT_REQUEST_TIMEDOUT");
        check("Kicks: o servidor nunca respondeu é rede", never?.Kind == KickKind.NoAnswer && never.Reason == "the server never answered", $"{never}");

        // O ban de outro jogador anunciado a todos nunca vira o teu
        var other = Run(t, on, "Admin banned Bob for 30 minutes! Reason: cheating", "[Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT");
        check("Kicks: o anúncio do ban de outro jogador não muda um timeout", other?.Kind == KickKind.NoAnswer, $"{other}");
        check("Kicks: uma frase do servidor sozinha não é desconexão", Run(t, on, "VPN/Proxy detected! Disconnecting...") == null);
        check("Kicks: gag de chat não é ban", Cs2Console.Classify("You have been banned from chat for 10 minutes") == KickKind.None);

        // Revisão: gag de chat antes de um kick, frases sobre outro jogador, "perma" no motivo, frase de outro servidor
        var gag = Run(t, on, "You have been banned from chat for 10 minutes", "[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED");
        check("Kicks: um gag de chat antes de um kick não o torna ban", gag?.Kind == KickKind.Kick && gag.Minutes == null && Cs2Console.HintOf("You have been banned from chat for 10 minutes") == null, $"{gag}");
        var otherVpn = Run(t, on, "[AntiVPN] Foo kicked: VPN detected", Join("KICKED"));
        check("Kicks: o VPN de outro jogador não muda o teu kick", otherVpn?.Kind == KickKind.Kick, $"{otherVpn}");
        check("Kicks: frases sobre outros não são pistas", Cs2Console.HintOf("Foo was kicked for using a VPN") == null
            && Cs2Console.HintOf("Foo was kicked to make room for a reserved slot") == null && Cs2Console.HintOf("Player Foo is AFK and will be kicked") == null);
        check("Kicks: o aviso de VPN no início da mensagem conta, com a hora e a etiqueta à frente",
            Cs2Console.HintOf("11/01 18:54:40 [AntiVPN] VPN/Proxy detected! Disconnecting...")?.Vpn == true && Cs2Console.HintOf("VPN usage is not allowed on this server")?.Vpn == true);
        check("Kicks: um tempo dito ganha a 'perma' no motivo", Cs2Console.MinutesIn("You have been banned for perma-camping for 30 minutes") == 30
            && Cs2Console.MinutesIn("You have been banned permanently") == 0);
        var moved = new DisconnectTracker();
        moved.Feed(on, t);
        moved.Feed("VPN/Proxy detected! Disconnecting...", t);
        moved.Feed("[Client] CL:  Connected to '198.51.100.7:27015'", t);
        moved.Feed(Join("KICKED"), t);
        check("Kicks: a frase de um servidor não passa para o seguinte", moved.Take(t)?.Kind == KickKind.Kick);

        // Durações
        check("Kicks: tempos em horas, dias, segundos e o último 'for' da linha",
            Cs2Console.MinutesIn("for 2 hours") == 120 && Cs2Console.MinutesIn("for 1 day") == 1440 && Cs2Console.MinutesIn("for 30 seconds") == 1
            && Cs2Console.MinutesIn("banned for camping for 45 minutes") == 45 && Cs2Console.MinutesIn("no time") == null);
        check("Kicks: texto do tempo", new Disconnect(KickKind.Ban, "", null, Minutes: 120).For == " for 2 hours" && new Disconnect(KickKind.Ban, "", null, Minutes: 2880).For == " for 2 days"
            && new Disconnect(KickKind.Ban, "", null, Minutes: 90).For == " for 90 minutes" && new Disconnect(KickKind.Ban, "", null).For == "");
        check("Kicks: a marca de um ban com tempo acaba", new ServerBan { Until = DateTime.Now.AddMinutes(-1) }.Over && !new ServerBan { Until = DateTime.Now.AddMinutes(5) }.Over && !new ServerBan().Over);

        // Códigos de conta (Steam): um IP novo não ajuda
        foreach (var c in new[] { "KICKED_UNTRUSTEDACCOUNT", "KICKED_CONVICTEDACCOUNT", "KICKED_INSECURECLIENT", "REJECT_STEAM" })
            check("Kicks: " + c + " é da conta (Steam)", Cs2Console.Classify(Join(c)) == KickKind.Steam);

        // Estatísticas: o envio ao fechar
        var now = DateTime.UtcNow;
        check("Telemetria: ao fechar envia os minutos da sessão", Telemetry.SendAtClose(now.AddMinutes(-40), now, 39.6, 0, 0));
        check("Telemetria: ao fechar envia os cliques mesmo com menos de 1 minuto", Telemetry.SendAtClose(now.AddMinutes(-12), now, 0.2, 3, 0));
        check("Telemetria: ao fechar não envia a menos de 10 min do último (o servidor recusa)", !Telemetry.SendAtClose(now.AddMinutes(-9), now, 9, 5, 0));
        check("Telemetria: ao fechar não envia sem nada novo", !Telemetry.SendAtClose(now.AddHours(-3), now, 0.4, 0, 0));
        check("Telemetria: a primeira vez (nunca enviado) também envia", Telemetry.SendAtClose(default, now, 5, 0, 0));
        check("Telemetria: o aviso já não diz 'once a day'", !Telemetry.NoticeText.Contains("once a day") && Telemetry.NoticeText.Contains("when it opens and closes"));
    }
}
