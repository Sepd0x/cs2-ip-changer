using System.IO;
using SRT.Core;

// "--admin <ficheiro>": testes que precisam de admin (só regras e rotas de teste), ver AdminTests.cs
if (args.Length > 1 && args[0] == "--admin") Environment.Exit(await AdminTests.Run(args[1]));

// "--a2s <ip> <porta> <ip local>": pergunta a um servidor real (pelo telemóvel) e mostra os jogadores
if (args.Length > 3 && args[0] == "--a2s")
{
    var info = await A2S.Query(args[1], int.Parse(args[2]), args[3], 3000);
    Console.WriteLine($"{info.Name} · {info.Map} · {info.Players}/{info.MaxPlayers} · bots {info.Bots} · {info.PingMs} ms · lista {(info.PlayerList == null ? "nula" : info.PlayerList.Count.ToString())}");
    foreach (var pl in info.PlayerList ?? new()) Console.WriteLine($"  {pl.Name} · {pl.Score} · {pl.Time}");
    return 0;
}

// Testes só de leitura: não mexem em rotas, Firewall nem no Steam deste PC.
var fails = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "OK  " : "FALHA")} {name} {detail}");
    if (!ok) fails++;
}
// No GitHub Actions (CI=true) não há Steam, telemóvel nem IP de casa: o que depende deste PC é saltado em vez de falhar.
var ci = Environment.GetEnvironmentVariable("CI") == "true";
void Env(string name, bool ok, string detail = "")
{
    if (ci && !ok) Console.WriteLine($"SALTO {name} (depende do PC) {detail}");
    else Check(name, ok, detail);
}

Paths.Ensure();

// Durações dos cooldowns
Check("21h", Fmt.ParseDuration("21h") == TimeSpan.FromHours(21));
Check("3d 4h", Fmt.ParseDuration("3d 4h") == TimeSpan.FromHours(76));
Check("181d", Fmt.ParseDuration("181d") == TimeSpan.FromDays(181));
Check("90m", Fmt.ParseDuration("90m") == TimeSpan.FromMinutes(90));
Check("36 (horas)", Fmt.ParseDuration("36") == TimeSpan.FromHours(36));
Check("lixo", Fmt.ParseDuration("abc") == null);
Check("Fmt.Left 181d", Fmt.Left(TimeSpan.FromDays(181)) == "181 d 0 h", Fmt.Left(TimeSpan.FromDays(181)));

// VDF: ida e volta numa cópia do loginusers.vdf
var dir = Steam.InstallDir();
Env("Steam encontrado", dir != null, dir ?? "");
var lu = dir == null ? null : Path.Combine(dir, "config", "loginusers.vdf");
if (lu != null && File.Exists(lu))
{
    var text = File.ReadAllText(lu);
    var a = VdfNode.Parse(text);
    var b = VdfNode.Parse(a.Write());
    Check("VDF ida e volta", a.Write() == b.Write());
    var accs = Steam.ReadAccounts();
    Check("Contas lidas", accs.Count > 0, $"{accs.Count} conta(s)");
    foreach (var x in accs) Console.WriteLine($"     {x.SteamId}  persona={x.PersonaName}  recente={x.MostRecent}");
    Console.WriteLine("     ativa: " + (Steam.ActiveSteamId() ?? "nenhuma"));
}
var sample = VdfNode.Parse("\"users\"\n{\n\t\"765\"\n\t{\n\t\t\"AccountName\"\t\t\"a\\\"b\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n");
Check("VDF aspas escapadas", sample.Child("users").Child("765").Get("AccountName") == "a\"b");

// Perfil público (conta conhecida da Valve)
try
{
    var p = await Steam.FetchProfile("76561197960287930", null);
    Env("Perfil público", p.Persona != null, $"{p.Persona} · VAC {p.VacBans} · nível {p.Level} · limitada {p.Limited}");
}
catch (Exception ex) { Env("Perfil público", false, ex.Message); }

// Steam na mesma saída: blocos da Valve e a ligação do steam.exe (só leitura)
Check("CM de Madrid é da Valve", Net.IsValve("155.133.246.36"));
Check("CM de Londres é da Valve", Net.IsValve("162.254.196.79"));
Check("CM de Paris é da Valve", Net.IsValve("185.25.182.20"));
Check("1.1.1.1 não é da Valve", !Net.IsValve("1.1.1.1"));
Check("Servidor da lista não é da Valve", !Net.IsValve("38.3.226.237"));
Check("IP de teste do bloco", Net.ProbeIp("155.133.224.0/19") == "155.133.224.1", Net.ProbeIp("155.133.224.0/19"));
var link = Net.ReadSteamLink();
Check("Ligação do Steam lida", true, link.Connected ? $"{link.Keys.Count} ligação(ões) pela(s) placa(s) {string.Join(", ", link.LocalIps)}" : (Steam.IsRunning ? "Steam aberto mas sem ligação à Valve" : "Steam fechado"));
foreach (var k in link.Keys) Console.WriteLine("     " + k);
// A mesma leitura num programa com ligações abertas (prova que a tabela TCP é bem lida mesmo sem o Steam aberto).
var other = new[] { "firefox", "chrome", "msedge", "Code", "svchost" }.Select(p => (p, l: Net.ReadLink(p, _ => true))).FirstOrDefault(x => x.l.Connected);
Env("Tabela TCP lida", other.l != null, other.l == null ? "nenhum programa com ligações" : $"{other.p}: {other.l.Keys.Count} ligação(ões), IP local {string.Join(", ", other.l.LocalIps)}, ex. {other.l.Keys.First()}");

#if PRIVADO
// Diagnóstico: gravar e exportar para uma pasta de teste (nunca o Ambiente de trabalho). Só na build de desenvolvimento.
Diag.Write("teste", new { a = 1, texto = "ação" });
var zipDir = Path.Combine(Path.GetTempPath(), "srt-teste-zip");
Directory.CreateDirectory(zipDir);
var zipPath = Diag.Export("agora de teste", "{}", Steam.InstallDir(), null, zipDir);
using (var z = System.IO.Compression.ZipFile.OpenRead(zipPath))
{
    var names = z.Entries.Select(e => e.FullName).ToList();
    Check("Zip com o diagnóstico", names.Any(n => n.StartsWith("diag/")), string.Join(", ", names));
    Check("Zip com o registo de ligação do Steam", dir == null || names.Contains("steam/connection_log.txt"));
    var conn = z.GetEntry("steam/connection_log.txt");
    if (conn != null)
    {
        var t = new StreamReader(conn.Open()).ReadToEnd();
        Check("IP externo do Steam tapado", !System.Text.RegularExpressions.Regex.IsMatch(t, @"external address = '[0-9a-f:.]+'"));
    }
    var d = new StreamReader(z.GetEntry(names.First(n => n.StartsWith("diag/"))).Open()).ReadToEnd();
    Check("Linha do diagnóstico em JSON", d.Contains("\"k\":\"teste\"") && d.Contains("ação"), d.Trim().Split('\n').Last());
}
#endif

