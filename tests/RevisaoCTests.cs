using System.IO;
using System.Net.Http;
using SRT;
using SRT.Core;

// Revisão completa de 6 nov. (área C: servidores e regiões): cada correção de um achado confirmado leva aqui o seu teste, que falhava antes.
static class RevisaoCTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");
        check("Revisão C: as fontes leem-se", Src("MainWindow.xaml").Length > 0, "");
        var xaml = Src("MainWindow.xaml");
        var servers = Src("Views", "MainWindow.Servers.cs");
        var regions = Src("Views", "MainWindow.Regions.cs");

        // C1: adicionar diz o que acontece no estado em que se está
        check("Revisão C1: com a proteção desligada adicionar não diz protected", !MainWindow.AddedText(2, 0, protOn: false, noPhone: false, egress: false).Contains("protected"), "");
        check("Revisão C1: proteção desligada diz que veem o IP de casa", MainWindow.AddedText(2, 0, false, false, false) == "2 servers added. Protection is off: they see your home IP until you turn it on.", MainWindow.AddedText(2, 0, false, false, false));
        check("Revisão C1: No phone diz que veem o IP de casa", MainWindow.AddedText(1, 0, true, true, false) == "1 server added. No phone mode: it sees your home IP.", MainWindow.AddedText(1, 0, true, true, false));
        check("Revisão C1: com o telemóvel ligado vão pelo telemóvel", MainWindow.AddedText(2, 1, true, false, true) == "2 servers added. They go through the phone. 1 ignored.", MainWindow.AddedText(2, 1, true, false, true));
        check("Revisão C1: proteção ligada sem telemóvel ficam bloqueados", MainWindow.AddedText(2, 0, true, false, false).Contains("blocked until the phone"), MainWindow.AddedText(2, 0, true, false, false));
        check("Revisão C1: o Add não promete o telemóvel", !xaml.Contains("routes them through the phone right away"), "");
        check("Revisão C1: No phone não oferece jogar pelo telemóvel", !MainWindow.AddJoinedText("1.2.3.4:27015", noPhone: true).Contains("through the phone"), "");
        check("Revisão C1: o toast de servidor novo sabe se é No phone", Src("Views", "MainWindow.Kick.cs").Contains("AddJoinedText(server, _s.NoPhone)"), "");

        // C2: telemóvel ligado e por verificar não é "desligado"
        var unchecked_ = new GuardReport { Candidate = new Adapter { Index = 5, LocalIp = "192.168.42.10" }, Distrust = Distrust.Unchecked, EgressProblem = DistrustText.Of(Distrust.Unchecked) };
        check("Revisão C2: telemóvel ligado e por verificar não diz disconnected", !MainWindow.NoLinkWhy(unchecked_).Contains("disconnected") && MainWindow.NoLinkWhy(unchecked_) == DistrustText.Of(Distrust.Unchecked), MainWindow.NoLinkWhy(unchecked_));
        check("Revisão C2: sem telemóvel diz que não está ligado", MainWindow.NoLinkWhy(new GuardReport()).Contains("isn't connected") && MainWindow.NoLinkWhy(null).Contains("isn't connected"), "");
        check("Revisão C2: o Join não mostra a palavra Blocked como motivo", !servers.Contains("protection is not confirmed ({s.RouteText})"), "");
        check("Revisão C2: o Join sem ligação diz o motivo real", MainWindow.JoinBlockedText("1.2.3.4:27015", unchecked_, "Blocked") == "Not joining 1.2.3.4:27015. " + DistrustText.Of(Distrust.Unchecked), MainWindow.JoinBlockedText("1.2.3.4:27015", unchecked_, "Blocked"));
        var linked = new GuardReport { Egress = new Adapter { Index = 5, LocalIp = "192.168.42.10" } };
        check("Revisão C2: o Join com ligação e rota por confirmar diz a rota", MainWindow.JoinBlockedText("X", linked, "Route failed") == "Not joining X: its route through the phone isn't confirmed (Route failed).", MainWindow.JoinBlockedText("X", linked, "Route failed"));
        check("Revisão C2: espera por vaga com a proteção desligada diz isso", MainWindow.SlotNoLinkText("X", onHome: false, protOn: false, null) == "Waiting for a slot on X: protection is off.", "");
        check("Revisão C2: espera por vaga com o telemóvel por verificar diz o motivo", MainWindow.SlotNoLinkText("X", false, true, unchecked_) == "Waiting for a slot on X. " + DistrustText.Of(Distrust.Unchecked), "");
        check("Revisão C2: Refresh e Regions usam o mesmo motivo", servers.Contains("NoLinkWhy(_last)") && regions.Contains("NoLinkWhy(_last)") && !servers.Contains("Phone disconnected: not querying") && !regions.Contains("the phone is disconnected"), "");
        check("Revisão C2: a linha de Regions com o telemóvel por verificar não manda ligá-lo", !MainWindow.NoPingSuffix(unchecked_).Contains("connect the phone") && MainWindow.NoPingSuffix(null).Contains("connect the phone"), "");

        // C3: o ping do A2S não conta a ida e volta do challenge
        {
            using var srv = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            var port = ((System.Net.IPEndPoint)srv.Client.LocalEndPoint!).Port;
            byte[] Str(string t) => System.Text.Encoding.UTF8.GetBytes(t + "\0");
            byte[] FF = { 0xFF, 0xFF, 0xFF, 0xFF };
            var info = FF.Concat(new byte[] { 0x49, 17 }).Concat(Str("Lento")).Concat(Str("de_inferno")).Concat(Str("csgo")).Concat(Str("CS2")).Concat(new byte[] { 0xDA, 0x02, 0, 10, 0 }).ToArray();
            var fake = Task.Run(async () =>
            {
                for (var i = 0; i < 2; i++)
                {
                    var r = await srv.ReceiveAsync();
                    if (r.Buffer.Length == 25) { await Task.Delay(300); await srv.SendAsync(FF.Concat(new byte[] { 0x41, 1, 2, 3, 4 }).ToArray(), r.RemoteEndPoint); }
                    else await srv.SendAsync(info, r.RemoteEndPoint);
                }
            });
            try
            {
                var q = A2S.Query("127.0.0.1", port, "127.0.0.1", 1500).GetAwaiter().GetResult();
                check("Revisão C3: o ping não conta a ida e volta do challenge", q.Name == "Lento" && q.PingMs < 200, $"{q.PingMs} ms");
            }
            catch (Exception ex) { check("Revisão C3: A2S com challenge lento", false, ex.Message); }
        }

        // C4: um nome ou um IPv6 não "já está na lista"
        check("Revisão C4: um nome não diz que já está na lista", MainWindow.NothingAdded(dupes: 0, ignored: 0) != "Those servers are already in the list." && MainWindow.NothingAdded(0, 0).Contains("No IPv4 address"), MainWindow.NothingAdded(0, 0));
        check("Revisão C4: repetidos dizem que já estão", MainWindow.NothingAdded(2, 0) == "Those servers are already in the list.", "");
        var ign = new List<string>();
        var known = new HashSet<string> { "1.2.3.4:27015" };
        var got = MainWindow.ParseServers("1.2.3.4:27015 play.example.com:27015 [2001:db8::1]:27015 5.6.7.8 5.6.7.8:27015", 27015, known, ign, out var dupes);
        check("Revisão C4: os repetidos contam-se à parte", got.SequenceEqual(new[] { "5.6.7.8:27015" }) && dupes == 2 && ign.Count == 0, $"{string.Join(",", got)} dupes={dupes} ign={ign.Count}");

        // C5: um número que não é porta não vira porta
        check("Revisão C5: '1.2.3.4 35ms' usa a porta por omissão", MainWindow.ParseServers("1.2.3.4 35ms", 27015).Single() == "1.2.3.4:27015", string.Join(",", MainWindow.ParseServers("1.2.3.4 35ms", 27015)));
        check("Revisão C5: '1.2.3.4, 12, de_dust2' usa a porta por omissão", MainWindow.ParseServers("1.2.3.4, 12, de_dust2", 27015).Single() == "1.2.3.4:27015", string.Join(",", MainWindow.ParseServers("1.2.3.4, 12, de_dust2", 27015)));
        check("Revisão C5: '1.2.3.4:27016' continua a dar a porta", MainWindow.ParseServers("1.2.3.4:27016", 27015).Single() == "1.2.3.4:27016", "");
        check("Revisão C5: listas por espaço e CSV continuam a dar a porta",
            MainWindow.ParseServers("1.2.3.4 27016", 27015).Single() == "1.2.3.4:27016" && MainWindow.ParseServers("1.2.3.4,27017,de_dust2", 27015).Single() == "1.2.3.4:27017"
            && MainWindow.ParseServers("1.2.3.4\t27018\r\n5.6.7.8;27019", 27015).SequenceEqual(new[] { "1.2.3.4:27018", "5.6.7.8:27019" }), "");
        check("Revisão C5: 'connect IP:port' e steam://connect continuam", MainWindow.ParseServers("connect 1.2.3.4:27020 steam://connect/5.6.7.8:27021", 27015).SequenceEqual(new[] { "1.2.3.4:27020", "5.6.7.8:27021" }), "");

        // C6: o que não foi perguntado agora sai da linha
        var s6 = new ServerItem { Ip = "1.2.3.4", Port = 27015, PlayersText = "12/20", PingMs = 35, PingText = "35 ms", Map = "de_mirage", PlayerCount = 12, MaxPlayers = 20, PlayersHidden = true, ServerName = "Casa" };
        MainWindow.ClearLive(s6);
        check("Revisão C6: sem pergunta, os jogadores, o mapa e o ping antigos saem", s6.PlayersText == "—" && s6.PingMs == -1 && s6.PingText == "—" && s6.Map == "—" && s6.PlayerCount == 0 && s6.MaxPlayers == 0 && !s6.HasPlayers && !s6.PlayersHidden && s6.Players == null, "");
        check("Revisão C6: o nome fica", s6.ServerName == "Casa", "");
        check("Revisão C6: o ramo sem rota e o catch limpam tudo", servers.Contains("ClearLive(srv); return false;") && servers.Contains("ClearLive(srv);\n            srv.Answered = false;"), "");
        check("Revisão C6: os não perguntados não contam como sem resposta", MainWindow.RespondedText(3, 5, 2) == "3 of 5 servers responded (2 not asked: not routed through the phone yet).", MainWindow.RespondedText(3, 5, 2));
        check("Revisão C6: sem ligação, as linhas perdem os valores antigos", servers.Contains("foreach (var srv in _servers) ClearLive(srv);"), "");

        // C7: um servidor por perguntar não é "Server not responding"
        var s7 = new ServerItem { Ip = "1.2.3.4", Port = 27015 };
        check("Revisão C7: um servidor sem nome usa o IP:porta como título", s7.Title == "1.2.3.4:27015", s7.Title);
        check("Revisão C7: a linha diz No name yet antes de perguntar", s7.RowTitle == "No name yet", s7.RowTitle);
        s7.Answered = false;
        check("Revisão C7: a linha diz Not responding depois de uma pergunta falhada", s7.RowTitle == "Not responding", s7.RowTitle);
        s7.ServerName = "Casa";
        check("Revisão C7: com nome, a linha e o título dizem o nome", s7.RowTitle == "Casa" && s7.Title == "Casa", "");
        check("Revisão C7: a linha da lista usa o RowTitle", xaml.Contains("<TextBlock Text=\"{Binding RowTitle}\" TextTrimming=\"CharacterEllipsis\" Margin=\"0,0,10,0\"/>") && !Src("Models.cs").Contains("\"Server not responding\""), "");

        // C8: tooltips que servem aos 4 estados
        foreach (var t in new[] { "always through the phone", "(through the phone, never the home network)", "measures the ping to every region through the phone",
                                  "Measured through the phone, the same path the game uses", "Servers that go through the phone", "lowest ping through the phone",
                                  "Checks this server's route first" })
            check($"Revisão C8: sem \"{t}\" (falso sem proteção e no No phone)", !xaml.Contains(t), "");

        // C9: duplo clique num botão da linha não entra no servidor
        var dbl = servers.IndexOf("void ServerList_DoubleClick");
        check("Revisão C9: duplo clique num botão da linha não entra no servidor",
            dbl > 0 && servers.IndexOf("OnRowControl(e.OriginalSource as DependencyObject, ServerList, out _)", dbl) is var i9 && i9 > 0 && i9 < servers.IndexOf("await Join(s);", dbl), "");
        check("Revisão C9: o clique simples usa a mesma regra", servers.Contains("if (OnRowControl(e.OriginalSource as DependencyObject, ServerList, out var row)) return;"), "");

        // C10: a vaga não entra sozinha com outro comando ou uma pergunta aberta
        check("Revisão C10: com outro comando a correr não entra sozinho", !MainWindow.SlotAutoJoins(autoOn: true, busy: true, dialogOpen: false), "");
        check("Revisão C10: com uma pergunta aberta não entra sozinho", !MainWindow.SlotAutoJoins(true, false, true), "");
        check("Revisão C10: livre e com a opção, entra", MainWindow.SlotAutoJoins(true, false, false) && !MainWindow.SlotAutoJoins(false, false, false), "");
        check("Revisão C10: a vaga e o servidor cheio usam a regra", servers.Contains("if (SlotAutoJoins(auto, CommandBusy, Dialog.Visibility == Visibility.Visible))")
            && Src("Views", "MainWindow.Kick.cs").Contains("SlotAutoJoins(true, CommandBusy, Dialog.Visibility == Visibility.Visible)"), "");

        // C19
        check("Revisão C19: com pesquisa o botão não diz all", MainWindow.RefreshLabel(0, filtered: true) == "Refresh shown" && MainWindow.RefreshLabel(0, false) == "Refresh all" && MainWindow.RefreshLabel(2, true) == "Refresh 2", "");
        check("Revisão C19: a pesquisa atualiza o nome do botão", servers.Contains("QueryBtn.Content = RefreshLabel(ServerList.SelectedItems.Count, !string.IsNullOrEmpty(q));"), "");

        // C20
        check("Revisão C20: uma pergunta pedida durante outra corre a seguir", servers.Contains("_queryAgain = true;") && servers.Contains("if (_queryAgain) { _queryAgain = false; await QueryServers(true); }"), "");

        // C21
        check("Revisão C21: 224.0.0.1 não é contado como rede local", !MainWindow.IsLocalNet("224.0.0.1") && MainWindow.IsLocalNet("192.168.1.10") && MainWindow.IsLocalNet("100.64.0.1") && !MainWindow.IsLocalNet("8.8.8.8"), "");
        check("Revisão C21: as regras do IP continuam iguais", !MainWindow.ValidIp("10.0.0.1") && !MainWindow.ValidIp("169.254.1.1") && MainWindow.ValidIp("100.128.0.1"), "");

    }
}
