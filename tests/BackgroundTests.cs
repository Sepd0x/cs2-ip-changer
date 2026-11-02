using System.Diagnostics;
using System.IO;
using SRT;
using SRT.Core;

// Fase 2: viver em segundo plano (bandeja, arrancar com o Windows, contador de dados, remover tudo)
static class BackgroundTests
{
    public static void Run(CoreTests.CheckFn check)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "srt-bg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try { Background(check, tmp); }
        finally { try { SRT.Program.DeleteTree(tmp); } catch { } }
    }

    static void Background(CoreTests.CheckFn check, string tmp)
    {
        var ci = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";   // no Explorer (no notification area) on the runner
        // Arrancar com o Windows: só a cópia instalada, tarefa com o exe entre aspas, --tray, admin sem UAC, também na bateria
        {
            var target = @"C:\Program Files\CS2 IP Changer";
            check("Autostart: só a cópia em Program Files", Autostart.Allowed(target + @"\CS2IPChanger.exe", target)
                && !Autostart.Allowed(@"C:\Users\x\Downloads\CS2IPChanger.exe", target) && !Autostart.Allowed(target + @"\sub\CS2IPChanger.exe", target) && !Autostart.Allowed(null, target));
            var exe = @"C:\Program Files\A & B <x>\CS2IPChanger.exe";
            var xml = Autostart.TaskXml(exe, "S-1-5-21-1-2-3-1001");
            check("Autostart: tarefa do utilizador, admin sem UAC, --tray, sem limites de bateria nem de tempo",
                xml.StartsWith("<?xml") && xml.Contains("<UserId>S-1-5-21-1-2-3-1001</UserId>") && xml.Contains("<RunLevel>HighestAvailable</RunLevel>")
                && xml.Contains("<LogonType>InteractiveToken</LogonType>") && xml.Contains("<Arguments>--tray</Arguments>")
                && xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>") && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
                && xml.Contains("&amp;") && !xml.Contains("A & B")
                && xml.Contains("<AllowStartOnDemand>false</AllowStartOnDemand>"));   // "schtasks /Run" would start it as admin without UAC
            check("Autostart: o exe da tarefa lê-se de volta igual", Autostart.CommandIn(xml) == exe && Autostart.CommandIn("<x/>") == null);
            // Só lê (schtasks /Query): um exe que nenhuma tarefa arranca não conta como ligado
            check("Autostart: tarefa de outro exe não conta como ligada", Autostart.IsOn(@"C:\nada\CS2IPChanger.exe").GetAwaiter().GetResult() != true);
            try { System.Xml.Linq.XDocument.Parse(xml.Replace("encoding=\"UTF-16\"", "")); check("Autostart: XML válido", true); }
            catch (Exception ex) { check("Autostart: XML válido", false, ex.Message); }
        }

        // Contador de dados móveis
        {
            var m = new DataMeter();
            var first = m.Add("a", 1000);
            var d1 = m.Add("a", 1500);
            var reset = m.Add("a", 200);                 // telemóvel ligado outra vez: o contador recomeça
            var other = m.Add("b", 9_000_000);           // outro adaptador: só marca o início
            var gone = m.Add(null, -1);
            var back = m.Add("b", 9_000_100);            // voltou: só marca o início outra vez
            check("Dados: 1.ª leitura marca o início, depois soma; contador recomeçado conta o novo total",
                first == 0 && d1 == 500 && reset == 200 && other == 0 && gone == 0 && back == 0 && m.Session == 700, m.Session.ToString());
            var nov = new DateTime(2026, 11, 30, 23, 0, 0);
            var dec = new DateTime(2026, 12, 1, 0, 1, 0);
            check("Dados: mês novo começa do zero", DataMeter.AddToMonth("2026-11", 5, 10, nov) == ("2026-11", 15) && DataMeter.AddToMonth("2026-11", 5, 10, dec) == ("2026-12", 10));
            check("Dados: aviso só quando passa o limite", DataMeter.Crossed(9_999_999_999, 10_000_000_001, 10) && !DataMeter.Crossed(10_000_000_001, 10_000_000_100, 10)
                && !DataMeter.Crossed(1, 99_000_000_000, 0) && !DataMeter.Crossed(1, 2, 10));
            check("Dados: texto em MB e GB", DataMeter.Text(85_400_000) == "85 MB" && DataMeter.Text(1_240_000_000) == "1.24 GB" && DataMeter.Text(5_000_000) == "5.0 MB",
                DataMeter.Text(85_400_000) + " " + DataMeter.Text(1_240_000_000));
            check("Dados: linha do Phone", MainWindow.DataText(85_400_000, 1_240_000_000, nov) == "Mobile data: 85 MB since the app opened, 1.24 GB in November.");
            check("Dados: adaptador que não existe dá -1", DataMeter.Read("{00000000-0000-0000-0000-000000000000}") == -1 && DataMeter.Read(null) == -1);
        }

        // Bandeja: o X só esconde com a opção ligada; nunca ao sair, numa atualização ou ao desligar o Windows
        {
            check("Bandeja: o X esconde só com a opção e o ícone", MainWindow.GoesToTray(true, true, false, false, false, false)
                && !MainWindow.GoesToTray(false, true, false, false, false, false) && !MainWindow.GoesToTray(true, false, false, false, false, false));
            check("Bandeja: Quit, atualização, fim de sessão e demo fecham mesmo", !MainWindow.GoesToTray(true, true, true, false, false, false)
                && !MainWindow.GoesToTray(true, true, false, true, false, false) && !MainWindow.GoesToTray(true, true, false, false, true, false) && !MainWindow.GoesToTray(true, true, false, false, false, true));
            var menu = MainWindow.TrayLayout("Protected", armed: false, phone: false);
            var keys = menu.Where(l => l.Key != null).Select(l => l.Key).ToList();
            check("Bandeja: menu com estado, Open, New IP, Change IP, Regions, Turn off, Quit",
                keys.SequenceEqual(new[] { "state", "open", "switch", "changeip", "regions", "off", "quit" }) && menu[0].Text == "Protected" && !menu[0].Enabled);
            check("Bandeja: Turn off só com a proteção ligada, Change IP só com telemóvel",
                !menu.First(l => l.Key == "off").Enabled && !menu.First(l => l.Key == "changeip").Enabled
                && MainWindow.TrayLayout("x", true, true).Where(l => l.Key is "off" or "changeip").All(l => l.Enabled));
            // Parte 5: o menu escuro. Linha do estado cinzenta e sem comando, Open a negrito, separadores, cinzentos sem telemóvel e sem proteção
            {
                var ran = new List<string>();
                var items = MainWindow.TrayItems("Protected", armed: false, phone: false, ran.Add);
                var flags = items.Select(Tray.MenuFlags).ToList();
                check("Bandeja escura: estado cinzento, Open a negrito, 2 separadores",
                    flags[0] == (Tray.MfGrayed, false) && items[0].Run == null && flags[2] == (Tray.MfString, true)
                    && flags.Count(f => f.Flags == Tray.MfSeparator) == 2 && flags.Count(f => f.Default) == 1);
                check("Bandeja escura: Change IP e Turn off cinzentos quando não servem",
                    flags[items.FindIndex(i => i.Text == "Change IP")].Flags == Tray.MfGrayed && flags[items.FindIndex(i => i.Text == "Turn off protection")].Flags == Tray.MfGrayed
                    && MainWindow.TrayItems("x", true, true, _ => { }).Where(i => i.Text is "Change IP" or "Turn off protection").All(i => Tray.MenuFlags(i).Flags == Tray.MfString));
                items.First(i => i.Text == "Quit").Run();
                items.First(i => i.Text == "Regions").Run();
                check("Bandeja escura: cada linha corre o seu comando", ran.SequenceEqual(new[] { "quit", "regions" }), string.Join(",", ran));
                check("Bandeja escura: modo escuro só no Windows 10 1903 ou mais novo (antes o número 135 era outra função)",
                    Tray.DarkMenusSupported(new Version(10, 0, 18362)) && Tray.DarkMenusSupported(new Version(10, 0, 26100)) && !Tray.DarkMenusSupported(new Version(10, 0, 17763)) && !Tray.DarkMenusSupported(new Version(6, 3, 9600)));
                check("Bandeja escura: liga neste Windows sem rebentar", Tray.DarkMenus() == Tray.DarkMenusSupported(Environment.OSVersion.Version));
            }
            // O ícone a sério (fica um instante na área de notificação): o Windows aceita-o, a dica muda e sai no Dispose
            if (!ci)
            {
                bool shown = false, tipOk = false;
                var t = new Thread(() =>
                {
                    using var tray = new Tray(() => Array.Empty<Tray.Item>(), () => { });
                    shown = tray.Shown;
                    tray.Tip = "CS2 IP Changer: teste";
                    tipOk = tray.Tip == "CS2 IP Changer: teste";
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join(10000);
                check("Bandeja: o Windows aceita o ícone e a dica", shown && tipOk);
            }
            check("Bandeja: dica até 127 carateres", MainWindow.TrayTip("Protected") == "CS2 IP Changer: Protected" && Tray.TrimTip(new string('a', 300)).Length == 127 && MainWindow.TrayTip(null) == "CS2 IP Changer");
        }

        // Fase 3: dicas de lag (só telemóvel e cabo; nunca comandos de consola do CS:GO)
        {
            var hot = NetReport.Tips(hotspot: true);
            var usb = NetReport.Tips(hotspot: false);
            check("Lag: dicas com USB só para quem está no hotspot, poupança de energia, 5G e longe da caixa",
                hot.Contains("USB cable") && !usb.Contains("USB") && usb.Contains("battery saver") && usb.Contains("5G") && usb.Contains("PC case")
                && !hot.Contains("cl_") && !hot.Contains("rate") && !hot.Contains("interp"));
        }

        // Fase 4: as 3 pequenas do estudo da concorrência
        {
            check("Concorrência: modo streamer liga com OBS e Streamlabs", Streaming.AppOpen(new[] { "cs2", "obs64" }) && Streaming.AppOpen(new[] { "Streamlabs OBS" })
                && Streaming.AppOpen(new[] { "OBS64" }) && !Streaming.AppOpen(new[] { "cs2", "steam", "discord", "obsidian" }) && !Streaming.AppOpen(Array.Empty<string>()) && !Streaming.AppOpen(null));
            var plain = Firewall.OffHint(Array.Empty<string>(), "there is no kill switch");
            var bd = Firewall.OffHint(new[] { "Bitdefender Firewall", "Windows Defender Firewall", "Bitdefender Firewall" }, "there is no kill switch");
            check("Concorrência: diz qual firewall de outro programa desligou a do Windows",
                plain == "Without the Firewall there is no kill switch; turn it on in Windows Security > Firewall."
                && bd.StartsWith("Bitdefender Firewall turned it off") && !bd.Contains("Windows Defender Firewall") && bd.Split("Bitdefender Firewall").Length == 2 && bd.Contains("in Bitdefender,"), bd);
            check("Concorrência: lista de firewalls lê-se sem rebentar", Firewall.OtherFirewalls() != null);
            var code = Steam.Cs2FriendCode("76561197960287930");
            check("Concorrência: código de amigo do CS2 (exemplo público)", code == "SUCVS-FADA", code ?? "null");
            var other = Steam.Cs2FriendCode("76561198000000001");
            check("Concorrência: código de amigo só de SteamID64, sem I, O, 0 nem 1",
                Steam.Cs2FriendCode("abc") == null && Steam.Cs2FriendCode("76561197960287930\n") == null && Steam.Cs2FriendCode("") == null && Steam.Cs2FriendCode(null) == null && Steam.Cs2FriendCode("123") == null
                && other != null && System.Text.RegularExpressions.Regex.IsMatch(other, "^([A-HJ-NP-Z2-9]{4}-)?[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{4}$"), other ?? "null");
        }
    }
}