// Registo de ligação do Steam (cópia do ficheiro trazido do outro PC, se existir)
var sample2 = args.FirstOrDefault(a => a.EndsWith("connection_log.txt"));
if (sample2 != null)
{
    var copy = Path.Combine(Path.GetTempPath(), "srt-conn-teste.txt");
    File.Copy(sample2, copy, true);
    var w = new SteamLogWatcher();
    var first = w.Poll(copy);
    Check("1.ª leitura só acerta o estado", first.Count == 0);
    Check("Sessão lida do registo", w.Online == true, $"login {w.LastLogon:HH:mm:ss} · queda {w.LastDrop:HH:mm:ss}");
    File.AppendAllText(copy, "[2026-10-27 12:50:00] [Logged Off, 4, 0] [U:1:1] ConnectionDisconnected('I/O Operation Failed') : 'OK' (155.133.250.4:27022, WebSocket)" + "\n");
    var ev = w.Poll(copy);
    Check("Queda apanhada", ev.Count == 1 && ev[0].Kind == "dropped" && w.Online == false, string.Join(" | ", ev.Select(e => e.Kind)));
    File.AppendAllText(copy, "[2026-10-27 12:50:11] [Logged On, 4, 7] [U:1:1] RecvMsgClientLogOnResponse() : processing complete\n"
        + "[2026-10-27 12:50:12] [Logged On, 4, 7] [U:1:1] LogOff()\n"
        + "[2026-10-27 12:50:12] [Logged Off, 0, 0] [U:1:1] ConnectionDisconnected('Disconnected By Remote Host') : 'OK' (1.2.3.4:1, WebSocket)\n"
        + "[2026-10-27 12:50:12] [Logged Off, 0, 0] [U:1:1] ConnectionDisconnected() not auto reconnecting due to user initiated logoff\n");
    ev = w.Poll(copy);
    Check("Login e saída normal", string.Join(",", ev.Select(e => e.Kind)) == "online,logging-off,offline", string.Join(",", ev.Select(e => e.Kind)));
}

// Rede (só leitura)
var ads = Net.Scan();
Check("Placas lidas", ads.Count > 0, $"{ads.Count}");
foreach (var ad in ads.Where(x => x.Up)) Console.WriteLine($"     [{ad.Index}] {ad.Kind,-16} {ad.Name} · {ad.Description} · {ad.LocalIp} gw {ad.Gateway}");
var best = Net.BestInterface("1.1.1.1");
Env("Rota escolhida pelo Windows", best > 0, $"IF {best}");
Console.WriteLine("     Firewall: " + (Firewall.CheckEnabled() ?? "ligado"));
var routes = await Net.ListRoutes();
Env("Tabela de rotas lida (netsh)", routes.Count > 0, $"{routes.Count} rotas");
var ip = await Net.AnyPublicIp();
Env("IP público", ip != null, ip == null ? "" : Fmt.MaskIp(ip));
var home = await Net.HomePublicIp(best);
Env("IP de casa pela placa certa", home != null && home == ip, home == null ? "" : Fmt.MaskIp(home));
var wrong = await Net.HomePublicIp(best + 1000);
Check("Recusa medir por uma placa que não é a usada", wrong == null);
var echo = await Net.EgressPublicIp(new Adapter { Index = best, LocalIp = ads.FirstOrDefault(x => x.Index == best)?.LocalIp });
Env("Teste de IP pelo 1.0.0.1", echo == ip, echo == null ? "" : Fmt.MaskIp(echo));
var adb = Adb.Find();
Console.WriteLine("     ADB: " + (adb ?? "não encontrado"));
if (adb != null) { var st = await Adb.Status(adb); Console.WriteLine("     telemóvel: " + (st?.State ?? "?") + " " + st?.Summary); }

// Saída Wi-Fi presa ao nome da rede
var wifi = ads.FirstOrDefault(x => x.IsWifi && x.Up);
if (wifi != null)
{
    Check("Nome da rede Wi-Fi lido", wifi.Ssid != null, wifi.Ssid == null ? "" : "(" + wifi.Ssid.Length + " letras)");
    Check("Wi-Fi na rede guardada conta como saída", Net.PickEgress(ads, wifi.Id, wifi.Ssid) != null);
    var none = Net.PickEgress(ads, wifi.Id, "OutraRede", out var why);
    Check("Wi-Fi noutra rede NÃO conta como saída", none == null, why);
}
// Intervalos do kill switch
var ranges = Firewall.AllExcept("192.168.43.5");
Check("Intervalos à volta do IP local", ranges.StartsWith("0.0.0.0-192.168.43.4,192.168.43.6-255.255.255.255,::-"), ranges);

// alpha.3: verificação a sério da regra (cobertura dos intervalos, não o texto)
const string V6 = "::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
Check("Cobertura: AllExcept passa", Firewall.CoversAllExcept(ranges, "192.168.43.5"));
Check("Cobertura: IP isolado em vez de intervalo", Firewall.CoversAllExcept("0.0.0.0-192.168.43.3,192.168.43.4,192.168.43.6-255.255.255.255," + V6, "192.168.43.5"));
Check("Cobertura: com máscara /1", Firewall.CoversAllExcept("0.0.0.0/1,128.0.0.0-192.168.43.4,192.168.43.6-255.255.255.255," + V6, "192.168.43.5"));
Check("Cobertura: falta o IPv6", !Firewall.CoversAllExcept("0.0.0.0-192.168.43.4,192.168.43.6-255.255.255.255", "192.168.43.5"));
Check("Cobertura: buraco no meio", !Firewall.CoversAllExcept("0.0.0.0-10.0.0.0,10.0.0.2-192.168.43.4,192.168.43.6-255.255.255.255," + V6, "192.168.43.5"));
Check("Cobertura: IP permitido bloqueado", !Firewall.CoversAllExcept("0.0.0.0-255.255.255.255," + V6, "192.168.43.5"));
Check("Cobertura: só um IP (regra mexida à mão)", !Firewall.CoversAllExcept("10.9.9.9", "192.168.43.5"));
Check("Cobertura: IP permitido nas pontas", Firewall.CoversAllExcept(Firewall.AllExcept("0.0.0.0"), "0.0.0.0") && Firewall.CoversAllExcept(Firewall.AllExcept("255.255.255.255"), "255.255.255.255"));
Check("Cobertura: IPv6 escrito como ::/0", Firewall.CoversAllExcept("0.0.0.0-192.168.43.4,192.168.43.6-255.255.255.255,::/0", "192.168.43.5"));
Check("Regra inexistente não conta como verificada", !Firewall.Verify("CS2IPChanger teste inexistente", new[] { "203.0.113.9" }, "192.168.43.5"));

