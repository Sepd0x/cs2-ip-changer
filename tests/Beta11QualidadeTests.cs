using System.IO;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// beta.11, qualidade (4 nov., 2.ª sessão): as correções M dos utilizadores simulados (Phone setup só com o passo atual, menus por
// marca só verificados, Checks recolhidos, VPN, servidor em que entraste, What the app changed, toast do ban), o contador
// phone_left_ para a dashboard v4, e o item 5 (updates sem o limite da API, Install sem programas do SRT_Dados antigo, Reciclagem,
// aviso das regras que ficam)
static class Beta11QualidadeTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { repo, "src" }.Concat(p).ToArray())).Replace("\r\n", "\n");

        // ---------------- 1. Phone setup: só o passo atual aberto
        var debugOff = new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new PhoneUsbState(true, null, "Google") };
        var steps = PhoneSetup.Steps(debugOff);
        var now = PhoneSetup.Current(steps);
        check("Setup: o passo atual é a depuração USB", now?.Step == PhoneStep.NoUsbDebugging, now?.Step.ToString());
        check("Setup: o passo atual mostra o texto todo", PhoneSetup.Shown(now, now) == now.Detail, "");
        check("Setup: os feitos dizem só Done", steps.Where(i => i.State == PhoneSetup.StepState.Ok).All(i => PhoneSetup.Shown(i, now) == "Done"), "");
        var later = steps.Where(i => i != now && i.State != PhoneSetup.StepState.Ok).ToList();
        check("Setup: os seguintes só o título (sem texto)", later.Count > 0 && later.All(i => PhoneSetup.Shown(i, now) == null), string.Join(",", later.Select(i => i.Title)));
        var tetherRow = new SetupStepRow(steps.First(i => i.Step == PhoneStep.TetherOff), now, null, true);
        check("Setup: um passo seguinte vermelho fica cinzento e sem botão", !tetherRow.HasAction && tetherRow.Later && tetherRow.Detail == null, "");
        var nowRow = new SetupStepRow(now, now, null, true);
        check("Setup: o passo atual fica aberto", nowRow.Current && !nowRow.Later && nowRow.Detail == now.Detail, "");
        var phoneSrc = Src("Views", "MainWindow.Phone.cs");
        check("Setup: o seguinte fica com a marca cinzenta", phoneSrc.Contains("public Brush Brush => _later ? Ui.Faint"), "");
        var ready = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true, Usb = new PhoneUsbState(true, null, "Google") });
        check("Setup: tudo pronto, sem passo atual, cada linha diz Done", PhoneSetup.Current(ready) == null && ready.All(i => PhoneSetup.Shown(i, null) == "Done"), "");
        var hotspot = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true });
        var cable = hotspot.First(i => i.Step == PhoneStep.NoUsb);
        check("Setup: hotspot sem passo atual, o cabo opcional diz porquê", PhoneSetup.Current(hotspot) == null || PhoneSetup.Shown(cable, PhoneSetup.Current(hotspot)) != null || cable.State == PhoneSetup.StepState.Ok
            ? PhoneSetup.Shown(cable, null)?.Contains("only needed for Change IP") == true : false, PhoneSetup.Shown(cable, null));
        var iphoneDone = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Tethered = true, Usb = new PhoneUsbState(true, null, "Apple") });
        check("Setup iPhone: pronto, a linha Change IP continua a dizer como", PhoneSetup.Shown(iphoneDone.Last(), null) == PhoneSetup.IphoneChangeIp, "");
        var iphonePlugged = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = new PhoneUsbState(true, null, "Apple") });
        var iNow = PhoneSetup.Current(iphonePlugged);
        check("Setup iPhone: o driver da Apple passa para o passo aberto", iNow?.Step == PhoneStep.Unauthorized && iNow.Detail.Contains("Apple Devices"), iNow?.Detail);
        var iHotspot = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Usb = new PhoneUsbState(true, null, "Apple") }).First(i => i.Step == PhoneStep.TetherOff);
        check("Setup iPhone: o Personal Hotspot também lembra o driver", iHotspot.Detail.Contains("Still red? ") && iHotspot.Detail.Contains("Apple Devices"), iHotspot.Detail);
        check("Setup iPhone: a linha do driver em espera fica só com o título", PhoneSetup.Shown(iphonePlugged[0], iNow) == null, "");
        check("Setup: XAML esconde o texto vazio", Src("MainWindow.xaml").Contains("Visibility=\"{Binding DetailVisibility}\""), "");

        // ---------------- 2. Menus por marca: só os verificados chegam ao ecrã
        check("Marca: Samsung tem os seus caminhos", PhoneSetup.PathsFor("Samsung").Brand == "Samsung", "");
        check("Marca: Xiaomi ainda não verificado, mostra o genérico", PhoneSetup.PathsFor("Xiaomi") == PhoneSetup.GenericPaths, "");
        check("Marca: Motorola ainda não verificado, mostra o genérico", PhoneSetup.PathsFor("Motorola") == PhoneSetup.GenericPaths, "");
        check("Marca: desconhecida ou nula, o genérico", PhoneSetup.PathsFor("Nothing") == PhoneSetup.GenericPaths && PhoneSetup.PathsFor(null) == PhoneSetup.GenericPaths, "");
        check("Marca: nenhum caminho por verificar chega ao ecrã", PhoneSetup.BrandPaths.Where(p => !p.Verified).All(p => PhoneSetup.PathsFor(p.Brand) == PhoneSetup.GenericPaths), "");
        check("Marca: Xiaomi e Motorola estão escritos, por verificar", PhoneSetup.BrandPaths.Count(p => !p.Verified) >= 2, "");
        var samsungDebug = PhoneSetup.Steps(debugOff, samsung: true).First(i => i.Step == PhoneStep.NoUsbDebugging).Detail;
        check("Marca: Samsung, a depuração USB diz Software information", samsungDebug.Contains("Software information") && !samsungDebug.Contains(PhoneSetup.SearchHint), samsungDebug);
        var genericDebug = steps.First(i => i.Step == PhoneStep.NoUsbDebugging).Detail;
        check("Marca: genérico, sem Samsung no texto e com a dica da pesquisa", !genericDebug.Contains("Samsung") && genericDebug.Contains(PhoneSetup.SearchHint), genericDebug);
        check("Marca: começa pelo porquê e diz o que tocar", genericDebug.StartsWith(PhoneSetup.UsbDebuggingWhy) && genericDebug.Contains("tap Allow"), "");
        var noAdb = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Tethered = false, Usb = new PhoneUsbState(true, null, "Samsung") }, samsung: true);
        var tetherSamsung = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "unauthorized", Usb = new PhoneUsbState(true, null, "Samsung") }, samsung: true).First(i => i.Step == PhoneStep.TetherOff);
        check("Marca: tethering à mão na Samsung, o caminho da Samsung", tetherSamsung.State != PhoneSetup.StepState.Todo || tetherSamsung.Detail.Contains("Mobile Hotspot and Tethering"), tetherSamsung.Detail);
        check("Allowed: diz o que marcar e tocar", PhoneSetup.Advice(PhoneStep.Unauthorized) == "On the phone, tick \"Always allow from this computer\", then tap Allow.", "");

        // ---------------- 3. Contador phone_left_ (combinado com a dashboard v4)
        var name = new Regex(@"^[A-Za-z0-9_]{1,48}$");
        check("Contador: passo atual", PhoneSetup.StepKey(steps) == "nousbdebugging", PhoneSetup.StepKey(steps));
        check("Contador: tudo pronto é done", PhoneSetup.StepKey(ready) == "done", "");
        var counts = PhoneSetup.ClosedCounts("nousb", "tetheroff");
        check("Contador: ao fechar, o passo ao abrir e o passo ao fechar", counts.SequenceEqual(new[] { "phone_setup_nousb", "phone_left_tetheroff" }), string.Join(",", counts));
        check("Contador: sem abertura não conta nada", PhoneSetup.ClosedCounts(null, null).Length == 0, "");
        check("Contador: todos os nomes passam no formato da telemetria",
            Enum.GetValues<PhoneStep>().All(s => name.IsMatch("phone_left_" + PhoneSetup.Key(s))) && name.IsMatch("phone_left_done"), "");
        check("Contador: a chave do driver é usbdrivererror (combinado)", PhoneSetup.Key(PhoneStep.UsbDriverError) == "usbdrivererror", "");
        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Contador: conta ao fechar o painel", phone.Contains("CountSetupClosed();") && phone.Contains("PhoneSetup.ClosedCounts(_setupFirstTodo, _setupLeft)"), "");
        check("Contador: conta ao sair com o painel aberto", Src("Views", "MainWindow.Telemetry.cs").Contains("Telemetry.AtExit(CountSetupClosed);")
            && Src("Core", "Telemetry.cs").Contains("try { _atExit?.Invoke(); } catch { }"), "");
        check("Contador: nunca no demo", phone.Contains("if (_demo) return;\n        foreach (var n in PhoneSetup.ClosedCounts"), "");

        // ---------------- 4. Checks recolhidos
        check("Checks: verde não aparece recolhido", !ChecksView.Shows(false, false), "");
        check("Checks: com problema aparece", ChecksView.Shows(true, false), "");
        check("Checks: Show all mostra tudo", ChecksView.Shows(false, true), "");
        check("Checks: tudo verde numa linha", ChecksView.Summary(11, 11, 0) == "All 11 checks OK.", "");
        check("Checks: um problema", ChecksView.Summary(11, 9, 1) == "1 check needs you.", "");
        check("Checks: vários problemas", ChecksView.Summary(11, 5, 3) == "3 checks need you.", "");
        check("Checks: com linhas sem uso não diz OK a tudo (e não deixa a dúvida 'quais 2?')", ChecksView.Summary(11, 9, 0) == "All good: nothing needs you.", ChecksView.Summary(11, 9, 0));
        check("Checks: proteção desligada diz isso mesmo", ChecksView.Summary(11, 1, 0, protOn: false) == "Protection is off, so there is little to check yet.", "");
        check("Checks: proteção desligada com um problema mostra o problema", ChecksView.Summary(11, 1, 1, protOn: false) == "1 check needs you.", "");
        check("Checks: botão", ChecksView.Toggle(false) == "Show all" && ChecksView.Toggle(true) == "Show less", "");
        check("Checks: vermelho e amarelo pedem atenção, cinzento não", new CheckItem { Ok = false }.Attention && new CheckItem { Waiting = true }.Attention && !new CheckItem { Ok = null }.Attention && !new CheckItem { Ok = true }.Attention, "");
        check("Checks: regiões que tu desligaste não contam como problema", !new CheckItem { Waiting = true, Chosen = true }.Attention
            && Src("MainWindow.xaml.cs").Contains("Title = \"Matchmaking regions\", Chosen = true"), "");
        check("Checks: Show all lembrado nas definições", new Settings().ChecksShowAll == false && Src("Views", "MainWindow.Checks.cs").Contains("_s.ChecksShowAll = !_s.ChecksShowAll;\n        SaveSettings();"), "");
        check("Checks: XAML esconde as linhas", Src("MainWindow.xaml").Contains("Visibility=\"{Binding Shown, Converter={StaticResource Vis}}\""), "");

        // ---------------- 5. VPN
        var wifi = new Adapter { Index = 5, Name = "Wi-Fi", Description = "MediaTek", Up = true, LocalIp = "192.168.1.5", IsWifi = true };
        var tailscale = new Adapter { Index = 9, Name = "Tailscale", Description = "Tailscale Tunnel", Up = true, LocalIp = "100.64.0.2", IsVpn = true };
        var wg = new Adapter { Index = 12, Name = "wg0", Description = "WireGuard Tunnel", Up = true, LocalIp = "10.2.0.2", IsVpn = true };
        check("VPN: só ligada (Tailscale sem exit node) não conta", ChecksView.VpnOn(new[] { wifi, tailscale }, 5) == null, "");
        check("VPN: a levar o tráfego do PC conta", ChecksView.VpnOn(new[] { wifi, wg }, 12) == "WireGuard Tunnel", "");
        check("VPN: o telemóvel nunca é VPN", ChecksView.VpnOn(new[] { new Adapter { Index = 3, IsVpn = true, IsPhone = true, Up = true, Description = "RNDIS VPN" } }, 3) == null, "");
        check("VPN: diz porquê e quando desligar, sem prometer mais (kill switch por testar)", ChecksView.VpnDetail("X").StartsWith("On (X). Not tested with the app yet")
            && ChecksView.VpnDetail("X").Contains("if CS2 or your servers don't connect") && ChecksView.VpnHelp.Contains("wasn't tested"), "");
        check("VPN: lida em cada relatório", Src("Views", "MainWindow.Protection.cs").Contains("UpdateVpnCheck(rep);"), "");
        check("VPN: a linha fica fora das estatísticas (não é vermelha)", !Telemetry.CheckKeys.ContainsKey("VPN app"), "");

        // ---------------- 6. Toast do ban diz a conta seguinte
        check("Ban: diz a tecla e a conta seguinte", MainWindow.BanToast(" for 30 minutes", "Ctrl+Shift+F9", "atlas") == "Banned for 30 minutes. Press Ctrl+Shift+F9 to rejoin with a new IP as atlas.", "");
        check("Ban: sem conta seguinte não manda carregar na tecla", !MainWindow.BanToast("", "F9", null).Contains("Press") && MainWindow.BanToast("", "F9", null).Contains("No other account"), "");
        check("Ban: o toast usa a conta seguinte do servidor", Src("Views", "MainWindow.Kick.cs").Contains("BanToast(d.For, _s.HotkeyRejoin, NextQuick(inList)?.Name)"), "");

        // ---------------- 6b. Um só botão azul: com o Wi-Fi do telemóvel ligado ou sem telemóvel, o azul é o do arranjo
        check("Botão: com algo a bloquear, nem Change IP nem rejoin ficam azuis", MainWindow.StatusPrimary(true, false, true, fixFirst: true) == ("Btn", "Btn")
            && MainWindow.StatusPrimary(true, false, false, fixFirst: true) == ("Btn", "Btn"), "");
        check("Botão: sem bloqueio fica como antes", MainWindow.StatusPrimary(true, false, true) == ("Primary", "Btn") && MainWindow.StatusPrimary(true, false, false) == ("Btn", "Primary"), "");
        check("Botão: lê o Wi-Fi do telemóvel e a ligação", Src("Views", "MainWindow.Kick.cs").Contains("PhoneWifiOn || (_last != null && _last.Egress == null)"), "");

        // ---------------- 7. Oferecer o servidor em que entraste
        var offered = new HashSet<string>();
        check("Servidor novo: oferece", MainWindow.OffersAdd("1.2.3.4:27015", new[] { "5.6.7.8:27015" }, offered), "");
        check("Servidor da lista: não oferece", !MainWindow.OffersAdd("5.6.7.8:27015", new[] { "5.6.7.8:27015" }, offered), "");
        offered.Add("1.2.3.4:27015");
        check("Servidor novo: só uma vez", !MainWindow.OffersAdd("1.2.3.4:27015", Array.Empty<string>(), offered), "");
        check("Servidor novo: Valve ou menu (sem endereço) não", !MainWindow.OffersAdd(null, Array.Empty<string>(), new HashSet<string>()), "");
        var addText = MainWindow.AddJoinedText("1.2.3.4:27015");
        check("Servidor novo: não diz que entraste protegido", !addText.Contains("protected", StringComparison.OrdinalIgnoreCase) && addText.Contains("isn't in your servers"), addText);
        var kick = Src("Views", "MainWindow.Kick.cs");
        check("Servidor novo: o toast tem o botão Add", kick.Contains("Toast(AddJoinedText(server), Tone.Info, \"Add\", () => AddFromText(server));"), "");
        var xamlSrv = Src("MainWindow.xaml");
        check("Servers vazio: quem só sabe o nome entra pelo CS2 e a app oferece juntá-lo (com a proteção desligada: em Game only os de fora da lista não ligam)",
            xamlSrv.Contains("Only know its name? Join it once in CS2 with protection off (that join uses your home IP): the app offers to add it."), "");
        check("Servers vazio: quem está banido não é mandado entrar para escrever status", xamlSrv.Contains("Banned there? Ask on its Discord or website for the IP")
            && !xamlSrv.Contains("join it in CS2 and type"), "");
        check("Toast com ação: o XAML tem o botão", Src("MainWindow.xaml").Contains("x:Name=\"ToastActionBtn\""), "");
        check("Toast normal: esconde o botão", Src("Views", "MainWindow.Feedback.cs").Contains("_toastAction = null;\n        ToastActionBtn.Visibility = Visibility.Collapsed;"), "");

        // ---------------- 8. What the app changed
        check("Mudanças: IPs do kill switch", AppChanges.Blocks("203.0.113.10/255.255.255.255,198.51.100.40") == "203.0.113.10, 198.51.100.40", AppChanges.Blocks("203.0.113.10/255.255.255.255,198.51.100.40"));
        check("Mudanças: * é tudo fora do telemóvel", AppChanges.Blocks("*") == "everything outside the phone", "");
        check("Mudanças: intervalos contados", AppChanges.Blocks("1.0.0.0-1.0.0.255,2.0.0.0/8") == "2 address ranges", "");
        check("Mudanças: muitos IPs cortados", AppChanges.Blocks(string.Join(",", Enumerable.Range(1, 15).Select(i => "10.0.0." + i))).EndsWith("and 3 more"), "");
        var changes = AppChanges.Text(new[] { new Firewall.RuleInfo(Firewall.RuleName, "203.0.113.10") }, new[] { "203.0.113.10/32|21|192.168.42.129" }, 21,
            i => i == 21 ? "Phone (USB)" : null, @"C:\Program Files\CS2 IP Changer\CS2IPChanger.exe");
        check("Mudanças: diz a regra, o que bloqueia e o nome", changes.Contains("Kill switch") && changes.Contains("Blocks: 203.0.113.10.") && changes.Contains("Rule name: " + Firewall.RuleName), changes);
        check("Mudanças: diz a rota e por onde", changes.Contains("203.0.113.10/32 through Phone (USB) (192.168.42.129)"), changes);
        check("Mudanças: diz a métrica", changes.Contains("Metric of Phone (USB) raised"), "");
        check("Mudanças: o comando de desfazer", changes.EndsWith("\"C:\\Program Files\\CS2 IP Changer\\CS2IPChanger.exe\" --restore"), "");
        var changes2 = AppChanges.Text(new[] { new Firewall.RuleInfo(Firewall.RuleName, "1.1.1.1") }, Array.Empty<string>(), -1, _ => null, "x.exe",
            new[] { "Start with Windows: the scheduled task \"CS2 IP Changer\"." });
        check("Mudanças: diz porque os nomes das regras são os antigos", changes2.Contains("same since the first version"), "");
        check("Mudanças: também a tarefa e a pasta da app", changes2.Contains("• Start with Windows: the scheduled task") && Src("Views", "MainWindow.Checks.cs").Contains("App folder: {Uninstall.Root}"), "");
        check("Telemóvel: Turn on USB tethering só quando ainda não está ligado", MainWindow.ShowTetherButton(false, true, false) && !MainWindow.ShowTetherButton(false, true, true)
            && !MainWindow.ShowTetherButton(true, true, false) && !MainWindow.ShowTetherButton(false, false, false), "");
        check("Mudanças: sem nada", AppChanges.Text(Array.Empty<Firewall.RuleInfo>(), Array.Empty<string>(), -1, _ => null, "x.exe").Contains("no rules from the app"), "");
        check("Mudanças: cada regra da app tem nome claro", new[] { Firewall.RuleName, Firewall.EchoRuleName, Firewall.Cs2RuleName, Firewall.DnsUdpRuleName, Firewall.DnsTcpRuleName, Firewall.Ipv6RuleName, Firewall.RegionsRuleName }
            .All(n => Firewall.Meaning(n) != "From an older version"), "");
        check("Mudanças: botão no Log", Src("MainWindow.xaml").Contains("Click=\"AppChanges_Click\" Content=\"What the app changed\""), "");
        check("Mudanças: só lê (nada se remove)", !Src("Views", "MainWindow.Checks.cs").Contains("Firewall.Remove"), "");

        // ---------------- 9. Updates sem o limite da API (C6)
        var ok = Updater.FromLatestRedirect(new Uri("https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v0.1.0-beta.11"));
        check("Updates: o redirect dá a versão", ok?.Version == "0.1.0-beta.11" && ok.Tag == "v0.1.0-beta.11", ok?.Version);
        check("Updates: os ficheiros são os da release, com a assinatura", ok?.ExeUrl == "https://github.com/Sepd0x/cs2-ip-changer/releases/download/v0.1.0-beta.11/CS2IPChanger.exe"
            && ok.ShaUrl == ok.ExeUrl + ".sha256" && ok.SigUrl == ok.ExeUrl + ".sha256.sig", ok?.ExeUrl);
        check("Updates: outro site não", Updater.FromLatestRedirect(new Uri("https://evil.example/Sepd0x/cs2-ip-changer/releases/tag/v9.9.9")) == null, "");
        check("Updates: outro repo não", Updater.FromLatestRedirect(new Uri("https://github.com/someone/cs2-ip-changer/releases/tag/v9.9.9")) == null, "");
        check("Updates: http não", Updater.FromLatestRedirect(new Uri("http://github.com/Sepd0x/cs2-ip-changer/releases/tag/v9.9.9")) == null, "");
        check("Updates: tag que não é versão não", Updater.FromLatestRedirect(new Uri("https://github.com/Sepd0x/cs2-ip-changer/releases/tag/latest")) == null
            && Updater.FromLatestRedirect(new Uri("https://github.com/Sepd0x/cs2-ip-changer/releases/tag/v1.0.0%2F..%2Fx")) == null, "");
        check("Updates: sem redirect não", Updater.FromLatestRedirect(null) == null, "");
        var upd = Src("Core", "Updater.cs");
        check("Updates: a API primeiro, o redirect se ela falhar", upd.Contains("AllowAutoRedirect = false") && upd.IndexOf("ApiUrl + \"?per_page=50\"") < upd.IndexOf("LatestPage, HttpCompletionOption"), "");
        check("Updates: a instalação continua a exigir a assinatura", upd.Contains("if (r.SigUrl == null) throw") && upd.Contains("SignedByProject(r.Version, shaFile"), "");

        // ---------------- 10. Install: sem programas do SRT_Dados antigo, Reciclagem, regras que ficam
        check("Install: não copia exe nem DLL", Installer.Skip("adb.exe") && Installer.Skip("AdbWinApi.dll") && Installer.Skip("x.CMD") && Installer.Skip("run.ps1"), "");
        check("Install: copia os dados", !Installer.Skip("settings.json") && !Installer.Skip("accounts.json") && !Installer.Skip("registo.log"), "");
        check("Install: não copia o platform-tools", Installer.SkipDir("platform-tools") && !Installer.SkipDir("profiles"), "");
        var tmp = Path.Combine(Path.GetTempPath(), "srt-b11q-" + Guid.NewGuid().ToString("N"));
        try
        {
            var from = Path.Combine(tmp, "from", "SRT_Dados");
            Directory.CreateDirectory(Path.Combine(from, "platform-tools"));
            Directory.CreateDirectory(Path.Combine(from, "profiles"));
            File.WriteAllText(Path.Combine(from, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(from, "profiles", "a.json"), "{}");
            File.WriteAllText(Path.Combine(from, "platform-tools", "adb.exe"), "x");
            File.WriteAllText(Path.Combine(from, "evil.dll"), "x");
            var to = Path.Combine(tmp, "to");
            var (files, skipped) = Installer.CopyData(from, to);
            check("Install: copia só os dados (2 ficheiros)", files == 2 && skipped == 0, $"{files} {skipped}");
            check("Install: nem a DLL nem o platform-tools chegam", !File.Exists(Path.Combine(to, "evil.dll")) && !Directory.Exists(Path.Combine(to, "platform-tools")), "");
            var fromDir = Path.Combine(tmp, "from");
            File.WriteAllText(Path.Combine(fromDir, AppIds.ExeName + ".old"), "x");
            var good = new Installer.Result("x", null, 2, 0, false);
            check("Install: oferece o SRT_Dados e o .old depois de copiar tudo", Installer.OldCopy(fromDir, good).Count == 2, "");
            check("Install: com ficheiros por copiar não oferece", Installer.OldCopy(fromDir, good with { Skipped = 1 }).Count == 0, "");
            check("Install: se ficou o SRT_Dados que lá estava (o antigo não foi copiado) não oferece", Installer.OldCopy(fromDir, good with { DataKept = true }).Count == 0, "");
            check("Install: com erro nos dados não oferece", Installer.OldCopy(fromDir, good with { DataError = "x" }).Count == 0, "");
            check("Install: Reciclagem recusa o que não existe", !Installer.Recycle(Path.Combine(tmp, "nada")), "");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        check("Install: o texto diz Reciclagem e que dá para recuperar", Installer.OldCopyText(@"C:\Users\x\Downloads").Contains("Recycle Bin") && Installer.OldCopyText("x").Contains("get it back"), "");
        var prog = Src("Program.cs");
        check("Install: só com o sim (Send to Recycle Bin / Keep it)", prog.Contains("NoticeWindow.Ask(\"The old copy\", Installer.OldCopyText(from), \"Send to Recycle Bin\", \"Keep it\")"), "");
        check("Install: nunca apaga para sempre (ALLOWUNDO)", Src("Core", "Installer.cs").Contains("Flags = 0x40 |"), "");
        var withBlock = SRT.Program.InstallText(@"C:\Users\x\Downloads", @"C:\Program Files\CS2 IP Changer", false, null, "0.1.0-beta.11", blockLeft: true);
        check("Install: avisa a proteção antiga que ficou e o --restore", withBlock.Contains("Protection from the old copy is still on") && withBlock.Contains("\"C:\\Users\\x\\Downloads\\CS2IPChanger.exe\" --restore"), "");
        check("Install: sem regras não avisa", !SRT.Program.InstallText("a", "b", false).Contains("still on"), "");
        check("Install: lê as regras ao abrir", prog.Contains("Firewall.BlockLeft(Firewall.Remaining(), unreadableCounts: false)"), "");
    }
}
