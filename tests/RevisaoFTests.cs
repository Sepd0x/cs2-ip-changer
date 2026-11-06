using System.IO;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área F: o Steam e o CS2 por dentro): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
// As linhas da consola levam a hora à frente, como no console.log real ("11/06 21:43:13 [ALL] ...").
static class RevisaoFTests
{
    const string Srv = "203.0.113.20:27015";
    const string On = "11/06 21:49:00 [Client] CL:  Connected to '" + Srv + "'";

    static Disconnect Run(DateTime t, params string[] lines)
    {
        var k = new DisconnectTracker();
        foreach (var l in lines) k.Feed(l, t);
        return k.Take(t);
    }

    static string Join(string code, int signOn = 6) =>
        $"11/06 21:50:05 [Client] Disconnection during connection phase. Sign-on state: {signOn} ({(signOn == 6 ? "SIGNONSTATE_FULL" : "SIGNONSTATE_CONNECTED")}). Disconnect reason: NETWORK_DISCONNECT_{code}.";

    static string Gone(string code) => "11/06 21:50:05 [Client] Disconnected from server: NETWORK_DISCONNECT_" + code;

    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão F: as fontes leem-se", Src("Views", "MainWindow.Kick.cs").Length > 0, "");
        var t = new DateTime(2026, 11, 6, 21, 50, 5);
        Console_(check, t);
        Steam_(check, Src);
        Kick_(check, Src, t);
    }

    static void Console_(Action<string, bool, string> check, DateTime t)
    {
        // F1. O chat com a hora do console.log à frente não é kick, ban nem No Steam logon; uma frase sozinha nunca é desconexão
        check("Revisão F1: chat com a hora não é ban", Cs2Console.Classify("11/06 21:50:00 [ALL] Bob: you got banned lol") == KickKind.None,
            Cs2Console.Classify("11/06 21:50:00 [ALL] Bob: you got banned lol").ToString());
        check("Revisão F1: chat com a hora não é No Steam logon", !Cs2Console.IsNoSteamLogon("11/06 21:50:00 [ALL] Bob: no steam logon again"), "");
        check("Revisão F1: chat com a hora e \"disconnect\" também não é ban (caminho fraco)",
            Cs2Console.Classify("11/06 21:50:00 [ALL] Bob: disconnect and get banned") == KickKind.None
            && Cs2Console.Classify("11/06 21:50:00 *DEAD* [CT] Bob: disconnected kick") == KickKind.None, "");
        check("Revisão F1: um comando escrito na consola, com a hora, não é kick", Cs2Console.Classify("11/06 21:50:00 ] kick bob") == KickKind.None, "");
        check("Revisão F1: chat com a hora não é pista nem amostra", Cs2Console.HintOf("11/06 21:50:00 [ALL] Bob: you were banned") == null
            && !Cs2Console.IsDisconnect("11/06 21:50:00 [ALL] Bob: disconnect lol"), "");
        check("Revisão F1: \"you were kicked\" no chat sem desconexão não dá kick", Run(t, On, "11/06 21:50:00 [ALL] Bob: you were kicked xd") == null, "");
        check("Revisão F1: \"you have been banned\" sozinha não é desconexão",
            Run(t, On, "11/06 21:50:00 You have been permanently banned from this server") == null
            && Cs2Console.Classify("You have been permanently banned from this server") == KickKind.None, "");
        var said = Run(t, On, "11/06 21:50:00 You have been banned from this server.", Gone("KICKED"));
        check("Revisão F1: a frase continua a dizer que o kick que vem a seguir é ban", said?.Kind == KickKind.Ban, $"{said}");

        // F2. Um anúncio de VPN não muda um código que já diz o motivo, nem um servidor cheio
        const string advert = "11/06 21:49:45 [ADVERT] VPN/Proxy not allowed on this server!";
        check("Revisão F2: o anúncio é mesmo uma pista de VPN (o que fazia o erro)", Cs2Console.HintOf(advert)?.Vpn == true, "");
        var vote = Run(t, On, advert, Gone("KICKED_VOTEDOFF"));
        check("Revisão F2: anúncio de VPN não muda um kick por votação", vote?.Kind == KickKind.Kick && vote.Reason == "kicked by a vote", $"{vote}");
        var idle = Run(t, On, advert, Gone("KICKED_IDLE"));
        check("Revisão F2: nem um kick por AFK", idle?.Kind == KickKind.Kick && idle.Reason == "kicked for being AFK", $"{idle}");
        var full = Run(t, On, advert, Join("REJECT_SERVERFULL", 2));
        check("Revisão F2: nem um servidor cheio", full?.Kind == KickKind.Full && !full.WantsSwitch, $"{full}");
        var vpnDrop = Run(t, On, "11/06 21:50:00 [AntiVPN] VPN/Proxy detected! Disconnecting...", Gone("DISCONNECT_BY_SERVER"));
        check("Revisão F2: um AntiVPN que expulsa com DISCONNECT_BY_SERVER continua a ser VPN", vpnDrop?.Kind == KickKind.Vpn, $"{vpnDrop}");
        var vpnBan = Run(t, On, "11/06 21:50:00 You have been banned for VPN Detected for 18000 minutes by Console!", Join("KICKBANADDED"));
        check("Revisão F2: e um ban de VPN com código continua VPN com ban", vpnBan?.Kind == KickKind.Vpn && vpnBan.BanCode, $"{vpnBan}");

        // F3. Os códigos de sessão do Steam são da família do No Steam logon; os da conta continuam Steam
        foreach (var c in new[] { "STEAM_DROPPED", "STEAM_VAC_CHECK_TIMEDOUT", "STEAM_AUTHCANCELLED", "STEAM_AUTHALREADYUSED", "STEAM_TICKET", "STEAM_LOGON" })
            check("Revisão F3: " + c + " é a sessão do Steam, não a conta", Cs2Console.IsNoSteamLogon(Gone(c)) && Cs2Console.Classify(Gone(c)) != KickKind.Steam,
                Cs2Console.Classify(Gone(c)).ToString());
        foreach (var c in new[] { "STEAM_VACBANSTATE", "STEAM_OWNERSHIP", "STEAM_AUTHINVALID", "STEAM_INUSE", "STEAM_LOGGED_IN_ELSEWHERE" })
            check("Revisão F3: " + c + " continua a ser da conta", Cs2Console.Classify(Gone(c)) == KickKind.Steam && !Cs2Console.IsNoSteamLogon(Gone(c)), "");
        check("Revisão F3: só o código do No Steam logon se chama assim", Cs2Console.SaysNoSteamLogon(Gone("STEAM_LOGON")) && Cs2Console.SaysNoSteamLogon("Disconnect: NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN")
            && !Cs2Console.SaysNoSteamLogon(Gone("STEAM_DROPPED")), "");
        check("Revisão F3: o toast de outro código da família não diz \"No Steam logon\"", !MainWindow.NoSteamLogonToast(false, said: false).Contains("No Steam logon")
            && MainWindow.NoSteamLogonToast(false, said: false).Contains("through the phone") && MainWindow.NoSteamLogonToast(false).Contains("No Steam logon"), MainWindow.NoSteamLogonToast(false, said: false));
        check("Revisão F3: a conta noutro PC tem a sua frase", Cs2Console.LoggedInElsewhere(Gone("STEAM_LOGGED_IN_ELSEWHERE")) && !Cs2Console.LoggedInElsewhere(Gone("STEAM_VACBANSTATE"))
            && MainWindow.SteamRefusedToast(true).Contains("logged in somewhere else") && MainWindow.SteamRefusedToast(false).Contains("check the account"), "");

        // F9. Um kick sem motivo ainda a entrar é uma entrada recusada: não é ban, não pede a troca
        var refused = Run(t, On, Join("KICKED", 2));
        check("Revisão F9: kick sem motivo ainda a entrar não é ban", refused?.Kind == KickKind.Kick && refused.Why == KickReason.RefusedJoin && !refused.WantsSwitch
            && refused.Reason == "the server refused the join without saying why", $"{refused}");
        var inGame = Run(t, On, Join("KICKED", 6));
        check("Revisão F9: um kick sem motivo já no jogo continua um kick simples", inGame?.Kind == KickKind.Kick && inGame.Why == KickReason.Other && inGame.Reason == null, $"{inGame}");
        var told = Run(t, On, "11/06 21:50:00 You have been banned from this server.", Join("KICKED", 2));
        check("Revisão F9: um ban dito antes, ainda a entrar, continua ban", told?.Kind == KickKind.Ban, $"{told}");
    }

    static void Steam_(Action<string, bool, string> check, Func<string[], string> src)
    {
        string Src(params string[] p) => src(p);
        var acc = Src("Core", "Steam", "SteamAccounts.cs");

        // F4. Uma hora no futuro no loginusers.vdf que não mudou não é "outra conta"
        var w = 1_800_000_000L;
        var accs = new[] { new LocalAccount("76561198000000001", "x", "X", false, w + 3600), new LocalAccount("76561198000000002", "y", "Y", true, w) };
        check("Revisão F4: uma hora no futuro que não mudou não é 'outra conta'",
            Steam.LoginDecision("76561198000000002", null, accs, w, false, nowUnix: w + 5).State == Steam.LoginState.Waiting, "");
        var now = new[] { new LocalAccount("76561198000000001", "x", "X", false, w + 3), new LocalAccount("76561198000000002", "y", "Y", true, w) };
        check("Revisão F4: o Steam a entrar mesmo noutra conta agora continua a contar",
            Steam.LoginDecision("76561198000000002", null, now, w, false, nowUnix: w + 5) is { State: Steam.LoginState.Other, Other: "76561198000000001" }, "");
        check("Revisão F4: o WaitForLogin passa a hora de agora", acc.Contains("DateTimeOffset.UtcNow.ToUnixTimeSeconds());\n            if (r.State != LoginState.Waiting) return r;"), "");

        // F5. Restart e Add account param quando o Steam não fechou
        var restart = acc[acc.IndexOf("public static async Task Restart(")..acc.IndexOf("public static bool WantsOffline(")];
        var addNew = acc[acc.IndexOf("public static async Task AddNew(")..];
        check("Revisão F5: Restart para se o Steam não fechou", restart.Contains("if (!SteamClosedNow()) throw") && restart.IndexOf("SteamClosedNow()") < restart.IndexOf("Launch(args)"), "");
        check("Revisão F5: Add account para se o Steam não fechou, antes de mexer no AutoLoginUser",
            addNew.Contains("if (!SteamClosedNow()) throw") && addNew.IndexOf("SteamClosedNow()") < addNew.IndexOf("AutoLoginUser"), "");

        // F8. Uma escrita que falha a meio põe o loginusers.vdf como estava
        var switchTo = acc[acc.IndexOf("public static async Task<long> SwitchTo(")..acc.IndexOf("public static string LoginArgs(")];
        var inner = switchTo[switchTo.IndexOf("WriteAll(file, mine);")..];
        check("Revisão F8: o catch da escrita repõe os bytes originais pelo mesmo handle e volta a lançar",
            inner.Contains("catch\n                {") && inner.Contains("try { WriteAll(file, bytes); }") && inner.IndexOf("try { WriteAll(file, bytes); }") < inner.IndexOf("TidyBackups(folder, name);"), "");

        // F10. Com o Steam já fechado à mão, as launch options entram na mesma
        var close = acc[acc.IndexOf("static async Task CloseSteam(")..acc.IndexOf("public static bool Closed =>")];
        check("Revisão F10: o CloseSteam com o Steam já fechado corre o whileClosed", close.Contains("if (SteamClosedNow()) { await WhileClosed(whileClosed); return; }")
            && !close.Contains("if (SteamClosedNow()) return;"), "");

        // F11. O ActiveUser de um Steam morto (o pid já não é de um steam.exe) não passa por outra conta
        var s11 = TimeSpan.FromSeconds(11);
        check("Revisão F11: um pid que já não é o Steam não deixa confiar no ActiveUser", !Steam.ActiveTrusted(s11, 2, 1234, _ => false), "");
        check("Revisão F11: com o pid de um steam.exe a correr, ou sem pid, como antes", Steam.ActiveTrusted(s11, 2, 1234, _ => true) && Steam.ActiveTrusted(s11, 2, null, _ => false)
            && !Steam.ActiveTrusted(TimeSpan.FromSeconds(5), 2, null, _ => true) && !Steam.ActiveTrusted(s11, 1, null, _ => true), "");
    }

    static void Kick_(Action<string, bool, string> check, Func<string[], string> src, DateTime t)
    {
        string Src(params string[] p) => src(p);
        var kick = Src("Views", "MainWindow.Kick.cs");
        var servers = Src("Views", "MainWindow.Servers.cs");

        // F6. A recusa de uma nova tentativa no servidor cheio conta outra vez; a mesma linha lida duas vezes não
        check("Revisão F6: um cheio depois de um Join novo conta outra vez", !MainWindow.SameDisconnect(KickKind.Full, t, KickKind.Full, t.AddSeconds(18), joinedAt: t.AddSeconds(15)), "");
        check("Revisão F6: o mesmo cheio lido em duas leituras sem Join pelo meio não conta duas vezes", MainWindow.SameDisconnect(KickKind.Full, t, KickKind.Full, t.AddSeconds(5), joinedAt: t.AddSeconds(-1)), "");
        check("Revisão F6: fora do cheio fica como era (só o mais forte conta nos 20 s)", MainWindow.SameDisconnect(KickKind.Kick, t, KickKind.Kick, t.AddSeconds(5), t.AddSeconds(3))
            && !MainWindow.SameDisconnect(KickKind.Kick, t, KickKind.Ban, t.AddSeconds(5), null) && !MainWindow.SameDisconnect(KickKind.Full, t, KickKind.Full, t.AddSeconds(21), null), "");
        check("Revisão F6: o OnKicked usa a regra com a hora do último Join", kick.Contains("if (SameDisconnect(_lastKickKind, _lastKickAt, kind, DateTime.Now, _s.LastServerAt))")
            && !kick.Contains("recent && Cs2Console.Stronger("), "");

        // F7. Os bots não enchem o servidor
        check("Revisão F7: 10/10 com 4 bots não está cheio", !MainWindow.IsFull(10, 4, 10) && MainWindow.IsFull(10, 0, 10) && !MainWindow.IsFull(0, 0, 0) && MainWindow.IsFull(12, 2, 10), "");
        check("Revisão F7: o Join e o Wait for a slot usam a regra, e os bots são guardados", servers.Contains("srv.Bots = info.Bots;") && servers.Contains("srv.Bots = 0;")
            && servers.Split("IsFull(s.PlayerCount, s.Bots, s.MaxPlayers)").Length == 3 && !servers.Contains("s.PlayerCount >= s.MaxPlayers"), "");

        // F9. A entrada recusada não marca ban nem pergunta pela troca
        check("Revisão F9: o toast diz que o servidor recusou sem motivo e que não ficou marcado", MainWindow.RefusedJoinToast.Contains("refused the join without saying why")
            && MainWindow.RefusedJoinToast.Contains("Not marked as a ban"), "");
        var branch = kick.IndexOf("if (kind == KickKind.Kick && d.Why == KickReason.RefusedJoin)");
        check("Revisão F9: a entrada recusada sai antes da marca de ban e da pergunta", branch > 0 && branch < kick.IndexOf("MarkServerBan(acc, inList, d.Minutes)")
            && kick[branch..kick.IndexOf("MarkServerBan(acc, inList, d.Minutes)")].Contains("Toast(RefusedJoinToast, Tone.Warn);\n            UpdateRejoinBtn();\n            return;"), "");
    }
}