// alpha.3: o ADB só arranca se for assinado pela Google
var adbFound = Adb.Find();
Check("ADB encontrado é da Google", adbFound == null || Adb.IsGoogleSigned(adbFound), adbFound ?? "sem ADB");
Check("Ficheiro da Microsoft não passa por ADB", !Adb.IsGoogleSigned(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
var fake = Path.Combine(Path.GetTempPath(), "srt-adb-falso.exe");
File.WriteAllBytes(fake, new byte[] { 0x4D, 0x5A, 0, 0 });
Check("Ficheiro sem assinatura não passa por ADB", !Adb.IsGoogleSigned(fake));

// alpha.3: as rotas da app ficam em disco (um crash não as perde)
var owned = Paths.File("owned.json");
var ownedBefore = File.Exists(owned) ? File.ReadAllText(owned) : null;
Store.Save(owned, new Guard.OwnedState { Routes = new() { "203.0.113.9/32|99|10.0.0.1" }, MetricIf = 42, Boot = DateTime.Now.AddMilliseconds(-Environment.TickCount64) });
var g = new Guard();
var gRoutes = (HashSet<string>)typeof(Guard).GetField("_ownedRoutes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g);
var gMetric = (int)typeof(Guard).GetField("_metricAppliedTo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g);
Check("Rotas da app lidas do disco", gRoutes.Contains("203.0.113.9/32|99|10.0.0.1") && gMetric == 42);
Store.Save(owned, new Guard.OwnedState { Routes = new() { "203.0.113.9/32|99|10.0.0.1" }, MetricIf = 42, Boot = DateTime.Now.AddDays(-1) });
var g2 = (HashSet<string>)typeof(Guard).GetField("_ownedRoutes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(new Guard());
Check("Rotas de antes de reiniciar o PC ignoradas", g2.Count == 0);
if (ownedBefore == null) File.Move(owned, owned + ".teste-" + DateTime.Now.Ticks); else File.WriteAllText(owned, ownedBefore);

// Kick e ban na consola do CS2 (formato real ainda desconhecido: linhas plausíveis do Source 2)
foreach (var l in new[] {
    "Disconnect: NETWORK_DISCONNECT_BANADDED",
    "[Networking] Disconnected from server: NETWORK_DISCONNECT_KICKBANADDED",
    "Connection rejected by server: NETWORK_DISCONNECT_REJECT_BANNED",
    "Disconnect: You have been banned from this server.",
    "You have been permanently banned from this server",
    "Server rejected connection: banned (permanent)",
    "CL:  Disconnected: Banned by admin (cheating)" })
    Check("Ban: " + l, Cs2Console.Classify(l) == KickKind.Ban, Cs2Console.Classify(l).ToString());
foreach (var l in new[] {
    "Disconnect: Kicked by Console.",
    "[Client] Disconnect reason: NETWORK_DISCONNECT_KICKED (39)",
    "Disconnected from server: NETWORK_DISCONNECT_KICKED_VOTEDOFF",
    "Steam Net connection #2876 UDP steamid:85568392932 closed by peer, reason 2039: NETWORK_DISCONNECT_KICKED_IDLE",
    "Disconnect: You have been kicked from the server: AFK too long",
    "[Client] Disconnecting from server: Kicked by admin" })
    Check("Kick: " + l, Cs2Console.Classify(l) == KickKind.Kick, Cs2Console.Classify(l).ToString());
foreach (var l in new[] {
    "Disconnect: NETWORK_DISCONNECT_DISCONNECT_BY_USER",
    "Disconnecting from server: NETWORK_DISCONNECT_EXITING",
    "Disconnect: Server shutting down",
    "Disconnect: NETWORK_DISCONNECT_CHANGELEVEL",
    "Disconnect: NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN",
    "[ALL] Player1: kick him, he got banned yesterday lol",
    "[ALL] someone: disconnect and get banned",
    "*DEAD* [CT] Bob: disconnected kick",
    "Player Bob left the game (Kicked by Console)",
    "] kick bob",
    "Vote to kick Bob passed",
    "[SM] Banned player \"Bob\" (minutes: 30)",
    "Connected to 203.0.113.10:27015",
    "" })
    Check("Não é kick: " + (l == "" ? "(vazia)" : l), Cs2Console.Classify(l) == KickKind.None, Cs2Console.Classify(l).ToString());
Check("Saída normal fica como amostra", Cs2Console.IsDisconnect("Disconnect: NETWORK_DISCONNECT_DISCONNECT_BY_USER"));
Check("Chat não fica como amostra", !Cs2Console.IsDisconnect("[ALL] Bob: disconnect lol"));
Check("Caminho do console.log", Cs2Console.LogPath(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe")
    == @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log");
Check("Sem cs2.exe, sem caminho", Cs2Console.LogPath(null) == null);

// Leitura do console.log aos bocados
var clog = Path.Combine(Path.GetTempPath(), "srt-teste-console-" + Environment.ProcessId + ".log");
File.WriteAllText(clog, "antiga 1\nantiga 2\n");
var tail = new ConsoleTail();
Check("Tail: a 1.ª leitura salta o que já lá estava", tail.ReadNew(clog).Count == 0);
File.AppendAllText(clog, "nova 1\r\nnova 2\nmeia");
var t1 = tail.ReadNew(clog);
Check("Tail: só linhas completas", t1.SequenceEqual(new[] { "nova 1", "nova 2" }), string.Join("|", t1));
File.AppendAllText(clog, " linha\n");
var t2 = tail.ReadNew(clog);
Check("Tail: a linha a meio chega inteira", t2.SequenceEqual(new[] { "meia linha" }), string.Join("|", t2));
File.WriteAllText(clog, "recomeço\n");
var t3 = tail.ReadNew(clog);
Check("Tail: ficheiro recomeçado lê-se do início", t3.SequenceEqual(new[] { "recomeço" }), string.Join("|", t3));
Check("Tail: sem ficheiro, nada", tail.ReadNew(clog + ".nao").Count == 0);

// /16 depois de trocar o IP
Check("/16 de um IPv4", IpRange.Slash16("198.51.100.23") == "198.51");
Check("/16 de IPv6 ou lixo é nulo", IpRange.Slash16("2001:db8::1") == null && IpRange.Slash16("lixo") == null && IpRange.Slash16(null) == null);
Check("Mesmo /16", IpRange.Same16("198.51.100.23", "198.51.7.200"));
Check("Outro /16", !IpRange.Same16("198.51.100.23", "198.52.100.23") && !IpRange.Same16("198.51.100.23", null));
Check("Gama nova face ao histórico", IpRange.FirstSame16("203.0.113.5", new[] { "198.51.100.23", "192.0.2.9" }) == null);
Check("Gama repetida face ao histórico", IpRange.FirstSame16("198.51.3.4", new[] { "192.0.2.9", "198.51.100.23" }) == "198.51.100.23");
Check("Histórico vazio = gama nova", IpRange.FirstSame16("198.51.3.4", Array.Empty<string>()) == null);

// Versões (SemVer) e o ficheiro .sha256
Check("alpha.10 > alpha.9", Updater.Compare("0.1.0-alpha.10", "0.1.0-alpha.9") > 0);
Check("v e +dev ignorados", Updater.Compare("v0.1.0-alpha.3", "0.1.0-alpha.3+dev") == 0);
Check("Final > pré-release", Updater.Compare("0.1.0", "0.1.0-alpha.10") > 0);
Check("0.2.0-alpha.1 > 0.1.9", Updater.Compare("0.2.0-alpha.1", "0.1.9") > 0);
Check("1.10.0 > 1.9.0", Updater.Compare("1.10.0", "1.9.0") > 0);
Check("alpha < beta", Updater.Compare("0.1.0-alpha.5", "0.1.0-beta.1") < 0);
Check("alpha < alpha.1", Updater.Compare("0.1.0-alpha", "0.1.0-alpha.1") < 0);
Check("Número < texto", Updater.Compare("1.0.0-1", "1.0.0-alpha") < 0);
Check("Versão inválida é a mais antiga", Updater.Compare("lixo", "0.0.1") < 0 && !Updater.IsNewer("lixo", "0.1.0"));
Check("alpha.4 é nova para alpha.3+dev", Updater.IsNewer("v0.1.0-alpha.4", "0.1.0-alpha.3+dev"));
Check("A mesma versão não é nova", !Updater.IsNewer("0.1.0-alpha.3", "0.1.0-alpha.3+dev"));
Check("Números enormes não rebentam", Updater.Compare("0.1.0-alpha.99999999999999999999999999999999", "0.1.0-alpha.9") > 0);

const string H = "6c8658ebc2d8b898e0c79355b405809752bc937d21f6fa3c740f0605186b1747";
Check(".sha256 normal", Updater.ParseSha256(H + "  CS2IPChanger.exe\n") == H);
Check(".sha256 em maiúsculas e com *", Updater.ParseSha256(H.ToUpperInvariant() + " *CS2IPChanger.exe\r\n") == H);
Check(".sha256 só com o hash", Updater.ParseSha256(H) == H);
Check(".sha256 de outro ficheiro", Updater.ParseSha256(H + "  outro.exe") == null);
Check(".sha256 com várias linhas", Updater.ParseSha256(new string('a', 64) + "  outro.zip\n" + H + "  CS2IPChanger.exe\n") == H);
Check(".sha256 curto ou lixo", Updater.ParseSha256(H[..63] + "  CS2IPChanger.exe") == null && Updater.ParseSha256("") == null && Updater.ParseSha256(null) == null);

var relJson = """
[
  {"tag_name":"v0.1.0-alpha.11","draft":true,"html_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-alpha.11","assets":[]},
  {"tag_name":"v0.1.0-alpha.9","draft":false,"prerelease":true,"html_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-alpha.9",
   "assets":[{"name":"CS2IPChanger.exe","browser_download_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/download/v0.1.0-alpha.9/CS2IPChanger.exe"}]},
  {"tag_name":"v0.1.0-alpha.10","draft":false,"prerelease":true,"html_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-alpha.10",
   "assets":[{"name":"CS2IPChanger.exe","browser_download_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/download/v0.1.0-alpha.10/CS2IPChanger.exe"},
             {"name":"CS2IPChanger.exe.sha256","browser_download_url":"https://evil.example/CS2IPChanger.exe.sha256"}]},
  {"tag_name":"nightly","draft":false,"assets":[]}
]
""";
var rel = Updater.PickLatest(relJson);
Check("Release mais alta (sem rascunhos)", rel?.Version == "0.1.0-alpha.10", rel?.Version);
Check("Exe do próprio repo aceite", rel?.ExeUrl == "https://github.com/Sepd0x/cs2-ip-changer/releases/download/v0.1.0-alpha.10/CS2IPChanger.exe");
Check("Link de fora do repo ignorado", rel?.ShaUrl == null);
Check("Página da release", rel?.PageUrl == "https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-alpha.10");
Check("Lista vazia", Updater.PickLatest("[]") == null);

// Linhas reais da consola do CS2 (teste de 27 out.)
Check("Real: ban KICKBANADDED (Netchan)", Cs2Console.Classify("10/27 22:49:25 [Networking] Netchan 'server' closed unexpectedly: NETWORK_DISCONNECT_KICKBANADDED") == KickKind.Ban);
Check("Real: ban KICKBANADDED (fase de ligação)", Cs2Console.Classify("10/27 22:49:25 [Client] Disconnection during connection phase. Sign-on state: 5 (SIGNONSTATE_SPAWN). Disconnect reason: NETWORK_DISCONNECT_KICKBANADDED.") == KickKind.Ban);
// beta.3: o timeout deixou de ser "nada": é "sem resposta" (oferece novo IP), nunca kick nem ban
Check("Real: timeout é sem resposta, não kick", Cs2Console.Classify("10/27 22:22:28 [Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT") == KickKind.NoAnswer);
Check("Real: sair à mão não é kick", Cs2Console.Classify("10/27 22:46:58 [Client] Disconnected from server: NETWORK_DISCONNECT_DISCONNECT_BY_USER") == KickKind.None);
Check("Real: mapa local a fechar não é kick", Cs2Console.Classify("10/27 22:18:20 [Server] Disconnect client 'Acc1' from server: NETWORK_DISCONNECT_SHUTDOWN (Server shutdown)") == KickKind.None && Cs2Console.Classify("10/27 22:18:20 [Client] Disconnecting from server: NETWORK_DISCONNECT_LOOPDEACTIVATE") == KickKind.None);
Check("Real: HOSTSTATE_IDLE não é kick", Cs2Console.Classify("10/27 22:46:27 [Client] Disconnecting from server: NETWORK_DISCONNECT_REQUEST_HOSTSTATE_IDLE") == KickKind.None);
Check("Real: servidor lido do Connected to", Cs2Console.ConnectedServer("10/27 22:49:23 [Client] CL:  Connected to '38.3.226.244:60004'") == "38.3.226.244:60004" && Cs2Console.ConnectedServer("[Client] CL:  Connected to 'loopback:1'") == null);

// alpha.4: marcas de jogadores (o A2S só dá nomes)
Check("Mesmo nome, maiúsculas e espaços", PlayerMatch.SameName("  Kestrel   x ", "kestrel x"));
Check("Nomes diferentes", !PlayerMatch.SameName("kestrel", "kestrel2") && !PlayerMatch.SameName("", "") && !PlayerMatch.SameName(null, "a"));
Check("SteamID64", PlayerMatch.ParseProfile(" 76561197960287930 ") == ("76561197960287930", null));
Check("Link /profiles/", PlayerMatch.ParseProfile("https://steamcommunity.com/profiles/76561197960287930/") == ("76561197960287930", null));
Check("Link /id/", PlayerMatch.ParseProfile("steamcommunity.com/id/gabelogannewell") == (null, "gabelogannewell"));
Check("Nome de URL sozinho", PlayerMatch.ParseProfile("gabelogannewell") == (null, "gabelogannewell"));
Check("Lixo não é perfil", PlayerMatch.ParseProfile("https://example.com/x") == (null, null) && PlayerMatch.ParseProfile("") == (null, null) && PlayerMatch.ParseProfile("12345") == (null, null));
Check("Servidor do perfil", PlayerMatch.ServerKey("203.0.113.10:27015") == "203.0.113.10:27015" && PlayerMatch.ServerKey("[A:1:2:3]") == null && PlayerMatch.ServerKey(null) == null);
var flag = new SRT.PlayerFlag { Name = "kestrel", Persona = "kestrel_new" };
Check("Marca pelo nome antigo e pelo atual", flag.Matches("Kestrel") && flag.Matches("kestrel_new") && !flag.Matches("orbit"));
try
{
    Env("Link /id/ resolvido sem chave", await Steam.ResolveProfile("https://steamcommunity.com/id/gabelogannewell", null) == "76561197960287930");
    var sum = await Steam.FetchSummaries(new[] { "76561197960287930" }, null);
    Env("Nome atual do perfil sem chave", sum.TryGetValue("76561197960287930", out var x) && !string.IsNullOrWhiteSpace(x.Persona), x?.Persona ?? "");
}
catch (Exception ex) { Env("Perfis por link", false, ex.Message); }

// alpha.4: um ban numa rede conta nos servidores todos dessa rede
Check("Etiqueta WINDOH", ServerNetwork.Tag("[PT] WINDOH.GG • Competitivo #5 • [ONLY MIRAGE]") == "WINDOH.GG", ServerNetwork.Tag("[PT] WINDOH.GG • Competitivo #5 • [ONLY MIRAGE]") ?? "null");
Check("Etiqueta Tuga Army", ServerNetwork.Tag("TUGA ARMY | Retakes #2") == "TUGA ARMY" && ServerNetwork.Tag("[PT] Tuga Army 3 - AWP") == "TUGA ARMY");
Check("Sem etiqueta clara", ServerNetwork.Tag("#1") == null && ServerNetwork.Tag(null) == null && ServerNetwork.Tag("") == null);
Check("Mesma rede pelo IP (nomes ainda desconhecidos)", ServerNetwork.Same("38.3.226.237:27005", null, null, "38.3.226.237:27001", null, null));
Check("Mesma rede pelo nome", ServerNetwork.Same("38.3.226.237:27005", "[PT] WINDOH.GG • Competitivo #5", null, "38.3.226.244:60004", "[PT] WINDOH.GG • Retakes #1", null));
Check("Redes diferentes", !ServerNetwork.Same("38.3.226.237:27005", "[PT] WINDOH.GG • Competitivo #5", null, "203.0.113.10:27015", "TUGA ARMY | Retakes", null));

// beta.1: redes de servidores sem falsos positivos (ponto 8)
Check("Etiqueta genérica 'Mirage 24/7 | FastDL' dá null", ServerNetwork.Tag("Mirage 24/7 | FastDL") == null, ServerNetwork.Tag("Mirage 24/7 | FastDL") ?? "null");
Check("Etiquetas genéricas: Retakes, AWP DM", ServerNetwork.Tag("Retakes #3") == null && ServerNetwork.Tag("AWP DM | 128 tick") == null);
Check("Etiqueta com 24/7 no fim", ServerNetwork.Tag("TUGA ARMY 24/7 | Mirage") == "TUGA ARMY", ServerNetwork.Tag("TUGA ARMY 24/7 | Mirage") ?? "null");
Check("Redes explícitas iguais contam", ServerNetwork.Same("203.0.113.10:27015", "Retakes #1", "Windoh", "198.51.100.7:27015", "Mirage 24/7", "WINDOH"));
Check("Redes explícitas diferentes não contam", !ServerNetwork.Same("203.0.113.10:27015", "[PT] WINDOH.GG • Retakes", "Windoh", "203.0.113.10:27016", "[PT] WINDOH.GG • DM", "Outra"));
Check("IP igual com nomes conhecidos e etiquetas diferentes não conta", !ServerNetwork.Same("203.0.113.10:27015", "TUGA ARMY | Retakes", null, "203.0.113.10:27016", "WINDOH.GG • DM", null));
Check("IP igual com nomes genéricos conhecidos não conta", !ServerNetwork.Same("203.0.113.10:27015", "Retakes #1", null, "203.0.113.10:27016", "Retakes #2", null));
Check("Rede à mão contra etiqueta do nome", ServerNetwork.Same("203.0.113.10:27015", "Retakes #1", "windoh.gg", "198.51.100.7:27015", "[PT] WINDOH.GG • Competitivo #5", null));

// beta.1: hotkeys sem Ctrl+Alt (AltGr) e configuráveis (ponto 2)
Check("Hotkey Ctrl+Shift+F9 lida", Hotkey.TryParse("Ctrl+Shift+F9", out var hm, out var hv) && hm == (Hotkey.MOD_CONTROL | Hotkey.MOD_SHIFT) && hv == 0x78, $"mods {hm} vk 0x{hv:X}");
Check("Hotkey Ctrl+Alt+Q recusada (AltGr)", !Hotkey.TryParse("Ctrl+Alt+Q", out _, out _));
Check("Hotkey F9 sozinha recusada", !Hotkey.TryParse("F9", out _, out _));
Check("Hotkey Shift+Q recusada, Shift+F10 aceite", !Hotkey.TryParse("Shift+Q", out _, out _) && Hotkey.TryParse("Shift+F10", out _, out _));
Check("Hotkey: lixo recusado", !Hotkey.TryParse("", out _, out _) && !Hotkey.TryParse(null, out _, out _) && !Hotkey.TryParse("Ctrl+Banana", out _, out _) && !Hotkey.TryParse("Ctrl+Ctrl+F9", out _, out _));
var hf = Hotkey.Format(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.F10);
Check("Hotkey: Format e TryParse dão a volta", hf == "Ctrl+Shift+F10" && Hotkey.TryParse(hf, out _, out _), hf);
Check("Hotkeys por defeito válidas", Hotkey.TryParse(SRT.Settings.DefaultHotkeySwitch, out _, out _) && Hotkey.TryParse(SRT.Settings.DefaultHotkeyRejoin, out _, out _));

// beta.1: VAC e Steam não são kick (ponto 10)
Check("VAC: KICKED_VACNETABNORMALBEHAVIOR", Cs2Console.Classify("10/29 21:03:11 [Client] Disconnected from server: NETWORK_DISCONNECT_KICKED_VACNETABNORMALBEHAVIOR") == KickKind.Steam,
    Cs2Console.Classify("10/29 21:03:11 [Client] Disconnected from server: NETWORK_DISCONNECT_KICKED_VACNETABNORMALBEHAVIOR").ToString());
Check("VAC: VACBANNED", Cs2Console.Classify("[Networking] Netchan 'server' closed unexpectedly: NETWORK_DISCONNECT_KICKED_VACBANNED") == KickKind.Steam
    && Cs2Console.Classify("Disconnect: NETWORK_DISCONNECT_VACBANNED") == KickKind.Steam);
// beta.3: STEAM_BANNED é a lista de bans do servidor (caso real de 28 out., 38.3.226.237, depois de um KICKBANADDED na mesma comunidade)
Check("Servidor: STEAM_BANNED é ban do servidor", Cs2Console.Classify("10/28 15:53:27 [Client] Disconnected from server: NETWORK_DISCONNECT_STEAM_BANNED") == KickKind.Ban);
Check("Steam: STEAM_LOGON continua a ser do Steam", Cs2Console.Classify("Disconnect reason: NETWORK_DISCONNECT_STEAM_AUTHINVALID") == KickKind.Steam);
Check("No Steam logon continua ignorado", Cs2Console.Classify("Disconnect: NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN") == KickKind.None
    && Cs2Console.Classify("Disconnect: NETWORK_DISCONNECT_STEAM_LOGON") == KickKind.None);
Check("Ban do servidor continua ban", Cs2Console.Classify("Disconnect: NETWORK_DISCONNECT_KICKBANADDED") == KickKind.Ban);

// beta.1: loginusers.vdf só é reescrito se der a volta igual (ponto 17)
if (lu != null && File.Exists(lu)) Check("VDF RoundTrips no loginusers real", VdfNode.RoundTrips(File.ReadAllText(lu)));
Check("VDF RoundTrips num texto normal", VdfNode.RoundTrips("\"users\"\n{\n\t\"765\"\n\t{\n\t\t\"AccountName\"\t\t\"a\\\"b\"\n\t}\n}\n"));
Check("VDF RoundTrips falso com #include", !VdfNode.RoundTrips("#include \"outro.vdf\"\n\"users\"\n{\n}\n"));
Check("VDF RoundTrips falso com condição [$WIN32]", !VdfNode.RoundTrips("\"users\"\n{\n\t\"a\"\t\"1\" [$WIN32]\n}\n"));
Check("VDF RoundTrips falso com chaveta a mais", !VdfNode.RoundTrips("\"users\"\n{\n}\n}\n\"outro\"\t\"1\"\n"));
Check("VDF RoundTrips falso com escape que se perderia", !VdfNode.RoundTrips("\"users\"\n{\n\t\"PersonaName\"\t\"a\\nb\"\n}\n"));

// beta.1: docks USB não passam por telemóvel (ponto 12)
Check("Dock ASIX não é telemóvel", !Net.LooksLikePhone("ASIX USB 3.0 to Gigabit Ethernet Adapter") && Net.LooksLikeUsbNet("ASIX USB 3.0 to Gigabit Ethernet Adapter"));
Check("Realtek USB GbE é rede USB", !Net.LooksLikePhone("Realtek USB GbE Family Controller") && Net.LooksLikeUsbNet("Realtek USB GbE Family Controller"));
Check("RNDIS do Android é telemóvel", Net.LooksLikePhone("Remote NDIS based Internet Sharing Device") && !Net.LooksLikeUsbNet("Remote NDIS based Internet Sharing Device"));
Check("iPhone é telemóvel", Net.LooksLikePhone("Apple Mobile Device Ethernet"));
Check("Placa de rede normal não é nenhum", !Net.LooksLikePhone("Intel(R) Ethernet Connection (7) I219-V") && !Net.LooksLikeUsbNet("Intel(R) Ethernet Connection (7) I219-V"));
var dock = new Adapter { Index = 7, Name = "Ethernet 3", Description = "ASIX USB 3.0 to Gigabit Ethernet Adapter", Id = "{dock}", Up = true, LocalIp = "192.168.1.20", Gateway = "192.168.1.1", IsUsbNet = true };
Check("Automático não escolhe o dock", Net.PickEgress(new List<Adapter> { dock }, null, null) == null && dock.Kind == "USB network");
Check("Dock escolhido à mão conta", Net.PickEgress(new List<Adapter> { dock }, "{dock}", null) == dock);

// beta.1: regra de IPv6 do modo Whole PC (ponto 3), só leitura
Check("IPv6 todo coberto (intervalo)", Firewall.CoversAllIpv6(V6) && Firewall.CoversAllIpv6("10.0.0.1," + V6));
Check("IPv6 todo coberto (::/0)", Firewall.CoversAllIpv6("::/0"));
Check("IPv6 parcial não cobre", !Firewall.CoversAllIpv6("fe80::-fe80::ffff") && !Firewall.CoversAllIpv6("*") && !Firewall.CoversAllIpv6(null));

// beta.1: tamanho do exe na release (ponto 7)
var relSized = Updater.PickLatest("""
[{"tag_name":"v0.1.0-beta.1","draft":false,"html_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-beta.1",
  "assets":[{"name":"CS2IPChanger.exe","size":69206016,"browser_download_url":"https://github.com/Sepd0x/cs2-ip-changer/releases/download/v0.1.0-beta.1/CS2IPChanger.exe"}]}]
""");
Check("Tamanho do exe lido da release", relSized?.Size == 69206016, relSized?.Size.ToString());

// beta.1: Export diagnostics da build pública, sem chave nem IP de casa (ponto 19)
var rDir = Path.Combine(Path.GetTempPath(), "srt-teste-report");
Directory.CreateDirectory(rDir);
var rZip = Report.Export("agora: casa 198.51.100.77 e outro 198.51.100.770", "{\"x\":1}", Steam.InstallDir(), null, new[] { "198.51.100.77" }, rDir);
using (var z = System.IO.Compression.ZipFile.OpenRead(rZip))
{
    var names = z.Entries.Select(e => e.FullName).ToList();
    Check("Report: now.txt e settings.json", names.Contains("now.txt") && names.Contains("settings.json"), string.Join(", ", names));
    var nowText = new StreamReader(z.GetEntry("now.txt").Open()).ReadToEnd();
    Check("Report: IP de casa tapado (e só ele)", !nowText.Contains("198.51.100.77 ") && nowText.Contains("home-ip") && nowText.Contains("198.51.100.770"), nowText);
    var conn = z.GetEntry("steam/connection_log.txt");
    if (conn != null) Check("Report: IP externo do Steam tapado", !System.Text.RegularExpressions.Regex.IsMatch(new StreamReader(conn.Open()).ReadToEnd(), @"external address = '[0-9a-f:.]+'"));
}

// beta.1: o ipwho.is responde por HTTPS (precisa de rede)
try
{
    var info = await Net.Lookup("1.1.1.1");
    Env("Operadora por HTTPS (ipwho.is)", info?.Isp != null, info?.Short ?? "sem resposta");
}
catch (Exception ex) { Env("Operadora por HTTPS (ipwho.is)", false, ex.Message); }

// alpha.4: Open with CS2 só aceita .exe que existem, nunca o CS2, o Steam nem a app
var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
Env("Notepad pode abrir", ShellLaunch.IsLaunchable(notepad, out _));
Check("Não .exe", !ShellLaunch.IsLaunchable(Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts"), out var w1), w1);
Check("Ficheiro que não existe", !ShellLaunch.IsLaunchable(@"C:\nao\existe.exe", out var w2), w2);
Check("Caminho relativo", !ShellLaunch.IsLaunchable("notepad.exe", out var w3), w3);
Check("Nunca o CS2 nem o Steam", !ShellLaunch.IsLaunchable(Path.Combine(Path.GetTempPath(), "cs2.exe"), out _) || !File.Exists(Path.Combine(Path.GetTempPath(), "cs2.exe")));
Check("Open recusa o que não existe", ShellLaunch.Open(@"C:\nao\existe.exe") != null);
// "--shell": abre mesmo o Bloco de Notas pela shell (só à mão, abre uma janela)
if (args.Contains("--shell")) Check("Aberto pela shell", ShellLaunch.Open(notepad) == null);

// alpha.4: sons dos comandos (WAV da Kenney embutidos no exe)
foreach (var k in Enum.GetValues<Tones.Kind>())
{
    var wav = Tones.Make(k);
    var ok = wav.Length > 44 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(wav, 8, 4) == "WAVE"
        && BitConverter.ToInt16(wav, 20) == 1 && BitConverter.ToInt16(wav, 22) == 1 && BitConverter.ToInt32(wav, 24) == 44100 && BitConverter.ToInt16(wav, 34) == 16;
    Check($"Som {k}: WAV PCM 16-bit mono 44.1 kHz", ok, $"{wav.Length} bytes");
}
// "--sons": toca os 5 sons (só à mão)
if (args.Contains("--sons")) foreach (var k in Enum.GetValues<Tones.Kind>()) { Tones.Play(k); Thread.Sleep(900); }

// beta.2: A2S com servidor falso no loopback: desafio, uma resposta repetida que chega atrasada à pergunta seguinte, lista partida em 2 pacotes
{
    using var srv = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
    var port = ((System.Net.IPEndPoint)srv.Client.LocalEndPoint!).Port;
    byte[] Pkt(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    byte[] Str(string t) => System.Text.Encoding.UTF8.GetBytes(t + "\0");
    byte[] FF = { 0xFF, 0xFF, 0xFF, 0xFF };
    var info = Pkt(FF, new byte[] { 0x49, 17 }, Str("HvH teste"), Str("de_mirage"), Str("csgo"), Str("CS2"), new byte[] { 0xDA, 0x02, 3, 32, 0 });
    var players = new List<byte>(FF) { 0x44, 3 };
    foreach (var (n, sc) in new[] { ("alfa", 10), ("bravo com um nome comprido", 30), ("charlie", 20) })
    { players.Add(0); players.AddRange(Str(n)); players.AddRange(BitConverter.GetBytes(sc)); players.AddRange(BitConverter.GetBytes(60f)); }
    var half = players.Count / 2;
    byte[] Part(int number, IEnumerable<byte> payload) =>
        Pkt(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF }, BitConverter.GetBytes(7), new byte[] { 2, (byte)number }, BitConverter.GetBytes((short)1248), payload.ToArray());
    var fakeA2s = Task.Run(async () =>
    {
        for (var i = 0; i < 4; i++)
        {
            var r = await srv.ReceiveAsync();
            var b = r.Buffer;
            if (b[4] == 0x54 && b.Length == 25) await srv.SendAsync(Pkt(FF, new byte[] { 0x41, 1, 2, 3, 4 }), r.RemoteEndPoint);
            else if (b[4] == 0x54) { await srv.SendAsync(info, r.RemoteEndPoint); await srv.SendAsync(info, r.RemoteEndPoint); }
            else if (b[4] == 0x55 && b.Skip(5).SequenceEqual(FF)) await srv.SendAsync(Pkt(FF, new byte[] { 0x41, 5, 6, 7, 8 }), r.RemoteEndPoint);
            else if (b[4] == 0x55) { await srv.SendAsync(Part(1, players.Skip(half)), r.RemoteEndPoint); await srv.SendAsync(Part(0, players.Take(half)), r.RemoteEndPoint); }
        }
    });
    try
    {
        var q = await A2S.Query("127.0.0.1", port, "127.0.0.1", 1500);
        Check("A2S: info depois do desafio", q.Name == "HvH teste" && q.Players == 3 && q.MaxPlayers == 32, $"{q.Name} {q.Players}/{q.MaxPlayers}");
        Check("A2S: lista partida em 2 pacotes, fora de ordem, com uma resposta atrasada pelo meio", q.PlayerList?.Count == 3 && q.PlayerList[0].Name == "bravo com um nome comprido",
            q.PlayerList == null ? "nula" : string.Join(", ", q.PlayerList.Select(x => x.Name)));
    }
    catch (Exception ex) { Check("A2S com servidor falso", false, ex.Message); }
    Check("A2S: pacote partido comprimido é recusado",
        A2S.Split(Pkt(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF }, BitConverter.GetBytes(unchecked((int)0x80000007)), new byte[] { 2, 0, 0, 0, 1 })) == null);
}

// beta.3: qualidade da ligação do telemóvel (níveis e a regra das duas medições)
{
    LinkQuality Q(int avg, int jit, int lost = 0, string net = "4G") => new(8, lost, avg, jit, net);
    Check("Qualidade: boa", Q(45, 6).Level == QualityLevel.Good);
    Check("Qualidade: razoável por jitter", Q(45, 25).Level == QualityLevel.Fair);
    Check("Qualidade: razoável com 1 perda", Q(45, 6, 1).Level == QualityLevel.Fair);
    Check("Qualidade: fraca por ping", Q(160, 6).Level == QualityLevel.Bad);
    Check("Qualidade: fraca por perdas (2 em 8)", Q(45, 6, 2).Level == QualityLevel.Bad);
    Check("Qualidade: fraca em 3G mesmo com bom ping", Q(45, 6, 0, "3G").Level == QualityLevel.Bad);
    Check("Qualidade: sem resposta é fraca", new LinkQuality(8, 8, 0, 0, "4G").Level == QualityLevel.Bad);
    Check("Regra: uma má sozinha não chega", Quality.Settled(new[] { Q(45, 6), Q(200, 60) }, QualityLevel.Good) == QualityLevel.Good);
    Check("Regra: duas más seguidas é fraca", Quality.Settled(new[] { Q(200, 60), Q(200, 60) }, QualityLevel.Good) == QualityLevel.Bad);
    Check("Regra: sai de fraca só com duas boas", Quality.Settled(new[] { Q(200, 60), Q(45, 6) }, QualityLevel.Bad) == QualityLevel.Bad
        && Quality.Settled(new[] { Q(45, 6), Q(45, 6) }, QualityLevel.Bad) == QualityLevel.Good);
    Check("Regra: 1.ª medição má não decide", Quality.Settled(new[] { Q(200, 60) }, QualityLevel.Unknown) == QualityLevel.Unknown);
}

// beta.3: kicks e bans que ficavam por marcar, e o servidor que não responde
Check("Timeout: TIMEDOUT é sem resposta", Cs2Console.Classify("10/28 17:02:11 [Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT") == KickKind.NoAnswer);
Check("Timeout: pedido de ligação sem resposta", Cs2Console.Classify("Disconnect reason: NETWORK_DISCONNECT_CONNECT_REQUEST_TIMEDOUT") == KickKind.NoAnswer);
Check("Timeout: 'Connection failed after 4 retries'", Cs2Console.Classify("Connection failed after 4 retries.") == KickKind.NoAnswer);
Check("Sair pelo menu não é nada", Cs2Console.Classify("[Client] Disconnected from server: NETWORK_DISCONNECT_DISCONNECT_BY_USER") == KickKind.None);
Check("Mudança de mapa não é nada", Cs2Console.Classify("[Client] Disconnecting from server: NETWORK_DISCONNECT_LOOPDEACTIVATE") == KickKind.None);
Check("Sign-on lido da linha real", Cs2Console.SignOnState("10/28 14:56:43 [Client] Disconnection during connection phase. Sign-on state: 6 (SIGNONSTATE_FULL). Disconnect reason: NETWORK_DISCONNECT_KICKBANADDED.") == 6
    && Cs2Console.SignOnState("[Client] Disconnected from server: NETWORK_DISCONNECT_KICKED") == null);
Check("O mais forte: ban ganha a kick", Cs2Console.Stronger(KickKind.Kick, KickKind.Ban) == KickKind.Ban && Cs2Console.Stronger(KickKind.Ban, KickKind.Kick) == KickKind.Ban);
Check("O mais forte: kick ganha a timeout", Cs2Console.Stronger(KickKind.NoAnswer, KickKind.Kick) == KickKind.Kick);

// beta.3 (revisão): o servidor atual muda também para o menu e relays; "No Steam logon" é dito; queda e login no mesmo segundo
Check("Ligação ao menu é linha de ligação", Cs2Console.IsConnectLine("10/28 16:26:59 [Client] CL:  Connected to 'loopback:1'") && Cs2Console.ConnectedServer("[Client] CL:  Connected to 'loopback:1'") == null);
Check("Ligação a relay da Valve é linha de ligação", Cs2Console.IsConnectLine("[Client] CL:  Connected to '=[A:1:123456:7890]:0'"));
Check("Ligação a servidor dá o IP", Cs2Console.ConnectedServer("10/28 15:54:33 [Client] CL:  Connected to '38.3.226.237:27010'") == "38.3.226.237:27010");
Check("No Steam logon é reconhecido", Cs2Console.IsNoSteamLogon("Disconnect: NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN") && Cs2Console.IsNoSteamLogon("[Client] Disconnected from server: NETWORK_DISCONNECT_STEAM_LOGON"));
Check("No Steam logon no chat não conta", !Cs2Console.IsNoSteamLogon("[ALL] Bob: no steam logon again lol"));
{
    var f = Path.Combine(Path.GetTempPath(), $"srt-steamlog-{Guid.NewGuid():N}.txt");
    File.WriteAllText(f, "[2026-10-28 15:00:00] [0,0] RecvMsgClientLogOnResponse() : processing complete" + "\n");
    var w = new SteamLogWatcher();
    w.Poll(f);
    File.AppendAllText(f, "[2026-10-28 15:53:50] [0,0] ConnectionDisconnected('I/O Operation Failed')" + "\n"
        + "[2026-10-28 15:53:50] [0,0] RecvMsgClientLogOnResponse() : processing complete" + "\n");
    w.Poll(f);
    Check("Steam: queda e login no mesmo segundo acaba online", w.Online == true);
    File.AppendAllText(f, "[2026-10-28 15:54:10] [0,0] ConnectionDisconnected('I/O Operation Failed')" + "\n");
    w.Poll(f);
    Check("Steam: queda depois do login fica offline", w.Online == false);
    try { File.Delete(f); } catch { }
}

Console.WriteLine(fails == 0 ? "\nTUDO OK" : $"\n{fails} FALHA(S)");
return fails;
