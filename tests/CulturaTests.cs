using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SRT;
using SRT.Core;

// 25 nov.: a cultura do Windows (formato regional) não pode mudar o que a app lê e escreve como dados. A app nunca muda a
// CurrentCulture (o Loc só troca o dicionário), por isso vale a do Windows de cada utilizador. Em turco (tr-TR, e az) o "i" só
// emparelha com "İ" e o "I" com "ı": uma Regex com IgnoreCase sem CultureInvariant deixava de reconhecer "128tick", "ANDROID",
// "rndis" ou um login noutra caixa. Em finlandês e dinamarquês a hora escreve-se "14.30.05" e em tailandês, árabe e persa o ano
// não é 2026: a linha do registo.log saía num formato que a app depois não sabia ler.
static class CulturaTests
{
    // As Regex estáticas ficam com a cultura do momento em que a classe é carregada: só um processo que arranca já em tr-TR
    // se porta como um Windows em turco. O processo filho corre os mesmos testes de dados e devolve uma linha por teste.
    static readonly string[] Culturas = { "tr-TR", "az-Latn-AZ", "fi-FI", "th-TH" };

    public static void Run(Action<string, bool, string> check)
    {
        foreach (var name in Culturas)
        {
            var lines = new List<string>();
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, ArgumentList = { "--cultura", name } };
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
                lines = output.Replace("\r", "").Split('\n').Where(l => l.StartsWith("OK\t", StringComparison.Ordinal) || l.StartsWith("FALHA\t", StringComparison.Ordinal)).ToList();
            }
            catch (Exception ex) { check($"Cultura {name}: o processo filho correu", false, ex.Message); continue; }
            check($"Cultura {name}: o processo filho correu os testes de dados", lines.Count >= 20, lines.Count + " linhas");
            foreach (var l in lines)
            {
                var p = l.Split('\t');
                check($"Cultura {name}: {p.ElementAtOrDefault(1)}", p[0] == "OK", p.ElementAtOrDefault(2) ?? "");
            }
        }

        // No mesmo processo, com a cultura trocada e reposta no fim: as Regex criadas na chamada (Regex.IsMatch, Regex.Replace)
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            check("Cultura tr-TR (mesmo processo): \"128tick | Retakes\" não é etiqueta de comunidade", ServerNetwork.Tag("128tick | Retakes") == null, ServerNetwork.Tag("128tick | Retakes") ?? "");
            check("Cultura tr-TR (mesmo processo): \"RNDIS,ADB\" é tethering ligado", Adb.TetherInUsbState("RNDIS,ADB"), "");
            var line = SteamLoginWindow.FailedLine("Wrong password for MIKE_main.", "mike_main");
            check("Cultura tr-TR (mesmo processo): o login noutra caixa sai da linha do Log", !line.Contains("MIKE", StringComparison.OrdinalIgnoreCase), line);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }

        Guarda(check);
    }

    /// <summary>"--cultura nome": o processo inteiro na cultura pedida, como num Windows com esse formato regional.</summary>
    public static int Child(string name)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        var fails = 0;
        try
        {
            var c = new CultureInfo(name);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = c;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = c;
            Console.OutputEncoding = Encoding.UTF8;
            Dados((n, ok, detail) =>
            {
                Console.WriteLine($"{(ok ? "OK" : "FALHA")}\t{n}\t{(detail ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}");
                if (!ok) fails++;
            });
        }
        catch (Exception ex) { Console.WriteLine("FALHA\texceção\t" + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ')); fails++; }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = null;
        }
        return fails;
    }

    /// <summary>O que a app lê e escreve como dados dá o mesmo em qualquer cultura do Windows.</summary>
    static void Dados(Action<string, bool, string> check)
    {
        // Servidores: "128tick" é uma palavra genérica, não o nome de uma comunidade (dois servidores sem relação não partilham bans)
        foreach (var n in new[] { "128tick | Retakes", "128Tick • FFA", "64tick - Surf", "128TICK | Retakes", "128hz | Arena" })
            check($"servidor \"{n}\" sem etiqueta de comunidade", ServerNetwork.Tag(n) == null, ServerNetwork.Tag(n) ?? "");
        check("dois servidores \"128tick\" sem relação não são a mesma comunidade",
            !ServerNetwork.Same("203.0.113.10:27015", "128tick | Retakes EU", null, "198.51.100.20:27015", "128tick | Surf Brasil", null), "");
        check("a etiqueta de uma comunidade com i continua igual (\"[EU] Titan.gg • Retakes\")", ServerNetwork.Tag("[EU] Titan.gg • Retakes") == "TITAN.GG", ServerNetwork.Tag("[EU] Titan.gg • Retakes") ?? "");

        // Telemóvel: o nome do adaptador e do dispositivo USB em qualquer caixa
        foreach (var d in new[] { "ANDROID USB Ethernet", "usb rndis gadget", "Remote Ndis Compatible Device", "APPLE MOBILE DEVICE ETHERNET", "Apple IPHONE", "remote ndis based internet sharing device" })
            check($"adaptador \"{d}\" é o telemóvel", Net.LooksLikePhone(d) && Net.IsPhoneAdapter(d, "Ethernet 5", false, null, null), "");
        check("um adaptador USB de rede que não é telemóvel continua a não ser", !Net.LooksLikePhone("Realtek USB GbE Family Controller") && Net.LooksLikeUsbNet("Realtek USB GbE Family Controller"), "");
        var vpn = typeof(Net).GetField("VpnPattern", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Regex;
        check("VPN: \"WIREGUARD Tunnel\", \"wintun userspace tunnel\" e \"TAILSCALE\" são VPN", vpn != null && vpn.IsMatch("WIREGUARD Tunnel") && vpn.IsMatch("wintun userspace tunnel") && vpn.IsMatch("TAILSCALE"), "");
        check("adb: sys.usb.state \"RNDIS,ADB\" é tethering ligado", Adb.TetherInUsbState("RNDIS,ADB") && Adb.TetherInUsbState("rndis,adb") && !Adb.TetherInUsbState("mtp,adb"), "");
        check("adb: \"ERROR: DEVICE OFFLINE\" é o telemóvel que saiu", Adb.IsGone("ERROR: DEVICE OFFLINE") && Adb.IsGone("error: device unauthorized.") && !Adb.IsGone("error: closed"), "");
        var google = PhoneUsb.Summarize(new[] { new UsbDevice(@"usb\vid_18d1&pid_4ee7\0a1b2c3d", "Pixel 7", null) });
        check("USB: o ID em minúsculas (usb\\vid_18d1&pid_...) é o telemóvel", google.Seen && google.Vendor == "Google", google.ToString());
        var samsung = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_04E8&PID_6860\R58M000000", "SAMSUNG MOBILE USB COMPOSITE DEVICE", 28) });
        check("USB: \"SAMSUNG MOBILE USB COMPOSITE DEVICE\" é o telemóvel (e o código do problema passa)", samsung.Seen && samsung.Vendor == "Samsung" && samsung.Problem == 28, samsung.ToString());
        var keyboard = PhoneUsb.Summarize(new[] { new UsbDevice(@"USB\VID_04E8&PID_1234\X", "Samsung Wireless Keyboard", null) });
        check("USB: um teclado da Samsung continua a não ser telemóvel", !keyboard.Seen, keyboard.ToString());
        var iphone = PhoneUsb.IphoneDeviceId(new[] { new UsbDevice(@"usb\vid_05ac&pid_12a8\00008030001c2d4e", "APPLE IPHONE", null) });
        check("USB: o iPhone com o ID em minúsculas e o nome em maiúsculas é encontrado", iphone == @"usb\vid_05ac&pid_12a8\00008030001c2d4e", iphone ?? "");

        // Esconder dados: logins, IDs e cookies em qualquer caixa
        var failed = SteamLoginWindow.FailedLine("Wrong password for NIGHTFALL_main.", "nightfall_main");
        check("login noutra caixa escondido na linha do Log (\"NIGHTFALL_main\")", !failed.Contains("NIGHTFALL", StringComparison.OrdinalIgnoreCase) && failed.Contains("(hidden)"), failed);
        var cookies = Secrets.Clean("Cookie: SESSIONID=abc123; SteamLOGINSecure=7656%7C%7Ctoken; STEAMMACHINEAUTH7656=ffee");
        check("cookies do Steam em maiúsculas escondidos (SESSIONID, STEAMMACHINEAUTH)", !cookies.Contains("abc123") && !cookies.Contains("%7C%7C") && !cookies.Contains("ffee"), cookies);
        var link = Rec.ScrubIds("perfil HTTPS://STEAMCOMMUNITY.COM/ID/joaosilva e HTTPS://STEAMCOMMUNITY.COM/PROFILES/abc", null);
        check("relatório: o endereço do perfil em maiúsculas sai (STEAMCOMMUNITY.COM/ID/...)", !link.Contains("joaosilva") && !link.Contains("/abc"), link);
        var folder = Rec.ScrubIds(@"D:\APP\CONFIGS\BACKUPS\87654321\cfg", null);
        check("relatório: a pasta da conta sai de um caminho em maiúsculas (\\CONFIGS\\BACKUPS\\)", !folder.Contains("87654321"), folder);
        var screen = Rec.Scrub("link STEAMCOMMUNITY.COM/ID/joaosilva");
        check("gravação: o endereço do perfil em maiúsculas é tapado", !screen.Contains("joaosilva"), screen);

        // Steam: ligações de perfil coladas em qualquer caixa
        check("perfil colado em maiúsculas (/PROFILES/)", PlayerMatch.ParseProfile("HTTPS://STEAMCOMMUNITY.COM/PROFILES/76561198000000042").SteamId == "76561198000000042", "");
        check("perfil colado com /ID/ em maiúsculas", PlayerMatch.ParseProfile("https://SteamCommunity.com/ID/gaben").Vanity == "gaben", PlayerMatch.ParseProfile("https://SteamCommunity.com/ID/gaben").Vanity ?? "");

        // O registo.log: a data de cada linha é sempre "2026-11-25 14:30:05", o formato que a app lê de volta para o limite de 14 dias
        var at = new DateTime(2026, 11, 25, 14, 30, 5);
        var written = typeof(Log).GetMethod("Line", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { new LogEntry { Time = at, Level = LogLevel.Info, Text = "x" } }) as string;
        check("registo.log: a linha começa por \"2026-11-25 14:30:05 [Info] x\"", written != null && written.StartsWith("2026-11-25 14:30:05 [Info] x", StringComparison.Ordinal), written?.Trim() ?? "(sem Log.Line)");
        check("registo.log: a data escrita lê-se de volta", written != null && written.Length >= 19
            && DateTime.TryParseExact(written[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var back) && back == at, "");

        // O que já estava certo e tem de continuar: números com ponto, durações, o console do CS2
        check("dados móveis: \"1.24 GB\" e \"8.5 MB\" com ponto", DataMeter.Text(1_240_000_000) == "1.24 GB" && DataMeter.Text(8_500_000) == "8.5 MB", DataMeter.Text(1_240_000_000));
        check("duração \"1,5\" e \"1.5\" são hora e meia; \"3D 4H\" são 76 horas", Fmt.ParseDuration("1,5") == TimeSpan.FromMinutes(90) && Fmt.ParseDuration("1.5") == TimeSpan.FromMinutes(90) && Fmt.ParseDuration("3D 4H") == TimeSpan.FromHours(76), "");
        check("console do CS2: \"banned for 30 MINUTES\" são 30 minutos", Cs2Console.MinutesIn("You have been banned for 30 MINUTES") == 30, Cs2Console.MinutesIn("You have been banned for 30 MINUTES")?.ToString() ?? "null");
        check("a etiqueta em maiúsculas não ganha o İ turco (\"titan\" dá \"TITAN\")", ServerNetwork.Tag("titan • Retakes") == "TITAN", ServerNetwork.Tag("titan • Retakes") ?? "");
    }

    // ------------------------------------------------------------ Guarda: o código de src/ não volta a depender da cultura

    /// <summary>Linhas que podem usar a cultura do Windows, com a razão: o ficheiro e um pedaço da linha.</summary>
    static readonly (string File, string Has, string Why)[] Allowed =
    {
        // Texto para o utilizador ler (a razão da cópia, já traduzida): a maiúscula segue a cultura como o resto do ecrã
        ("MainWindow.Configs.cs", "char.ToUpper(reason[0])", "texto visível"),
    };

    static void Guarda(Action<string, bool, string> check)
    {
        var src = Path.Combine(SecurityTests.Repo(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).ToList();
        var regex = new List<string>(); var inline = new List<string>(); var casing = new List<string>(); var prefix = new List<string>();
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var text = File.ReadAllText(f);
            string Where(int i) => $"{name}:{text.Take(i).Count(c => c == '\n') + 1}";
            bool Ok(int i)
            {
                var start = text.LastIndexOf('\n', Math.Max(0, i - 1)) + 1;
                var end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                var line = text[start..end];
                return line.TrimStart().StartsWith("//", StringComparison.Ordinal) || Allowed.Any(a => a.File == name && line.Contains(a.Has, StringComparison.Ordinal));
            }

            // 1. RegexOptions.IgnoreCase só com RegexOptions.CultureInvariant na mesma instrução (do ";" anterior ao seguinte)
            foreach (Match m in Regex.Matches(text, @"RegexOptions\.IgnoreCase"))
            {
                var start = text.LastIndexOf(';', m.Index) + 1;
                var end = text.IndexOf(';', m.Index); if (end < 0) end = text.Length;
                if (!text[start..end].Contains("RegexOptions.CultureInvariant", StringComparison.Ordinal) && !Ok(m.Index)) regex.Add(Where(m.Index));
            }
            // 2. "(?i)" dentro de um padrão usa a cultura do momento: a opção tem de vir por RegexOptions, com CultureInvariant
            foreach (Match m in Regex.Matches(text, @"\(\?[a-z]*i[a-z]*(-[a-z]+)?[:)]"))
                if (!Ok(m.Index)) inline.Add(Where(m.Index));
            // 3. Maiúsculas e minúsculas: ToUpperInvariant/ToLowerInvariant (em turco "i".ToUpper() é "İ")
            foreach (Match m in Regex.Matches(text, @"\.To(Upper|Lower)\(\)|\bchar\.To(Upper|Lower)\(|ToTitleCase"))
                if (!Ok(m.Index)) casing.Add(Where(m.Index));
            // 4. StartsWith/EndsWith de texto sem StringComparison comparam pela cultura: Ordinal (um carácter '…' já é ordinal)
            foreach (Match m in Regex.Matches(text, @"\.(StartsWith|EndsWith)\("))
            {
                var args = Args(text, m.Index + m.Length);
                if (args.Contains("StringComparison.", StringComparison.Ordinal) || Regex.IsMatch(args.Trim(), @"^'(\\.|[^'\\])'$") || Ok(m.Index)) continue;
                prefix.Add(Where(m.Index));
            }
        }
        check("Guarda de cultura: leu o código de src/", files.Count >= 80, files.Count + " ficheiros");
        check("Guarda de cultura: nenhum RegexOptions.IgnoreCase sem RegexOptions.CultureInvariant na mesma chamada", regex.Count == 0, regex.Count + ": " + string.Join(", ", regex.Take(30)));
        check("Guarda de cultura: nenhum \"(?i)\" dentro de um padrão", inline.Count == 0, string.Join(", ", inline.Take(10)));
        check("Guarda de cultura: nenhum ToUpper()/ToLower() sem Invariant fora de texto para o ecrã", casing.Count == 0, string.Join(", ", casing.Take(10)));
        check("Guarda de cultura: nenhum StartsWith/EndsWith de texto sem StringComparison", prefix.Count == 0, prefix.Count + ": " + string.Join(", ", prefix.Take(40)));

        // A guarda vê o que tem de ver (um exemplo de cada erro) e deixa passar o que está certo
        check("Guarda de cultura: os argumentos de uma chamada leem-se até ao parêntese certo",
            Args("x.StartsWith(\"a)\\\"b\", StringComparison.Ordinal) && y", 13) == "\"a)\\\"b\", StringComparison.Ordinal"
            && Args("p.StartsWith(@\"\\\\?\\\") && q", 13) == "@\"\\\\?\\\"" && Args("s.EndsWith(')') || t", 11) == "')'" && Args("s.StartsWith(F(a, b)) ? 1 : 2", 13) == "F(a, b)", "");
    }

    /// <summary>Os argumentos de uma chamada a partir do carácter a seguir ao "(": até ao ")" que a fecha, sem contar o que está dentro de textos.</summary>
    static string Args(string text, int from)
    {
        var depth = 0;
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                for (i += 2; i < text.Length; i++)
                    if (text[i] == '"') { if (i + 1 < text.Length && text[i + 1] == '"') i++; else break; }
            }
            else if (c == '"') { for (i++; i < text.Length && text[i] != '"'; i++) if (text[i] == '\\') i++; }
            else if (c == '\'') { for (i++; i < text.Length && text[i] != '\''; i++) if (text[i] == '\\') i++; }
            else if (c == '(') depth++;
            else if (c == ')') { if (depth == 0) return text[from..i]; depth--; }
        }
        return text[from..];
    }
}
