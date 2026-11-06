using System.IO;
using SRT;
using SRT.Core;

// Beta.12 (6 nov.): o refactor do untangle-codebase (publish/untangle-2026-11-06/registo.md). Testes de comportamento: cada um
// falhava antes da sua correção.
static class Untangle12Tests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        await MetricBack(check);
        IphoneOnWifi(check);
        StoppedClock(check);
        RouteKeys(check);
        OursOnTheExit(check);
        await RouteNotDeleted(check);
        PhoneLinkState(check);
        AppNames(check);
        await Cs2OpenAndClose(check);
        TypedValues(check);
        ExplicitState(check);
        KeyValuesSame(check);
        SteamIdsAndLinks(check);
        OneSameRule(check);
        ConfigFailuresSaid(check);
        OneLinkedText(check);
        await PictureNotReadSaid(check);
        SendFailuresSaid(check);
    }

    // Os textos "Linked": os Configs dizem o mesmo que a troca de conta quando a pasta do Steam é uma junção (com o porquê)
    static void OneLinkedText(Action<string, bool, string> check)
    {
        const string A = "76561198000000031", B = "76561198000000032";
        const string KeysRel = @"local\cfg\cs2_user_keys_0_slot0.vcfg";
        var tmp = Path.Combine(Path.GetTempPath(), "srt-linked-" + Guid.NewGuid().ToString("N")[..8]);
        var ud = Path.Combine(tmp, "Steam", "userdata");
        string Dir(string id) => Path.Combine(ud, Cs2Config.Id32(id), "730");
        var keys = Cs2Config.Crlf("\"config\"\n{\n\t\"bindings\"\n\t{\n\t\t\"MOUSE4\"\t\t\"+jump\"\n\t}\n}\n");
        Directory.CreateDirectory(Path.Combine(Dir(A), "local", "cfg"));
        File.WriteAllText(Path.Combine(Dir(A), KeysRel), keys);
        // A pasta 730 da conta B é uma junção para fora do userdata (mklink /J não precisa de admin)
        var outside = Path.Combine(tmp, "fora");
        Directory.CreateDirectory(Path.Combine(outside, "local", "cfg"));
        Directory.CreateDirectory(Path.GetDirectoryName(Dir(B))!);
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/c mklink /J \"{Dir(B)}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        mk?.WaitForExit(10000);
        if (!Directory.Exists(Dir(B))) { Console.WriteLine("SALTO Linked: não deu para criar a junção"); return; }
        var store = new Cs2ConfigStore(ud, Path.Combine(tmp, "configs")) { Cs2Running = () => false };
        var r = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" }, Reason = CfgTrigger.Copy }, "l1");
        check("Linked: uma cópia para uma junção é recusada com o texto da troca de conta (diz porquê)",
            r.Outcome == CfgOutcome.Failed && r.Detail == SafeFile.SteamLinked && SafeFile.SteamLinked.EndsWith("the app only writes to a real folder"), r.Detail ?? r.Outcome.ToString());
        check("Linked: e nada foi escrito do outro lado da junção", !File.Exists(Path.Combine(outside, KeysRel)), "");
    }

    // Os catch {} dos fluxos principais: o Restore que dizia que correu bem, o ReadText que tomava um erro por "não há ficheiro"
    static void ConfigFailuresSaid(Action<string, bool, string> check)
    {
        const string A = "76561198000000021", B = "76561198000000022";
        const string KeysRel = @"local\cfg\cs2_user_keys_0_slot0.vcfg", ConvRel = @"local\cfg\cs2_user_convars_0_slot0.vcfg";
        var keys = Cs2Config.Crlf("\"config\"\n{\n\t\"bindings\"\n\t{\n\t\t\"MOUSE4\"\t\t\"+jump\"\n\t}\n}\n");
        var conv = Cs2Config.Crlf("\"config\"\n{\n\t\"convars\"\n\t{\n\t\t\"sensitivity\"\t\t\"2\"\n\t}\n}\n");
        var tmp = Path.Combine(Path.GetTempPath(), "srt-b8r-" + Guid.NewGuid().ToString("N")[..8]);
        var ud = Path.Combine(tmp, "Steam", "userdata");
        string P(string id, string rel) => Path.Combine(ud, Cs2Config.Id32(id), "730", rel);
        void Put(string id, string rel, string text) { Directory.CreateDirectory(Path.GetDirectoryName(P(id, rel))!); File.WriteAllText(P(id, rel), text); }
        var logged = new List<string>();
        void OnLog(LogEntry e) { lock (logged) logged.Add(e.Text); }
        Log.Added += OnLog;
        try
        {
            Put(A, KeysRel, keys);
            Put(A, ConvRel, conv);
            Put(B, ConvRel, conv);
            var store = new Cs2ConfigStore(ud, Path.Combine(tmp, "configs")) { Cs2Running = () => false };

            // Um ficheiro que existe mas não se lê (preso por outro programa) não é "não há ficheiro"
            using (new FileStream(P(A, KeysRel), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var set = store.Read(A);
                check("Catch: uma config que não se lê não aparece como meia config (antes: sem binds e sem aviso)", set.Empty && logged.Any(l => l.Contains("couldn't be read")), string.Join(",", set.Files.Keys));
            }
            check("Catch: e volta a ler-se quando o ficheiro solta", store.Read(A).Get(CfgPart.Keys) == keys, "");

            // O Restore apaga o que a cópia criou; se não consegue, diz (antes dizia que correu bem)
            var r = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" }, Reason = CfgTrigger.Copy }, "s1");
            var bk = store.Backups().First(b => b.SteamId == B);
            bool threw;
            using (new FileStream(P(B, KeysRel), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { store.Restore(bk, "s2"); threw = false; } catch (IOException) { threw = true; }
            }
            check("Catch: o Restore que não apaga o ficheiro criado diz que falhou", r.Outcome == CfgOutcome.Copied && bk.Created.Contains(KeysRel) && threw && File.Exists(P(B, KeysRel)), $"{r.Outcome} {threw}");
            var again = store.Restore(store.Backups().Where(b => b.SteamId == B && b.Reason == CfgTrigger.Copy).OrderBy(b => b.At).First(), "s3");
            check("Catch: com o ficheiro solto o mesmo Restore apaga-o", again && !File.Exists(P(B, KeysRel)), "");

            // Só de leitura: parado antes de escrever (antes o Delete falhava calado)
            store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "binds" }, Reason = CfgTrigger.Copy }, "s4");
            var bk2 = store.Backups().Where(b => b.SteamId == B && b.SyncId == "s4").First();
            File.SetAttributes(P(B, KeysRel), FileAttributes.ReadOnly);
            string why = null;
            try { store.Restore(bk2, "s5"); } catch (IOException ex) { why = ex.Message; }
            File.SetAttributes(P(B, KeysRel), FileAttributes.Normal);
            check("Catch: um ficheiro criado só de leitura para o Restore antes de escrever", why?.Contains("read-only") == true && File.Exists(P(B, KeysRel)), why ?? "sem erro");
        }
        finally
        {
            Log.Added -= OnLog;
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    sealed class SteamFake : System.Net.Http.HttpMessageHandler
    {
        public Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> Answer;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(Answer(req) ?? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    // O avatar atual que não se lê: a cópia para desfazer fica sem imagem, e agora o Log diz
    static async Task PictureNotReadSaid(Action<string, bool, string> check)
    {
        const string Id = "76561198000000031";
        var fakeSignIn = ProfileTests.FakeToken(Id, marker: "B8");   // made up by the test, signed by nobody
        var cut = false;   // true: the picture server's connection drops (an exception), else it answers 500
        static System.Net.Http.HttpResponseMessage Body(string text, string type) => new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(text, System.Text.Encoding.UTF8, type) };
        static string Html(object o) => "<html><body><div id=\"profile_edit_config\" data-profile-edit=\"" + System.Net.WebUtility.HtmlEncode(System.Text.Json.JsonSerializer.Serialize(o)) + "\" data-profile-badges=\"{}\"></div></body></html>";
        var fake = new SteamFake
        {
            Answer = req =>
            {
                var u = req.RequestUri!;
                if (u.AbsolutePath == "/jwt/finalizelogin")
                    return Body($"{{\"steamID\":\"{Id}\",\"transfer_info\":[{{\"url\":\"https://steamcommunity.com/login/settoken\",\"params\":{{\"nonce\":\"n\",\"auth\":\"a\"}}}}]}}", "application/json");
                if (u.AbsolutePath == "/login/settoken")
                {
                    var r = Body("{\"result\":1}", "application/json");
                    r.Headers.Add("Set-Cookie", $"steamLoginSecure={Id}%7C%7Cacc; Path=/; Secure; HttpOnly");
                    return r;
                }
                if (u.AbsolutePath == "/my") { var r = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found); r.Headers.Location = new Uri($"https://steamcommunity.com/profiles/{Id}/"); return r; }
                if (u.AbsolutePath == $"/profiles/{Id}/edit/info")
                    return Body(Html(new { strPersonaName = "old", strRealName = "", strSummary = "", strCustomURL = "", LocationData = new { locCountryCode = "", locStateCode = "", locCityCode = "" } }), "text/html");
                if (u.AbsolutePath == $"/profiles/{Id}/edit/settings")
                    return Body(Html(new { Privacy = new { PrivacySettings = new { PrivacyProfile = 3, PrivacyInventory = 3, PrivacyInventoryGifts = 1, PrivacyOwnedGames = 3, PrivacyPlaytime = 3, PrivacyFriendsList = 3 }, eCommentPermission = 1 } }), "text/html");
                if (u.AbsolutePath == $"/profiles/{Id}/" && u.Query == "?xml=1")
                    return Body($"<?xml version=\"1.0\"?><profile><steamID64>{Id}</steamID64><steamID><![CDATA[old]]></steamID><avatarFull><![CDATA[https://avatars.steamstatic.com/abc_full.jpg]]></avatarFull></profile>", "text/xml");
                if (u.Host == "avatars.steamstatic.com")
                    return cut ? throw new System.Net.Http.HttpRequestException("reset") : new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                if (u.AbsolutePath == $"/profiles/{Id}/edit/") return Body("{\"success\":1,\"errmsg\":\"\"}", "application/json");
                return null;
            },
        };
        var tmp = Path.Combine(Path.GetTempPath(), "srt-b8p-" + Guid.NewGuid().ToString("N")[..8]);
        var logged = new List<string>();
        void OnLog(LogEntry e) { lock (logged) logged.Add(e.Text); }
        Log.Added += OnLog;
        try
        {
            var snaps = new SnapshotStore(Path.Combine(tmp, "snapshots"));
            var applier = new ProfileApplier(new SteamWeb(fake), id => id == Id ? fakeSignIn : null, snaps, (_, _) => Task.CompletedTask);
            var jobs = ProfilePlan.Make(new ProfileTemplate { Name = "nv" }, ProfileFields.Name, new List<ProfileTarget> { new(Id, "loginb8", "old") }, null, false);
            var res = await applier.Apply(jobs, null, null, default);
            check("Catch: o avatar atual que o Steam não manda fica no Log (a cópia para desfazer fica sem imagem)",
                res[0].Ok && snaps.Original(Id)?.Avatar == null && logged.Any(l => l.Contains("current picture of an account couldn't be read (Steam's picture server didn't send it)") && l.Contains("undo copy")), res[0].Message);
            cut = true;
            var res2 = await applier.Apply(jobs, null, null, default);
            check("Catch: e o que falha pela rede também", res2[0].Ok && logged.Any(l => l.Contains("current picture of an account couldn't be read (Couldn't reach Steam")), res2[0].Message);
        }
        finally
        {
            Log.Added -= OnLog;
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    // A telemetria e os servidores que falham em segundo plano: uma linha por tipo de falha, não uma a cada 5 minutos
    static void SendFailuresSaid(Action<string, bool, string> check)
    {
        var logged = new List<string>();
        void OnLog(LogEntry e) { lock (logged) logged.Add(e.Text); }
        Log.Added += OnLog;
        try
        {
            const string Url = "https://stats.example/v1-test-b8";
            Telemetry.NotSent(Url, "the server answered 503");
            Telemetry.NotSent(Url, "the server answered 503");
            Telemetry.NotSent(Url + "/live", "the server answered 503");
            Telemetry.NotSent(Url, Telemetry.Kind(new TaskCanceledException()));
            var first = logged.Count(l => l.StartsWith("Usage stats:"));
            Telemetry.SentOk(Url);
            Telemetry.NotSent(Url, "the server answered 503");
            Telemetry.NotSent(Url + "/live", "the server answered 503");
            var after = logged.Count(l => l.StartsWith("Usage stats:"));
            check("Catch: as estatísticas que não saem ficam no Log, uma vez por tipo de falha", first == 3 && after == 4
                && logged.Any(l => l.Contains("wasn't sent (no answer in time)")), string.Join(" | ", logged));
            check("Catch: os tipos de falha não levam endereços", Telemetry.Kind(new System.Net.Http.HttpRequestException("https://x/ boom")) == "no connection"
                && Telemetry.Kind(new InvalidOperationException("C:\\Users\\Ana")) == "InvalidOperationException", "");
            check("Catch: um servidor que não responde não é falha; uma resposta estragada é",
                A2S.NoAnswer(new OperationCanceledException()) && A2S.NoAnswer(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))
                && !A2S.NoAnswer(new InvalidDataException("Invalid A2S response")) && !A2S.NoAnswer(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.AddressNotAvailable)), "");
        }
        finally { Log.Added -= OnLog; }
    }

    // B8: o mapa categoria → ficheiro e a regra de "igual" escritos uma vez (a cópia e o Compare usavam regras diferentes)
    static void OneSameRule(Action<string, bool, string> check)
    {
        check("B8: cada categoria no seu ficheiro, uma vez", Cs2Config.Categories.SequenceEqual(new[] { "binds", "crosshair", "game", "video", "hud" })
            && Cs2Config.Categories.Where(Cs2Config.WholeFile).SequenceEqual(new[] { "binds", "video", "hud" })
            && Cs2Config.CategoryOf(CfgPart.Keys, "MOUSE4") == "binds" && Cs2Config.CategoryOf(CfgPart.Convars, "cl_crosshairsize") == "crosshair"
            && Cs2Config.CategoryOf(CfgPart.Convars, "sensitivity") == "game" && Cs2Config.CategoryOf(CfgPart.Machine, "cl_hud_color") == "hud"
            && Cs2Config.Categories.All(c => Cs2Config.PartsOf(c).Length == 1 && Cs2Config.CategoryOf(Cs2Config.PartsOf(c)[0], c == "crosshair" ? "cl_crosshairsize" : "x") == c), "");

        const string A = "76561198000000001", B = "76561198000000002";
        static string Conv(params (string K, string V)[] kv) => Cs2Config.Crlf("\"config\"\n{\n\t\"convars\"\n\t{\n" + string.Concat(kv.Select(x => $"\t\t\"{x.K}\"\t\t\"{x.V}\"\n")) + "\t}\n}\n");
        var tmp = Path.Combine(Path.GetTempPath(), "srt-b8c-" + Guid.NewGuid().ToString("N")[..8]);
        var ud = Path.Combine(tmp, "Steam", "userdata");
        void Put(string id, string text)
        {
            var p = Path.Combine(ud, Cs2Config.Id32(id), "730", @"local\cfg\cs2_user_convars_0_slot0.vcfg");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }
        string Get(string id) => File.ReadAllText(Path.Combine(ud, Cs2Config.Id32(id), "730", @"local\cfg\cs2_user_convars_0_slot0.vcfg"));
        try
        {
            // Os mesmos valores, escritos de outra maneira (um CS2 mais antigo grava "1.000000") e por outra ordem
            Put(A, Conv(("cl_crosshairsize", "2.000000"), ("cl_crosshairgap", "-3"), ("sensitivity", "1.250000")));
            var bText = Conv(("sensitivity", "1.25"), ("cl_crosshairgap", "-3"), ("cl_crosshairsize", "2"));
            Put(B, bText);
            var store = new Cs2ConfigStore(ud, Path.Combine(tmp, "configs")) { Cs2Running = () => false };
            var diff = Cs2Config.Diff(store.Read(A), store.Read(B));
            var r = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair" }, Reason = CfgTrigger.Copy }, "s1");
            var r2 = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair", "game" }, Reason = CfgTrigger.Copy }, "s2");
            check("B8: o que o Compare mostra igual a cópia não reescreve (antes copiava e dizia Copied)", diff.Count == 0 && r.Outcome == CfgOutcome.Same && r2.Outcome == CfgOutcome.Same
                && Get(B) == bText && store.Backups().Count == 0, $"{diff.Count} {r.Outcome} {r2.Outcome}");
            Put(A, Conv(("cl_crosshairsize", "3"), ("cl_crosshairgap", "-3"), ("sensitivity", "1.250000")));
            var r3 = store.Push(new CfgJob { SourceId = A, Target = B, Categories = new() { "crosshair" }, Reason = CfgTrigger.Copy }, "s3");
            check("B8: o que muda continua a copiar-se", r3.Outcome == CfgOutcome.Copied && Cs2Config.Flatten(Get(B), CfgPart.Convars)["cl_crosshairsize"] == "3"
                && Cs2Config.Diff(store.Read(A), store.Read(B)).Count == 0, r3.Outcome.ToString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // B8: um só SteamIds (o regex e a base escritos uma vez) e uma só verificação de junções no SafeFile
    static void SteamIdsAndLinks(Action<string, bool, string> check)
    {
        check("B8: SteamIds aceita só SteamID64 reais", SteamIds.IsId64("76561198000000001") && !SteamIds.IsId64(null) && !SteamIds.IsId64("")
            && !SteamIds.IsId64("7656119800000000") && !SteamIds.IsId64("76561198000000001\n") && !SteamIds.IsId64(" 76561198000000001") && !SteamIds.IsId64("86561198000000001"), "");
        check("B8: id32 e SteamID64 vão e voltam", SteamIds.Id32("76561198000000001") == "39734273" && SteamIds.FromId32(39734273) == "76561198000000001"
            && Cs2Config.Id32("76561198000000001") == "39734273" && SteamIds.Id32("76561197960265728") == null && SteamIds.Id32("x") == null, SteamIds.Id32("76561198000000001"));
        check("B8: as mesmas regras nos sítios que leem ids", PlayerMatch.ParseProfile("76561198000000001").SteamId == "76561198000000001"
            && ProfileLink.Path("76561198000000001") == "/profiles/76561198000000001" && Steam.ShownName("76561198000000001") == null && Steam.ShownName("Ana") == "Ana"
            && Steam.Cs2FriendCode("1234") == null && Steam.Cs2FriendCode("76561198000000001") != null, "");
        check("B8: o User-Agent da app vai no fim dos dois do Steam", SteamWeb.UserAgent.EndsWith(" CS2IPChanger/0.1") && Steam.ProfileUserAgent == "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CS2IPChanger/0.1", "");

        var tmp = Path.Combine(Path.GetTempPath(), "srt-b8-" + Guid.NewGuid().ToString("N")[..8]);
        var real = Path.Combine(tmp, "real");
        var link = Path.Combine(tmp, "link");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "f.txt"), "x");
        try
        {
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{real}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }))
                p!.WaitForExit(10000);
            var made = Directory.Exists(link);
            check("B8: SafeFile.Linked vê a junção e deixa passar pastas, ficheiros e caminhos que não existem",
                !made || (SafeFile.Linked(real, Path.Combine(real, "f.txt"), Path.Combine(tmp, "nada")) == false && SafeFile.Linked(real, link) && SafeFile.Linked(Path.Combine(link, "f.txt"), link)),
                made ? "" : "sem junção");
        }
        finally
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(tmp, true); } catch { }
        }
    }

    // B8: um só leitor de KeyValues (Steam com escapes, CS2 sem). As impressões digitais são da saída de antes da junção (HEAD 12a3a2b),
    // tiradas com este mesmo código: o que o Steam e o CS2 leem e o que a app escreve (Write, RoundTrips, launch options,
    // loginusers com MarkForLogin, Flatten, MergeConvars, KeepDisplay) fica igual ao byte.
    static readonly string[] KvSamples =
    {
        "\"users\"\n{\n\t\"76561198000000001\"\n\t{\n\t\t\"AccountName\"\t\t\"ana\"\n\t\t\"PersonaName\"\t\t\"a\\\"b\\\\c\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"timestamp\"\t\t\"1793308000\"\n\t}\n\t\"76561198000000002\"\n\t{\n\t\t\"AccountName\"\t\t\"bia\"\n\t\t\"AutoLogin\"\t\t\"1\"\n\t}\n}\n",
        "\"users\"\r\n{\r\n\t\"76561198000000001\"\r\n\t{\r\n\t\t\"AccountName\"\t\t\"ana\"\r\n\t\t\"Timestamp\"\t\t\"1\"\r\n\t}\r\n}\r\n",
        "// comment\n\"users\" // trailing\n{\n\t\"a\" \"1\" // c\n\t// \"b\" \"2\"\n}\n",
        "\"users\"\n{\n\t\"a\"\n}\n",
        "\"a\"",
        "  \"b\"   \"2\"\n\"a\"\t\"1\"\n\"c\" { \"x\" \"y\" }",
        "#include \"outro.vdf\"\n\"users\"\n{\n}\n",
        "\"users\"\n{\n\t\"a\"\t\"1\" [$WIN32]\n}\n",
        "\"users\"\n{\n\t\"PersonaName\"\t\"a\\nb\"\n}\n",
        "\"users\"\n{\n}\n}\n\"outro\"\t\"1\"\n",
        "\"config\"\n{\n\t\"bindings\"\n\t{\n\t\t\"\\\"\t\t\"say hi\"\n\t\t\"a\"\t\t\"+left\"\n\t}\n}\n",
        "\"config\"\r\n{\r\n\t\"bindings\"\r\n\t{\r\n\t\t\"\\\"\t\t\"\\\"\r\n\t}\r\n}\r\n",
        "\"a\" { \"}\" \"x\" }",
        "\"a\" \"{\" \"b\" \"c\" }",
        "\"a\" \"b",
        "\"a\" \"b\\",
        "",
        "   \n\t ",
        "abc\"def\" \"x\" \"y\"",
        "\"nome\"\t\"Ána ç ✓\"\n",
        "\"a\"{\"b\"\"c\"}\"d\"\"e\"",
        "\"k\" \"v\" \"lonely\"",
        "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-novid  +fps_max 0 \"\n\t\t\t\t\t\t\"cloud\"\t\t\"x\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n",
        "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"other\"\t\t\"1\"\n\t\t\t}\n\t\t}\n\t}\n}\n",
        "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-condebug -CONCLEARLOG\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n",
        "\"config\"\r\n{\r\n\t\"convars\"\r\n\t{\r\n\t\t\"cl_crosshairsize\"\t\t\"2\"\r\n\t\t\"sensitivity\"\t\t\"1.250000\"\r\n\t\t\"cl_crosshaircolor\"\t\t\"4\"\r\n\t\t\"fps_max_ui$2\"\t\t\"200\"\r\n\t}\r\n}\r\n",
        "\"config\"\n{\n\t\"convars\"\n\t{\n\t\t\"sensitivity\"\t\t\"2\"\n\t\t\"cl_crosshairgap\"\t\t\"-3\"\n\t\t\"volume\"\t\t\"0.5\"\n\t}\n}\n",
        "\"video.cfg\"\n{\n\t\"setting.defaultres\"\t\t\"1280\"\n\t\"setting.defaultresheight\"\t\t\"960\"\n\t\"setting.refreshrate_numerator\"\t\t\"144\"\n\t\"setting.monitor_index\"\t\t\"1\"\n\t\"VendorID\"\t\t\"4318\"\n}\n",
        "\"video.cfg\"\n{\n\t\"setting.defaultres\"\t\t\"1920\"\n\t\"setting.refreshrate_numerator\"\t\t\"60\"\n}\n",
        string.Concat(Enumerable.Repeat("\"a\" {", 40)),
    };

    static readonly string[] KvBefore =
    {
        "BE88300335721EE6", "37AD33A47FA6FE09", "990FC7B1DAF7B785", "69A796092F15817C", "FC65196CE766F32C", "DC97B8AEBFA26D7E",
        "AEFD98D26BF92F0C", "DEB47A8E35CE97FC", "B962AFD0F4B1DC31", "69AE862E3E7853D1", "8524258EBF2ED92D", "BAA173F1013B303D",
        "12ACD40A3810F0D6", "96935D2E307CB4E6", "B2C10905F8EAB941", "2EEBDF2EEC133B0A", "B86CB6E36849B4A1", "B86CB6E36849B4A1",
        "0ED7445AF46AA4F5", "39386D3A48AC26DA", "77484F3091861C61", "399CD08E600DC446", "BBA1A62EBD83C4A6", "9A186A0491041611",
        "E9F2963F7E62EBF1", "A61776DA8CF96D8F", "599896E73643A44C", "C315A85FFD414CED", "D23DAA6A578701D4", "E530316C47DC6E22",
    };
    const string KvPairsBefore = "8CB061A91E611364";

    static string KvTry(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) { return "ERR:" + ex.GetType().Name + ":" + ex.Message; }
    }

    static string KvFlat(Dictionary<string, string> m) => m == null ? "null" : string.Join(";", m.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));

    /// <summary>Tudo o que a app lê ou escreve a partir de um texto, nos dois dialetos.</summary>
    static string KvOf(string t) => string.Join("\u0001", new[]
    {
        KvTry(() => VdfNode.Parse(t).Write()),
        VdfNode.RoundTrips(t).ToString(),
        KvTry(() => Vcfg.Write(Vcfg.Parse(t))),
        Vcfg.RoundTrips(t).ToString(),
        KvTry(() => { var p = Cs2LaunchOptions.Patch(t); return $"{p.Text}|{p.Before}|{p.After}|{p.Changed}|{p.Why}|{p.Already}"; }),
        KvTry(() => string.Join(";", Steam.ParseAccounts(t).Select(a => $"{a.SteamId},{a.AccountName},{a.PersonaName},{a.MostRecent},{a.Timestamp}"))),
        KvTry(() => { var d = VdfNode.Parse(t); if (d.Child("users") is VdfNode u) Steam.MarkForLogin(u, "76561198000000001", true, 1793308000); return d.Write(); }),
        KvTry(() => KvFlat(Cs2Config.Flatten(t, CfgPart.Convars))),
        KvTry(() => KvFlat(Cs2Config.Flatten(t, CfgPart.Video))),
    });

    static string KvSha(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)))[..16];

    static void KeyValuesSame(Action<string, bool, string> check)
    {
        var diff = Enumerable.Range(0, KvSamples.Length).Where(i => KvSha(KvOf(KvSamples[i])) != KvBefore[i]).ToList();
        check("B8: um só leitor de KeyValues lê e escreve igual ao byte a antes (aspas, escapes, comentários, chaves sem valor, ordem, espaços, CRLF)",
            diff.Count == 0, string.Join(",", diff));
        var pairs = new System.Text.StringBuilder();
        foreach (var a in KvSamples)
            foreach (var b in KvSamples.Append(null))
            {
                pairs.Append(KvTry(() => Cs2Config.MergeConvars(a, b, true, false))).Append('\u0002');
                pairs.Append(KvTry(() => Cs2Config.MergeConvars(a, b, false, true))).Append('\u0002');
                pairs.Append(KvTry(() => Cs2Config.KeepDisplay(a, b))).Append('\u0003');
            }
        check("B8: as fusões das configs do CS2 (mira, jogo, ecrã) ficam iguais ao byte", KvSha(pairs.ToString()) == KvPairsBefore, KvSha(pairs.ToString()));
        check("B8: a mesma tecla \\ do CS2 e o mesmo \\\" do Steam",
            Vcfg.Parse(KvSamples[10]).At(new[] { "config", "bindings" })?.Get("\\") == "say hi" && VdfNode.Parse(KvSamples[0]).At(new[] { "users", "76561198000000001" })?.Get("PersonaName") == "a\"b\\c", "");
        var p = Cs2LaunchOptions.Patch(KvSamples[23]);
        check("B8: as launch options leem-se do mesmo caminho em que são escritas", p.Changed && Cs2LaunchOptions.Of(VdfNode.Parse(p.Text)) == "-condebug -conclearlog"
            && Cs2LaunchOptions.Of(VdfNode.Parse(KvSamples[22])) == "-novid  +fps_max 0 ", p.Why);
    }

    // B7: o Core não escreve nas Settings, as contas novas têm uma regra, a lista de contas não perde um Refresh a meio
    static void ExplicitState(Action<string, bool, string> check)
    {
        var s = new Settings { Cs2Path = @"C:\Games\cs2.exe" };
        check("B7: um sync sem nada de novo não mexe nas settings", !MainWindow.FromSync(s, new GuardReport()) && s.Cs2Path == @"C:\Games\cs2.exe" && !s.LegacyCleaned, "");
        check("B7: o cs2.exe noutro sítio e as regras antigas limpas vêm no relatório e a janela guarda-os",
            MainWindow.FromSync(s, new GuardReport { Cs2Found = @"D:\cs2.exe", LegacyCleaned = true }) && s.Cs2Path == @"D:\cs2.exe" && s.LegacyCleaned
            && !MainWindow.FromSync(s, new GuardReport { Cs2Found = @"D:\cs2.exe", LegacyCleaned = true }), s.Cs2Path);
        check("B7: o mesmo cs2.exe não é novidade", !MainWindow.NoteCs2(s, @"D:\cs2.exe") && !MainWindow.NoteCs2(s, null), "");
        var req = new SyncRequest(new[] { "1.2.3.4" }) { Force = true, Distrust = Distrust.SameIp };
        check("B7: o pedido de sync tem nomes, e ligado por defeito", req.On && req.Force && req.Distrust == Distrust.SameIp && !req.Cs2Running && req.VerifiedKey == null, "");

        List<string> known = null;
        var first = MainWindow.NewAccounts(ref known, new[] { "a", "b", "a" });
        check("B7: na 1.ª vez as contas que há só ficam lembradas", first.Count == 0 && known.SequenceEqual(new[] { "a", "b" }), string.Join(",", known));
        var later = MainWindow.NewAccounts(ref known, new[] { "a", "b", "c" });
        check("B7: depois, uma conta que aparece é nova uma vez", later.SequenceEqual(new[] { "c" }) && known.Contains("c") && MainWindow.NewAccounts(ref known, new[] { "a", "b", "c" }).Count == 0, string.Join(",", later));
        var empty = new List<string>();
        check("B7: uma lista vazia já não é a 1.ª vez (a primeira conta num PC sem contas é nova)", MainWindow.NewAccounts(ref empty, new[] { "a" }).SequenceEqual(new[] { "a" }), "");

        // O bug: LoadAccounts trocava os AccountMeta enquanto um Refresh ainda escrevia nos antigos, e o que ele escrevia perdia-se
        var meta = new Dictionary<string, AccountMeta>();
        var locals = new List<LocalAccount> { new("76561198000000001", "l1", "Ana", false, 0), new("76561198000000002", "l2", "Bia", false, 0) };
        var before = MainWindow.AccountItems(locals, meta, null, false, out _);
        var refreshing = before[0];   // o Refresh tem este item na mão
        meta["76561198000000002"].Hidden = true;
        var again = MainWindow.AccountItems(locals, meta, "76561198000000001", false, out var hidden);   // a lista feita de novo a meio (um kick, uma conta nova)
        refreshing.Meta.Profile = new SteamProfile { VacBans = 1 };
        check("B7: a lista feita de novo usa os mesmos dados de cada conta (o Refresh a meio não se perde)",
            ReferenceEquals(again[0].Meta, refreshing.Meta) && again[0].Meta.Profile?.VacBans == 1 && meta["76561198000000001"].Profile?.VacBans == 1, "");
        check("B7: as escondidas contam-se e não se listam", again.Count == 1 && hidden == 1 && again[0].Active, "");

        var tmp = Path.Combine(Path.GetTempPath(), "srt-b7-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var lower = Path.Combine(tmp, "lower.json");
            File.WriteAllText(lower, "{ \"statsaccepted\": true, \"statsnotice\": 3 }");
            check("B7: o aviso lê o settings.json como a app o lê (sem olhar a maiúsculas)", App.StatsNoticeIn(lower) == 3 && Store.Load(lower, () => new Settings()).StatsAccepted, "");
            var broken = Path.Combine(tmp, "broken.json");
            File.WriteAllText(broken, "{ nao é json");
            File.WriteAllText(broken + ".prev", "{ \"StatsAccepted\": true }");
            check("B7: estragado usa o .prev e não escreve nada (nem a cópia .broken)", App.StatsNoticeIn(broken) == 1 && Directory.GetFiles(tmp).Length == 3,
                string.Join(",", Directory.GetFiles(tmp).Select(Path.GetFileName)));
            check("B7: sem ficheiro é \"ainda não\"", App.StatsNoticeIn(Path.Combine(tmp, "none.json")) == 0 && Store.Peek<Settings>(Path.Combine(tmp, "none.json")) == null, "");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }

        check("B7: o 1.º arranque vem do aviso, passado à janela", new App.StatsConsent(true, JustAccepted: true).FirstStart
            && !new App.StatsConsent(true, JustAccepted: true, AskedAgain: true).FirstStart && !new App.StatsConsent(true).FirstStart, "");
    }

    // B6: a app decide por valores tipados, não por cópias dos textos do ecrã; os textos ficam iguais
    static void TypedValues(Action<string, bool, string> check)
    {
        var t = new DateTime(2026, 11, 6, 20, 0, 0);
        const string On = "[Client] CL:  Connected to '203.0.113.10:27015'";
        static string Code(string c, int signOn = 6) => $"[Client] Disconnection during connection phase. Sign-on state: {signOn} (x). Disconnect reason: NETWORK_DISCONNECT_{c}.";
        Disconnect Take(params string[] lines)
        {
            var k = new DisconnectTracker();
            foreach (var l in lines) k.Feed(l, t);
            return k.Take(t);
        }
        var slot = Take(On, "You were kicked to make room for a reserved slot", Code("KICKED"));
        var slotCode = Take(On, Code("REJECT_SERVERFULL", 6));
        check("B6: slot reservado (dito pelo servidor ou pelo código já no jogo) tem o seu motivo, com o texto de sempre",
            slot?.Why == KickReason.ReservedSlot && slotCode?.Why == KickReason.ReservedSlot && slot.Reason == "kicked to make room for a reserved slot" && slotCode.Reason == slot.Reason, $"{slot} {slotCode}");
        var full = Take(On, Code("REJECT_SERVERFULL", 2));
        check("B6: cheio a entrar não é slot reservado", full?.Why == KickReason.Other && full.Reason == "the server is full", $"{full}");
        var never = Take(On, "[Client] Disconnected from server: NETWORK_DISCONNECT_CONNECT_REQUEST_TIMEDOUT");
        var lost = Take(On, "[Client] Disconnected from server: NETWORK_DISCONNECT_TIMEDOUT");
        check("B6: nunca respondeu é um motivo, perder a ligação não", never?.Why == KickReason.NeverAnswered && never.Reason == "the server never answered" && lost?.Why == KickReason.Other, $"{never} {lost}");
        var ban = Take(On, Code("KICKBANADDED"));
        check("B6: ban só pelo código diz \"banned\" e não o repete entre parênteses", ban?.Why == KickReason.Banned && ban.Reason == "banned" && ban.ReasonInBrackets == "", $"{ban}");
        var vote = Take(On, Code("KICKED_VOTEDOFF"));
        check("B6: outro motivo vai entre parênteses", vote?.Why == KickReason.Other && vote.ReasonInBrackets == " (kicked by a vote)", $"{vote}");
        check("B6: um motivo escrito à mão igual ao texto não engana a app", new Disconnect(KickKind.Full, "", null, "kicked to make room for a reserved slot").Why == KickReason.Other
            && new Disconnect(KickKind.Ban, "", null, Why: KickReason.Banned).Reason == "banned", "");

        var vdf = "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-condebug -conclearlog\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";
        var already = Cs2LaunchOptions.Patch(vdf);
        var broken = Cs2LaunchOptions.Patch("\"users\"\n{\n}\n");
        check("B6: opções já lá é um resultado, não um texto", already.Already && !already.Changed && !broken.Already && !broken.Changed, already.Why);
        check("B6: só as contas deixadas por um motivo contam como falha",
            !new Cs2LaunchOptions.AccountResult("1", false, "x", "x", "already there", Already: true).Left && new Cs2LaunchOptions.AccountResult("2", false, null, null, "already there").Left
            && !new Cs2LaunchOptions.AccountResult("3", true, null, null, null).Left, "");

        var row = new ProfileRow(new AccountItem(new LocalAccount("76561198000000001", "l", "Ana", false, 0), new AccountMeta()));
        row.Session = "Sign-in expired";
        var byText = row.SessionLevelNow;
        row.Expired = true;
        check("B6: o âmbar da sessão expirada vem do estado, não do texto", byText == "none" && row.SessionLevelNow == "warn", byText);

        check("B6: os modos dos configs guardam-se com os mesmos valores", new Settings().ConfigMode == "off" && string.Join(",", CfgMode.All) == "off,main,previous,latest", "");
        check("B6: o gatilho \"closed\" só copia em Latest change wins",
            Cs2Config.Plan(CfgMode.Main, "a", null, null, CfgTrigger.Closed, null, new[] { "a", "b" }, new HashSet<string>(), new[] { "binds" }).Count == 0
            && Cs2Config.Plan(CfgMode.Latest, null, null, "a", CfgTrigger.Closed, null, new[] { "a", "b" }, new HashSet<string>(), new[] { "binds" }).Count == 1, "");

        check("B6: o motivo de desconfiança vem da janela como valor",
            MainWindow.DistrustNow(true, true, true) == Distrust.SameIp && MainWindow.DistrustNow(false, true, true) == Distrust.PhoneWifi
            && MainWindow.DistrustNow(false, false, true) == Distrust.NoHome && MainWindow.DistrustNow(false, false, false) == Distrust.None, "");
        var wifi = new GuardReport { Distrust = Distrust.PhoneWifi };
        check("B6: o relatório diz o motivo e o texto de sempre", wifi.Distrusted && wifi.DistrustReason == "The phone's Wi-Fi is on, so it may be sharing your home network instead of mobile data."
            && !new GuardReport().Distrusted && new GuardReport().DistrustReason == null
            && DistrustText.Of(Distrust.Unchecked) == "The phone connection's public IP hasn't been checked yet.", wifi.DistrustReason);

        check("B6: o tempo esgotado do sign-in tem nome", new SteamWebException("x", eresult: 9).SignInTimedOut && new SteamWebException("x", eresult: 27).SignInTimedOut
            && !new SteamWebException("x", eresult: 5).SignInTimedOut, "");

        // O bug: um Firewall que não se lê virava um nome de regra falso e "não sobrou nada"
        var unread = new FirewallRules { Error = "Access is denied." };
        check("B6: Firewall ilegível não é um nome de regra", unread.Count == 0 && unread.ErrorText == "(could not read the Firewall: Access is denied.)", "");
        check("B6: Firewall ilegível nunca é \"não sobrou nada\" (o arranque mantém a proteção com o CS2 aberto)", Firewall.BlockLeft(unread), "");
        check("B6: na janela de Install, ilegível não inventa um bloqueio", !Firewall.BlockLeft(unread, unreadableCounts: false), "");
        check("B6: lido e vazio é mesmo nada", !Firewall.BlockLeft(new FirewallRules()) && Firewall.BlockLeft(new FirewallRules { Firewall.Cs2RuleName }), "");
    }

    // B5: abrir e fechar o CS2 num só ajudante: os argumentos, o carimbo de "fechado pela app" com o motivo, o console.log
    static async Task Cs2OpenAndClose(Action<string, bool, string> check)
    {
        check("B5: o Steam abre o CS2 com o console.log ligado", Cs2Launch.SteamArgs == "-applaunch 730 -condebug -conclearlog", Cs2Launch.SteamArgs);
        check("B5: as opções que a app junta no Steam são as mesmas com que ela abre o CS2",
            Cs2LaunchOptions.Has(Cs2Launch.SteamArgs) && Cs2LaunchOptions.Add("-novid") == "-novid " + string.Join(" ", Cs2Launch.ConsoleFlags), Cs2LaunchOptions.Add("-novid"));
        check("B5: o Steam reaberto leva -silent à frente se for o caso",
            Cs2Launch.SteamRestartArgs(true) == "-silent -applaunch 730 -condebug -conclearlog" && Cs2Launch.SteamRestartArgs(false) == Cs2Launch.SteamArgs, "");

        var now = new DateTime(2026, 11, 6, 20, 0, 0);
        int kills = 0, polls = 0;
        var cs2 = new Cs2Launch(() => now, () => kills++, () => ++polls < 3);
        check("B5: sem nada fechado pela app, uma saída do CS2 é dele", !cs2.ClosedByAppRecently && cs2.ClosedReason == null, "");
        await cs2.Close("the Steam restart");
        check("B5: fechar o CS2 carimba sempre, com o motivo", kills == 1 && cs2.ClosedByAppRecently && cs2.ClosedReason == "the Steam restart", cs2.ClosedReason);
        check("B5: fechar espera que o CS2 saia", polls == 3, polls.ToString());
        now = now.AddSeconds(59);
        check("B5: ainda dentro do minuto conta como da app", cs2.ClosedByAppRecently, "");
        now = now.AddSeconds(2);
        check("B5: passado o minuto uma saída volta a ser do CS2", !cs2.ClosedByAppRecently && cs2.ClosedReason == null, "");
        cs2.Closing("the launch options change");
        check("B5: o Steam a fechar leva o CS2: carimba sem matar nada", kills == 1 && cs2.ClosedReason == "the launch options change", "");

        check("B5: o console.log fica em game\\csgo ao lado do cs2.exe guardado",
            Cs2Launch.ConsoleLog(@"D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe")
            == @"D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log", "");
        check("B5: o menu do CS2 é o loopback, um servidor não",
            Cs2Console.IsMenu("CL:  Connected to 'loopback:0'") && !Cs2Console.IsMenu("CL:  Connected to '203.0.113.9:27015'") && !Cs2Console.IsMenu(null), "");

        var dir = Path.Combine(Path.GetTempPath(), "srt-b5-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var log = Path.Combine(dir, "console.log");
            File.WriteAllText(log, "CL:  Connected to 'loopback:0'\nold line\n");
            var tail = new ConsoleTail();
            tail.StartAt(log);
            check("B5: o que já estava no console.log antes de abrir o CS2 não conta", tail.ReadNew(log).Count == 0, "");
            File.AppendAllText(log, "CL:  Connected to 'loopback:0'\n");
            check("B5: o menu escrito depois de abrir o CS2 conta", tail.ReadNew(log).Any(Cs2Console.IsMenu), "");

            File.WriteAllText(log, new string('x', 200) + "\n");
            tail.StartAt(log);
            File.WriteAllText(log, "CL:  Connected to 'loopback:0'\n");   // -conclearlog: CS2 starts the file again, shorter
            check("B5: um console.log começado de novo lê-se do início", tail.ReadNew(log).Any(Cs2Console.IsMenu), "");

            var later = Path.Combine(dir, "later.log");
            tail.StartAt(later);
            File.WriteAllText(later, "CL:  Connected to 'loopback:0'\n");
            check("B5: sem console.log ao abrir, o que vier lê-se todo", tail.ReadNew(later).Any(Cs2Console.IsMenu), "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // B3: os nomes da app (exe, pastas, ficheiros, argumentos, regras, User-Agent) num só sítio, com os valores de sempre
    static void AppNames(Action<string, bool, string> check)
    {
        var repo = SecurityTests.Repo();
        var csproj = File.ReadAllText(Path.Combine(repo, "src", "ServerRouteTool.csproj"));
        var names = System.Text.RegularExpressions.Regex.Matches(csproj, "<AssemblyName>([^<]+)</AssemblyName>").Select(m => m.Groups[1].Value).ToList();
        check("B3: o exe publicado chama-se como o AssemblyName do csproj", names.Count > 0 && names[0] + ".exe" == AppIds.ExeName, string.Join(", ", names));
        check("B3: o build Recording tem outro nome e não se atualiza sozinho", names.Skip(1).All(n => n != AppIds.Name) && names.Contains(AppIds.Name + "-Recording"), string.Join(", ", names));
        check("B3: o update usa .new.exe e .old com o mesmo nome", AppIds.NewExeName == "CS2IPChanger.new.exe" && AppIds.ExeName + AppIds.OldSuffix == "CS2IPChanger.exe.old", "");

        var cmd = File.ReadAllText(Path.Combine(repo, "tools", "restore.cmd"));
        check("B3: o restore.cmd apaga as regras com o mesmo prefixo da app", cmd.Contains($"-DisplayName '{AppIds.RulePrefix}*'"), "");
        check("B3: todas as regras da app começam pelo prefixo", new[] { Firewall.RuleName, Firewall.EchoRuleName, Firewall.Cs2RuleName, Firewall.DnsUdpRuleName,
            Firewall.DnsTcpRuleName, Firewall.Ipv6RuleName, Firewall.RegionsRuleName }.All(n => n.StartsWith(AppIds.RulePrefix + " - ")) && Firewall.RuleName == "ServerRouteTool v9 - kill switch", "");

        check("B3: a cópia nova recebe o estado da proteção depois de --updated",
            AppIds.Args.ForUpdate(AppIds.Args.UpdateState(true)) == "--updated --on" && AppIds.Args.ForUpdate(AppIds.Args.UpdateState(false)) == "--updated --off"
            && AppIds.Args.ForUpdate(AppIds.Args.Standby) == "--updated --standby", "");
        check("B3: um estado que a cópia nova não conhece não passa", AppIds.Args.ForUpdate(null) == "--updated" && AppIds.Args.ForUpdate("--demo") == "--updated", "");
        check("B3: --restore conta escrito à mão em qualquer caixa, os outros não",
            AppIds.Args.IsRestore(new[] { "--RESTORE" }) && !AppIds.Args.IsRestore(new[] { "--demo", "restore" }) && SRT.Program.DllSearchProblem(false, true, new[] { "--Restore" }) == null, "");
        check("B3: o comando de desfazer é o exe com --restore", AppChanges.RestoreCommand(@"C:\x\" + AppIds.ExeName) == "\"C:\\x\\CS2IPChanger.exe\" --restore", "");
        check("B3: a pasta de instalação é a do Installer, em Program Files",
            Installer.Target == AppIds.InstallDir && AppIds.InstallDir.EndsWith(@"\CS2 IP Changer")
            && SRT.Program.FolderProblem(Path.GetTempPath(), Path.GetTempPath())?.Contains($"create the folder {AppIds.InstallDir}, copy CS2IPChanger.exe into it") == true, "");
        check("B3: a pasta do ProgramData é a mesma para o Uninstall e o Install", Uninstall.Root == AppIds.ProgramDataDir && Installer.StageDir == Path.Combine(AppIds.ProgramDataDir, "install"), "");
        check("B3: os pedidos da app levam o User-Agent de sempre", AppIds.UserAgent == "CS2IPChanger/0.1", "");

        // O Install deixa de fora o que o Store deixa ao lado de um ficheiro estragado ou a meio
        var dir = Path.Combine(Path.GetTempPath(), "srt-b3-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, AppIds.Files.Settings);
            File.WriteAllText(file, "{ cortado");
            Store.Load(file, () => new Settings());
            var left = Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n != AppIds.Files.Settings).ToList();
            check("B3: o Install não copia o ficheiro estragado que o Store guardou", left.Count == 1 && left.All(Installer.Skip), string.Join(", ", left));
            check("B3: o Install não copia o .tmp de um save, o owned.json nem o running.flag",
                Installer.Skip(AppIds.Files.Servers + AppIds.Files.TmpSuffix) && Installer.Skip(AppIds.Files.Owned) && Installer.Skip(AppIds.Files.RunningFlag) && !Installer.Skip(AppIds.Files.Servers), "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // B2: um só estado da ligação do telemóvel (hotspot, Wi-Fi do telemóvel, internet de casa, modo efetivo) para toda a app
    static void PhoneLinkState(Action<string, bool, string> check)
    {
        var onHome = new PhoneInfo("S1", "device", "Pixel", "MEO", "5G", 80, WifiOn: true, WifiSsid: "Casa", WifiConnected: true);
        check("B2: Wi-Fi do telemóvel numa rede, por cabo: não é de confiança", PhoneLink.WifiOn(onHome, null) && PhoneLink.WifiOn(onHome, false), "");
        check("B2: no hotspot (a ligação é Wi-Fi) o Wi-Fi do telemóvel não conta", !PhoneLink.WifiOn(onHome, true), "");
        check("B2: Wi-Fi ligado sem rede (a procurar) não conta", !PhoneLink.WifiOn(onHome with { WifiConnected = false }, null), "");
        check("B2: Android antigo que não diz se está numa rede conta", PhoneLink.WifiOn(onHome with { WifiConnected = null }, null), "");
        check("B2: telemóvel por autorizar ou sem telemóvel não conta", !PhoneLink.WifiOn(onHome with { State = "unauthorized" }, null) && !PhoneLink.WifiOn(null, null), "");

        var phone = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var hotspot = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "172.20.10.2", Gateway = "172.20.10.1" };
        var home = new Adapter { Index = 5, Up = true, LocalIp = "192.168.1.40", Gateway = "192.168.1.1" };
        check("B2: com a rede de casa ao lado há internet de casa", PhoneLink.HasHome(new[] { phone, home }, phone) && PhoneLink.HasHome(new[] { home }, null), "");
        check("B2: o hotspot e outro telemóvel não são internet de casa", !PhoneLink.HasHome(new[] { hotspot, phone }, hotspot), "");
        check("B2: um adaptador em baixo ou sem gateway não é internet de casa",
            !PhoneLink.HasHome(new[] { phone, new Adapter { Index = 5, Up = false, Gateway = "192.168.1.1" }, new Adapter { Index = 6, Up = true } }, phone), "");

        check("B2: proteção desligada é \"off\" em Game only e Whole PC", PhoneLink.EffectiveMode(false, "servers", true, true) == "off" && PhoneLink.EffectiveMode(false, "all", false, false) == "off", "");
        check("B2: No phone continua \"home\" com a proteção desligada", PhoneLink.EffectiveMode(false, "home", false, false) == "home", "");
        check("B2: ligada, o modo é o de sempre", PhoneLink.EffectiveMode(true, "servers", true, true) == "all" && PhoneLink.EffectiveMode(true, "servers", false, true) == "servers", "");

        check("B2: a ligação do telemóvel num relatório é a protegida ou a que está a ser verificada",
            PhoneLink.Exit(new GuardReport { Candidate = hotspot }) == hotspot && PhoneLink.Exit(new GuardReport { Egress = phone, Candidate = phone }) == phone && PhoneLink.Exit(null) == null, "");
    }

    // B1: a chave das rotas da app num só tipo, no formato que o owned.json sempre teve
    static void RouteKeys(Action<string, bool, string> check)
    {
        var key = RouteKey.Of(new Net.Route("203.0.113.9/32", 17, "192.0.2.1"));
        check("B1: a chave de uma rota escreve-se como no owned.json", key.ToString() == "203.0.113.9/32|17|192.0.2.1", key.ToString());
        check("B1: uma linha de um owned.json antigo volta à mesma chave", RouteKey.TryParse("203.0.113.9/32|17|192.0.2.1", out var back) && back == key, back.ToString());
        check("B1: sem três partes ou com um índice que não é número, não é chave",
            !RouteKey.TryParse("203.0.113.9/32|17", out _) && !RouteKey.TryParse("203.0.113.9/32|-3|192.0.2.1", out _) && !RouteKey.TryParse(null, out _), "");
        var usb = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var onLink = new Adapter { Index = 21, Up = true, LocalIp = "10.0.0.2" };
        check("B1: a rota que a app junta tem o gateway do adaptador, ou 0.0.0.0 sem gateway",
            RouteKey.Of("1.0.0.1/32", usb).ToString() == "1.0.0.1/32|17|192.168.42.129" && RouteKey.Of("1.0.0.1/32", onLink).Hop == "0.0.0.0", "");
        check("B1: o owned.json continua a aceitar as mesmas linhas",
            Guard.Trusted(new Guard.OwnedState { Routes = new() { key.ToString(), "0.0.0.0/1|17|192.0.2.1", "10.0.0.0/8|17|192.0.2.1" } }, out var dropped).Routes.Count == 2 && dropped == 1, "");
    }

    // B1: "esta rota é da app" também no hotspot ou numa rede USB (a ligação do telemóvel), mas nunca uma rota de outro programa
    static void OursOnTheExit(Action<string, bool, string> check)
    {
        var none = new HashSet<string>();
        var servers = new HashSet<string> { "203.0.113.9" };
        var wifi = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "172.20.10.2", Gateway = "172.20.10.1", Ssid = "Ana" };
        var usbPhone = new Adapter { Index = 17, IsPhone = true, Up = true, LocalIp = "192.168.42.10", Gateway = "192.168.42.129" };
        var adapters = new List<Adapter> { wifi, usbPhone };
        Net.Route On(string prefix, int index) => new(prefix, index, "172.20.10.1");

        check("B1: um servidor da lista no hotspot é da app quando o hotspot é a ligação do telemóvel",
            Guard.IsOurs(On("203.0.113.9/32", 12), none, adapters, 12, servers), "");
        check("B1: sem ligação conhecida (--restore), o hotspot fica como hoje", !Guard.IsOurs(On("203.0.113.9/32", 12), none, adapters, -1, servers), "");
        check("B1: as metades do PC e os blocos da Valve no hotspot também",
            Guard.IsOurs(On("0.0.0.0/1", 12), none, adapters, 12, none) && Guard.IsOurs(On(Net.ValvePrefixes[0], 12), none, adapters, 12, none), "");
        check("B1: um /32 que não é da lista no hotspot nunca é da app (o servidor de uma VPN)",
            !Guard.IsOurs(On("198.51.100.7/32", 12), none, adapters, 12, servers, anyHostOnPhone: true), "");
        check("B1: num adaptador de telemóvel, qualquer /32 conta quando a lista pode ter mudado",
            Guard.IsOurs(On("198.51.100.7/32", 17), none, adapters, 12, servers, anyHostOnPhone: true), "");
        check("B1: outras rotas (a rota por defeito, uma rede) nunca são da app",
            !Guard.IsOurs(On("0.0.0.0/0", 12), none, adapters, 12, servers) && !Guard.IsOurs(On("10.0.0.0/8", 17), none, adapters, 12, servers), "");
        check("B1: o que está no owned.json é da app em qualquer adaptador",
            Guard.IsOurs(On("203.0.113.9/32", 5), new HashSet<string> { "203.0.113.9/32|5|172.20.10.1" }, adapters, -1, none), "");

        check("B1: a ligação do telemóvel conta enquanto tem o mesmo IP local", Guard.ExitIndex(wifi, adapters) == 12, "");
        var moved = new Adapter { Index = 12, IsWifi = true, Up = true, LocalIp = "192.168.1.40", Gateway = "192.168.1.1" };
        check("B1: o Wi-Fi que saltou para o router de casa já não conta", Guard.ExitIndex(wifi, new List<Adapter> { moved }) == -1, "");
        var vpn = new Adapter { Index = 30, IsVpn = true, Up = true, LocalIp = "10.8.0.2" };
        check("B1: o adaptador de uma VPN escolhido à mão nunca conta", Guard.ExitIndex(vpn, new List<Adapter> { vpn }) == -1, "");
    }

    // B1: uma rota que o Windows não apaga continua a ser da app (no owned.json também) e vai ao Log uma vez, não a cada sync
    static async Task RouteNotDeleted(Action<string, bool, string> check)
    {
        const string key = "203.0.113.9/32|9999|192.0.2.1";
        var dir = Path.Combine(Path.GetTempPath(), "srt-untangle12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { Routes = new() { key }, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        var table = new List<Net.Route> { new("203.0.113.9/32", 9999, "192.0.2.1") };
        var deletes = 0;
        var refuse = true;
        var guard = new Guard(file, (a, ms) => Task.FromResult(new ProcResult(0, "")), new Guard.RouteTable(
            () => Task.FromResult(table.ToList()),
            rt => { deletes++; if (refuse) return Task.FromResult(new ProcResult(1, "Element not found.")); table.Remove(rt); return Task.FromResult(new ProcResult(0, "Ok.")); }));
        check("B1: o Guard lê a rota do owned.json", guard.Owned().Routes.Contains(key), "");

        var warned = 0;
        void Seen(LogEntry e) { if (e.Text.StartsWith("Couldn't delete the route 203.0.113.9/32")) warned++; }
        Log.Added += Seen;
        try
        {
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            check("B1: a rota que o Windows recusa apagar continua da app", deletes == 2 && guard.Owned().Routes.Contains(key)
                && Store.Load(file, () => new Guard.OwnedState()).Routes.Contains(key), $"{deletes} tentativas");
            check("B1: e a falha vai ao Log uma vez, não a cada tentativa", warned == 1, $"{warned} linhas");
            refuse = false;
            await guard.RemoveIps(new[] { "203.0.113.9" }, new Settings());
            check("B1: apagada, deixa de ser da app", table.Count == 0 && !guard.Owned().Routes.Contains(key)
                && !Store.Load(file, () => new Guard.OwnedState()).Routes.Contains(key), "");
        }
        finally { Log.Added -= Seen; }
    }

    // O relógio do demo parado: o que a app mostra e carimba lê o AppClock, por isso as capturas saem iguais em cada corrida
    static void StoppedClock(Action<string, bool, string> check)
    {
        var at = new DateTime(2026, 11, 6, 16, 44, 0, DateTimeKind.Local);
        LogEntry got = null;
        void Seen(LogEntry e) { if (e.Text == "untangle12 clock") got = e; }
        AppClock.StopAt(at);
        try
        {
            Log.Added += Seen;
            Log.Info("untangle12 clock");
            check("Relógio: parado, diz sempre a mesma hora", AppClock.Now == at && AppClock.Today == at.Date, "");
            check("Relógio: a linha do Log leva a hora do relógio da app", got?.Time == at, got?.Time.ToString("O") ?? "sem linha");
            check("Relógio: \"há 5 min\" conta a partir do relógio da app", Fmt.Ago(at.AddMinutes(-5)) == "5 min ago", Fmt.Ago(at.AddMinutes(-5)));
            check("Relógio: Previous IPs mostra a hora no dia do relógio", new IpHistoryEntry { Time = at.AddHours(-3) }.TimeText == "13:44"
                && new IpHistoryEntry { Time = at.AddDays(-1) }.TimeText == "5 Nov", "");
        }
        finally { Log.Added -= Seen; AppClock.Run(); }
        check("Relógio: volta a andar com o Windows", Math.Abs((AppClock.Now - DateTime.Now).TotalSeconds) < 5, "");
    }

    /// <summary>Um owned.json desta sessão do Windows (a métrica só vive até reiniciar).</summary>
    static string OwnedWithMetric(int ifIndex)
    {
        var dir = Path.Combine(Path.GetTempPath(), "srt-untangle12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { MetricIf = ifIndex, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        return file;
    }

    // A7: devolver o adaptador do telemóvel ao fechar com o PowerShell a falhar não pode esquecer que a métrica 900 ficou lá
    static async Task MetricBack(Action<string, bool, string> check)
    {
        var file = OwnedWithMetric(7);
        var failing = new Guard(file, (args, ms) => Task.FromResult(new ProcResult(1, "NetTCPIP\\Set-NetIPInterface : Access is denied.")));
        check("A7: o Guard lê a métrica aplicada do owned.json", failing.PcOffPhoneApplied, "");
        await failing.ReleasePcOffPhone();
        check("A7: com o PowerShell a falhar a métrica continua marcada", failing.PcOffPhoneApplied && failing.Owned().MetricIf == 7, "");
        check("A7: e o owned.json guarda-a para o próximo arranque ou o --restore",
            Store.Load(file, () => new Guard.OwnedState()).MetricIf == 7, "");

        string ran = null;
        var working = new Guard(file, (args, ms) => { ran = args; return Task.FromResult(new ProcResult(0, "")); });
        await working.ReleasePcOffPhone();
        check("A7: com o PowerShell a funcionar a métrica sai", !working.PcOffPhoneApplied && Store.Load(file, () => new Guard.OwnedState()).MetricIf == -1, "");
        check("A7: e o comando devolve a rota por defeito ao adaptador 7", ran?.Contains("-InterfaceIndex 7 ") == true && ran.Contains("-IgnoreDefaultRoutes Disabled"), ran ?? "");
    }

    // A8: um iPhone só por hotspot Wi-Fi, sem cabo, recebe os passos do iPhone e não os do Android
    static void IphoneOnWifi(Action<string, bool, string> check)
    {
        var none = PhoneUsbState.None;
        check("A8: um hotspot com nome de iPhone diz que é um iPhone", PhoneUsb.IphoneSeen(false, none, null, "Ana's iPhone") == true, "");
        check("A8: um Android pronto no ADB diz que não é", PhoneUsb.IphoneSeen(true, none, null, "Ana's iPhone") == false, "");
        check("A8: um hotspot Wi-Fi com outro nome e sem cabo não diz nada", PhoneUsb.IphoneSeen(false, none, null, "MEO-5G") == null, "");
        check("A8: sem nada visto, o iPhone guardado vale enquanto há hotspot", PhoneUsb.IsIphoneNow(null, true, "MEO-5G"), "");
        check("A8: sem hotspot, o iPhone guardado não vale (o cabo decide)", !PhoneUsb.IsIphoneNow(null, true, null), "");
        check("A8: o que se vê ganha ao guardado", !PhoneUsb.IsIphoneNow(false, true, "MEO-5G") && PhoneUsb.IsIphoneNow(true, false, null), "");

        var steps = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Hotspot = true, Usb = none }, ssid: "MEO-5G");
        check("A8: o Phone setup de um iPhone só por Wi-Fi diz que o cabo é opcional",
            steps.Any(s => s.Detail?.Contains("the cable is optional") == true) && !steps.Any(s => s.Detail?.Contains("USB debugging") == true), "");
        check("A8: o iPhone fica guardado nas definições", typeof(Settings).GetProperty(nameof(Settings.PhoneIsIphone)) != null, "");
    }
}
