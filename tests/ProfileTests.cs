using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SRT.Core;

// Página Profiles: sem rede e sem contas reais. O Steam é um handler falso; os tokens são JWT inventados aqui.
static class ProfileTests
{
    const string Id1 = "76561198000000001", Id2 = "76561198000000002", Id3 = "76561198000000003", Id4 = "76561198000000004";

    static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Um refresh token com a forma dos do Steam (não é assinado por ninguém).</summary>
    public static string FakeToken(string steamId, int days = 200, string marker = "TESTONLY") =>
        B64Url("{\"typ\":\"JWT\",\"alg\":\"EdDSA\"}") + "." +
        B64Url($"{{\"iss\":\"steam\",\"sub\":\"{steamId}\",\"aud\":[\"web\",\"renew\",\"derive\"],\"exp\":{DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds()},\"jti\":\"{marker}\"}}") + "." +
        B64Url("assinatura-falsa-" + marker);

    sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Method, Uri Uri, string Cookie, string Body)> Seen = new();
        public Func<HttpRequestMessage, string, HttpResponseMessage> Answer;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            req.Headers.TryGetValues("Cookie", out var c);
            Seen.Add((req.Method.Method, req.RequestUri, c?.FirstOrDefault(), body));
            return Answer(req, body) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/html") };

    static string FormField(string multipart, string name)
    {
        var m = Regex.Match(multipart, "name=\"?" + Regex.Escape(name) + "\"?\\r\\n(?:[^\\r\\n]+\\r\\n)*\\r\\n([^\\r]*)");
        if (m.Success) return m.Groups[1].Value;
        // Urlencoded body (the sign-in calls): name=value&name=value
        var u = Regex.Match(multipart, "(?:^|&)" + Regex.Escape(name) + "=([^&]*)");
        return u.Success ? Uri.UnescapeDataString(u.Groups[1].Value.Replace('+', ' ')) : null;
    }

    static byte[] Png(int w, int h, string trailer = null)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)(i % 255); px[i + 1] = 90; px[i + 2] = 200; px[i + 3] = 255; }
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        if (trailer != null) ms.Write(Encoding.ASCII.GetBytes(trailer));
        return ms.ToArray();
    }

    /// <summary>Um JPEG com comentário e autor nos metadados (EXIF/XMP), para ver que nada disso passa.</summary>
    static byte[] JpegWithMetadata(string secret)
    {
        var png = BitmapDecoder.Create(new MemoryStream(Png(300, 240)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var meta = new BitmapMetadata("jpg") { Comment = secret, Author = new System.Collections.ObjectModel.ReadOnlyCollection<string>(new[] { secret }) };
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(png, null, meta, null));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static string Html(object editConfig) =>
        "<html><body><div id=\"profile_edit_config\" data-profile-edit=\"" + WebUtility.HtmlEncode(JsonSerializer.Serialize(editConfig)) + "\" data-profile-badges=\"{}\"></div></body></html>";

    public static async Task Run(Action<string, bool, string> Check, Action<string, bool, string> Env)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "srt-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try { await Body(Check, Env, tmp); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static async Task Body(Action<string, bool, string> Check, Action<string, bool, string> Env, string tmp)
    {
        // ---------------- Modelos guardados
        {
            var t = new ProfileTemplate
            {
                Title = "Main look", Name = "nightfall {n}", RealName = "", Summary = "linha 1\nlinha 2 ✓", Country = "PT", CustomUrl = "nf{n}", Avatar = "x.jpg",
                Privacy = new PrivacyPreset { Profile = 3, Inventory = 1, Comments = 2 }, Fields = ProfileFields.Name | ProfileFields.Privacy | ProfileFields.Summary,
            };
            var f = Path.Combine(tmp, "templates.json");
            Store.Save(f, new List<ProfileTemplate> { t });
            var back = Store.Load(f, () => new List<ProfileTemplate>()).Single();
            Check("Perfis: modelo ida e volta", back.Title == t.Title && back.Name == t.Name && back.Summary == t.Summary && back.Country == "PT" && back.CustomUrl == "nf{n}"
                && back.Fields == t.Fields && back.Privacy.Profile == 3 && back.Privacy.Inventory == 1 && back.Privacy.Comments == 2 && back.Privacy.GameDetails == null && back.Id == t.Id, "");
        }

        // ---------------- Nomes: {login} não existe, e o login não sai por nenhum caminho
        {
            Check("Perfis: {login} recusado", NamePattern.Problem("x {login}")?.Contains("login") == true, NamePattern.Problem("x {login}"));
            Check("Perfis: {Login} e {user} recusados", NamePattern.Problem("{Login}") != null && NamePattern.Problem("{user}") != null && NamePattern.Problem("{account}") != null, "");
            Check("Perfis: etiqueta desconhecida recusada", NamePattern.Problem("{steamid}") != null, "");
            Check("Perfis: {name} e {n} aceites", NamePattern.Problem("{name} | NV {n}") == null, "");
            Check("Perfis: {n} conta e {name} é o nome atual", NamePattern.Render("{name} #{n}", "orbit", 3) == "orbit #3", NamePattern.Render("{name} #{n}", "orbit", 3));
            Check("Perfis: URL só aceita {n}", NamePattern.Problem("{name}", allowName: false) != null, "");

            var targets = new List<ProfileTarget> { new(Id1, "nightfall_main", "nightfall"), new(Id2, "orbit_alt01", "orbit_alt01") };
            ProfileJob J(ProfileTemplate t, ProfileFields f, string id, IEnumerable<string> all = null) => ProfilePlan.Make(t, f, targets, null, false, all).Single(j => j.SteamId == id);
            Check("Perfis: login escrito à mão no nome é apanhado", J(new ProfileTemplate { Name = "NIGHTFALL_main" }, ProfileFields.Name, Id1).Problem != null, "");
            Check("Perfis: {name} igual ao login que já é público não bloqueia", J(new ProfileTemplate { Name = "{name}" }, ProfileFields.Name, Id2).Problem == null, "");
            Check("Perfis: o login de outra conta também é apanhado", J(new ProfileTemplate { Name = "orbit_alt01" }, ProfileFields.Name, Id1).Problem?.Contains("another account") == true,
                J(new ProfileTemplate { Name = "orbit_alt01" }, ProfileFields.Name, Id1).Problem);
            Check("Perfis: login de outra conta do PC (fora da lista) apanhado", ProfilePlan.Make(new ProfileTemplate { Name = "drift_faceit" }, ProfileFields.Name, targets.Take(1).ToList(), null, false, new[] { "drift_faceit" })[0].Problem != null, "");
            Check("Perfis: login na bio e no nome real apanhado", J(new ProfileTemplate { Summary = "add me: nightfall_main" }, ProfileFields.Summary, Id1).Problem != null
                && J(new ProfileTemplate { RealName = "orbit_alt01" }, ProfileFields.RealName, Id1).Problem != null, "");
            Check("Perfis: nome com mais de 32 recusado", J(new ProfileTemplate { Name = new string('a', 33) }, ProfileFields.Name, Id1).Problem != null, "");
            Check("Perfis: URL igual em 2 contas recusado", J(new ProfileTemplate { CustomUrl = "teamnv" }, ProfileFields.CustomUrl, Id1).Problem?.Contains("{n}") == true, "");
            var ok = ProfilePlan.Make(new ProfileTemplate { Name = "nv {n}", CustomUrl = "nv{n}" }, ProfileFields.Name | ProfileFields.CustomUrl, targets, null, false);
            Check("Perfis: {n} numera pela ordem", ok[0].PersonaName == "nv 1" && ok[1].PersonaName == "nv 2" && ok[1].CustomUrl == "nv2" && ok.All(j => j.Problem == null), string.Join(",", ok.Select(j => j.PersonaName + "/" + j.Problem)));
            var noAvatar = ProfilePlan.Make(new ProfileTemplate { Name = "{name}" }, ProfileFields.Name | ProfileFields.Avatar, targets, null, false)[0];
            Check("Perfis: sem imagem, o campo imagem sai", !noAvatar.Fields.HasFlag(ProfileFields.Avatar), "");
        }

        // ---------------- Multipart: a fronteira vai sem aspas (o api.steampowered.com fecha a ligação com aspas)
        {
            using var mp = SteamWeb.Form(new[] { KeyValuePair.Create("input_protobuf_encoded", "AAAA"), KeyValuePair.Create("x", "y") });
            var ct = mp.Headers.ContentType;
            var bnd = ct?.Parameters.SingleOrDefault(p => p.Name == "boundary")?.Value;
            var header = mp.Headers.GetValues("Content-Type").Single();
            var body = await mp.ReadAsStringAsync();
            Check("Perfis: multipart com fronteira sem aspas", ct?.MediaType == "multipart/form-data" && bnd != null && !bnd.Contains('"') && Regex.IsMatch(bnd, "^----WebKitFormBoundary[A-Za-z0-9]{16}$")
                && header == "multipart/form-data; boundary=" + bnd, header);
            Check("Perfis: o corpo usa essa mesma fronteira", bnd != null && body.StartsWith("--" + bnd + "\r\n") && body.TrimEnd().EndsWith("--" + bnd + "--")
                && Regex.Matches(body, "--" + Regex.Escape(bnd) + "\r\n").Count == 2 && FormField(body, "input_protobuf_encoded") == "AAAA", body.Length.ToString());
            using var mp2 = SteamWeb.Form(new[] { KeyValuePair.Create("a", "b") });
            Check("Perfis: fronteira nova em cada formulário", mp2.Headers.ContentType.Parameters.Single(p => p.Name == "boundary").Value != bnd, "");
        }

        // ---------------- Um sign-in que falha deixa uma linha no Log, sem o login
        {
            var l = SRT.SteamLoginWindow.FailedLine("Couldn't reach Steam (ResponseEnded).", "nightfall_main");
            Check("Perfis: sign-in falhado fica no Log com o motivo", l == "Profiles: sign-in to an account failed: Couldn't reach Steam (ResponseEnded).", l);
            var l2 = SRT.SteamLoginWindow.FailedLine("Wrong password for NIGHTFALL_main.", "nightfall_main");
            Check("Perfis: o login nunca vai para essa linha", !l2.Contains("nightfall", StringComparison.OrdinalIgnoreCase) && l2.Contains("(hidden)"), l2);
        }

        // ---------------- O formulário do Steam: o que não foi escolhido fica como está
        {
            var cur = new EditConfig { PersonaName = "old", RealName = "Rafa", Summary = "bio", Country = "PT", State = "14", City = "123", CustomUrl = "oldurl" };
            var f = ProfileForm.Build(cur, new ProfileJob { Fields = ProfileFields.Name, PersonaName = "new", RealName = "IGNORAR", Country = "DE" }, "sid").ToDictionary(k => k.Key, k => k.Value);
            Check("Perfis: só o nome muda no formulário", f["personaName"] == "new" && f["real_name"] == "Rafa" && f["summary"] == "bio" && f["country"] == "PT" && f["state"] == "14" && f["city"] == "123" && f["customURL"] == "oldurl" && f["sessionID"] == "sid" && f["type"] == "profileSave", string.Join(";", f));
            var g = ProfileForm.Build(cur, new ProfileJob { Fields = ProfileFields.Country | ProfileFields.Summary, Country = "DE", Summary = "" }, "sid").ToDictionary(k => k.Key, k => k.Value);
            Check("Perfis: outro país limpa estado e cidade, bio vazia é vazia", g["country"] == "DE" && g["state"] == "" && g["city"] == "" && g["summary"] == "" && g["personaName"] == "old", string.Join(";", g));
            var priv = new PrivacyState { Settings = System.Text.Json.Nodes.JsonNode.Parse("{\"PrivacyProfile\":3,\"PrivacyInventory\":3,\"PrivacyInventoryGifts\":1,\"PrivacyOwnedGames\":3,\"PrivacyPlaytime\":3,\"PrivacyFriendsList\":3}")!.AsObject(), CommentPermission = 1 };
            var after = new PrivacyPreset { Inventory = 1, Comments = PrivacyPreset.Friends }.ApplyTo(priv);
            Check("Perfis: privacidade muda só o escolhido", (int)after.Settings["PrivacyInventory"]! == 1 && (int)after.Settings["PrivacyProfile"]! == 3 && (int)after.Settings["PrivacyInventoryGifts"]! == 1 && (int)after.Settings["PrivacyPlaytime"]! == 3 && after.CommentPermission == 0
                && (int)priv.Settings["PrivacyInventory"]! == 3, after.Settings.ToJsonString());
        }

        // ---------------- Imagens
        {
            Check("Perfis: imagem que não é imagem recusada", AvatarImage.Prepare(Encoding.ASCII.GetBytes("<html>not a picture</html>"), out var p1) == null && p1 != null, p1);
            Check("Perfis: imagem pequena recusada", AvatarImage.Prepare(Png(100, 100), out var p2) == null && p2.Contains("184"), p2);
            Check("Perfis: PNG estragado recusado", AvatarImage.Prepare(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat((byte)0x41, 500)).ToArray(), out var p3) == null, p3);
            var big = new byte[AvatarImage.MaxInput + 1]; big[0] = 0x89;
            Check("Perfis: ficheiro acima de 20 MB recusado", AvatarImage.Prepare(big, out _) == null, "");
            var secret = "GPS-38.7-SEGREDO-EXIF";
            var jpg = AvatarImage.Prepare(JpegWithMetadata(secret), out var p4);
            var withTail = AvatarImage.Prepare(Png(800, 500, "DADOS-ESCONDIDOS-NO-FIM"), out _);
            var dec = jpg == null ? null : BitmapDecoder.Create(new MemoryStream(jpg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var decTail = withTail == null ? null : BitmapDecoder.Create(new MemoryStream(withTail), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Check("Perfis: imagem refeita em JPEG quadrado", jpg != null && jpg[0] == 0xFF && jpg[1] == 0xD8 && dec.PixelWidth == 240 && dec.PixelHeight == 240 && jpg.Length < 1024 * 1024, p4 ?? $"{dec?.PixelWidth}x{dec?.PixelHeight}");
            Check("Perfis: metadados não passam", jpg != null && !Encoding.Latin1.GetString(jpg).Contains(secret) && !Encoding.Unicode.GetString(jpg).Contains(secret), "");
            Check("Perfis: dados depois da imagem não passam, e fica em 500 px", withTail != null && !Encoding.Latin1.GetString(withTail).Contains("DADOS-ESCONDIDOS") && decTail.PixelWidth == 500, $"{decTail?.PixelWidth}");
        }

        // ---------------- Sessões guardadas: DPAPI e acesso só deste utilizador
        {
            var dir = Path.Combine(tmp, "sessions");
            var store = new SessionStore(dir);
            var tok = FakeToken(Id1);
            store.Save(Id1, tok);
            var file = Path.Combine(dir, Id1 + ".bin");
            var raw = File.ReadAllBytes(file);
            Check("Perfis: token ida e volta (DPAPI)", store.Token(Id1) == tok && store.Info(Id1)?.Valid == true, "");
            Check("Perfis: o ficheiro não tem o token em claro", !Encoding.Latin1.GetString(raw).Contains("eyJ") && !Encoding.Latin1.GetString(raw).Contains(Id1), "");
            var me = WindowsIdentity.GetCurrent().User;
            var acl = new FileInfo(file).GetAccessControl();
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Check("Perfis: ficheiro só para este utilizador, sem herança", acl.AreAccessRulesProtected && rules.Count == 1 && rules[0].IdentityReference.Equals(me) && rules[0].AccessControlType == AccessControlType.Allow,
                string.Join(", ", rules.Select(r => r.IdentityReference + ":" + r.FileSystemRights + (r.IsInherited ? "(herdada)" : ""))));
            var dacl = new DirectoryInfo(dir).GetAccessControl();
            var drules = dacl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Check("Perfis: pasta só para este utilizador", dacl.AreAccessRulesProtected && drules.All(r => r.IdentityReference.Equals(me)), string.Join(", ", drules.Select(r => r.IdentityReference.ToString())));
            File.Copy(file, Path.Combine(dir, Id2 + ".bin"));
            Check("Perfis: ficheiro copiado para outra conta não abre", store.Token(Id2) == null, "");
            var wrong = false;
            try { store.Save(Id3, FakeToken(Id1)); } catch (InvalidOperationException) { wrong = true; }
            Check("Perfis: token de outra conta não é guardado", wrong && !File.Exists(Path.Combine(dir, Id3 + ".bin")), "");
            store.Save(Id4, FakeToken(Id4, days: -1));
            Check("Perfis: token expirado não é usado", store.Token(Id4) == null && store.Info(Id4)?.Valid == false, "");
            Check("Perfis: esquecer apaga", store.Forget(Id1) && store.Token(Id1) == null && !File.Exists(file), "");
            Check("Perfis: esquecer todas", store.ForgetAll() >= 2 && !store.Accounts().Any(), "");
            var bad = false;
            try { store.Save("../../x", tok); } catch (ArgumentException) { bad = true; }
            Check("Perfis: SteamID inválido não vira caminho", bad, "");
        }

        // ---------------- O token nunca chega ao ecrã de gravação, ao Export diagnostics nem ao Log
        {
            var tok = FakeToken(Id1, marker: "NAOPODESAIR");
            var line = $"finalize nonce={tok} cookie steamLoginSecure={Id1}%7C%7C{tok}; sessionid=abc123";
            Check("Perfis: gravação tapa o token", !Rec.Scrub(line).Contains("eyJ") && !Rec.Scrub(line).Contains("NAOPODESAIR"), Rec.Scrub(line));
            Check("Perfis: Secrets.Clean tapa token e cookies", !Secrets.Clean(line).Contains("eyJ") && !Secrets.Clean(line).Contains("abc123") && !Secrets.Clean(line).Contains("%7C%7C"), Secrets.Clean(line));
            var outDir = Path.Combine(tmp, "report");
            Directory.CreateDirectory(outDir);
            var zip = Report.Export("now " + line, "{\"x\":\"" + tok + "\"}", null, null, null, outDir);
            var all = new StringBuilder();
            using (var z = ZipFile.OpenRead(zip)) foreach (var e in z.Entries) using (var r = new StreamReader(e.Open())) all.Append(r.ReadToEnd());
            Check("Perfis: Export diagnostics sem o token", all.Length > 0 && !all.ToString().Contains("eyJ") && !all.ToString().Contains(B64Url("assinatura-falsa-NAOPODESAIR")), "");
        }

        // ---------------- Só hosts da Valve, só HTTPS
        {
            Check("Perfis: hosts da Valve aceites", SteamWeb.Allowed(new Uri("https://steamcommunity.com/my")) && SteamWeb.Allowed(new Uri("https://api.steampowered.com/x")) && SteamWeb.Allowed(new Uri("https://avatars.fastly.steamstatic.com/a.jpg")), "");
            Check("Perfis: http, outros hosts, portas e user@ recusados",
                !SteamWeb.Allowed(new Uri("http://steamcommunity.com/")) && !SteamWeb.Allowed(new Uri("https://steamcommunity.com.evil.example/")) && !SteamWeb.Allowed(new Uri("https://evil.example/steamcommunity.com"))
                && !SteamWeb.Allowed(new Uri("https://steamcommunity.com:8443/")) && !SteamWeb.Allowed(new Uri("https://a@steamcommunity.com/")) && !SteamWeb.Allowed(new Uri("https://store.steampowered.com/")), "");
            var fake = new Fake { Answer = (_, _) => Text("ok") };
            var refused = false;
            try { await new SteamWeb(fake).Send(new HttpRequestMessage(HttpMethod.Get, "https://evil.example/"), default); } catch (SteamWebException) { refused = true; }
            Check("Perfis: pedido para fora nem sai", refused && fake.Seen.Count == 0, "");
        }

        // ---------------- Protobuf, JWT e QR
        {
            var bytes = new ProtoWriter().Str(2, "conta").UInt(1, 300).Fixed64(3, 76561198000000001).Bool(5, true).Msg(9, new ProtoWriter().Str(1, "dev").Int(2, 2)).ToArray();
            var m = ProtoMsg.Parse(bytes);
            Check("Perfis: protobuf ida e volta", m.Str(2) == "conta" && m.Num(1) == 300 && m.Num(3) == 76561198000000001 && m.Num(5) == 1 && m.All(9).Single().Str(1) == "dev" && m.All(9).Single().Num(2) == 2, "");
            var bad = false;
            try { ProtoMsg.Parse(new byte[] { 0x0A, 0x7F, 0x01 }); } catch (InvalidDataException) { bad = true; }
            Check("Perfis: protobuf partido dá erro, não estoura", bad, "");
            var jt = SteamToken.Read(FakeToken(Id2));
            Check("Perfis: JWT lido (dono, validade, web)", jt?.SteamId == Id2 && jt.IsWebRefresh && jt.Expires > DateTime.Now.AddDays(199), "");
            Check("Perfis: lixo não é token", SteamToken.Read("abc.def") == null && SteamToken.Read(null) == null, "");
            var q = QrCode.Encode("https://s.team/q/1/2372462679780599330");
            Check("Perfis: QR versão 3 com os 3 quadrados", q.Version == 3 && q.Size == 29 && q[0, 0] && q[6, 6] && !q[7, 7] && q[q.Size - 1, 0] && q[0, q.Size - 1] && q[8, q.Size - 8], $"v{q.Version}");
        }

        // ---------------- Sign-in: palavra-passe cifrada com a chave do Steam, código por email
        {
            using var rsa = RSA.Create(2048);
            var pub = rsa.ExportParameters(false);
            string decrypted = null;
            var fake = new Fake();
            fake.Answer = (req, body) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                var b64 = req.Method == HttpMethod.Get ? Uri.UnescapeDataString(Regex.Match(req.RequestUri.Query, "input_protobuf_encoded=([^&]+)").Groups[1].Value) : FormField(body, "input_protobuf_encoded");
                var msg = b64 == null ? null : ProtoMsg.Parse(Convert.FromBase64String(b64));
                HttpResponseMessage Proto(ProtoWriter w, int eresult = 1)
                {
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(w.ToArray()) };
                    r.Headers.Add("x-eresult", eresult.ToString());
                    return r;
                }
                if (path.EndsWith("/GetPasswordRSAPublicKey/v1/") && msg?.Str(1) == "nightfall_main")
                    return Proto(new ProtoWriter().Str(1, Convert.ToHexString(pub.Modulus)).Str(2, Convert.ToHexString(pub.Exponent)).UInt(3, 12345));
                if (path.EndsWith("/BeginAuthSessionViaCredentials/v1/"))
                {
                    decrypted = Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(msg.Str(3)), RSAEncryptionPadding.Pkcs1));
                    if (decrypted != "hunter2 ç") return Proto(new ProtoWriter(), 5);
                    var ok = msg.Num(4) == 12345 && msg.Num(6) == 2 && msg.Str(8) == "Community" && msg.All(9).Single().Str(1) == SteamAuth.DeviceName;
                    return ok ? Proto(new ProtoWriter().UInt(1, 999).Bytes(2, new byte[] { 1, 2, 3 }).Msg(4, new ProtoWriter().Int(1, 2)).Msg(4, new ProtoWriter().Int(1, 6)).UInt(5, ulong.Parse(Id1))) : Proto(new ProtoWriter(), 8);
                }
                if (path == "/jwt/checkdevice") return Json("{\"result\":29}");
                if (path.EndsWith("/UpdateAuthSessionWithSteamGuardCode/v1/"))
                    return Proto(new ProtoWriter(), msg.Str(3) == "AB12C" && msg.Num(2) == ulong.Parse(Id1) && msg.Num(4) == 2 ? 1 : 65);
                if (path.EndsWith("/PollAuthSessionStatus/v1/") && msg.Num(1) == 999)
                    return Proto(new ProtoWriter().Str(3, FakeToken(Id1)).Str(6, "nightfall_main"));
                return null;
            };
            var auth = new SteamAuth(new SteamWeb(fake));
            var pw = Encoding.UTF8.GetBytes("hunter2 ç");
            var (enc, ts) = await auth.EncryptPassword("nightfall_main", pw, default);
            var start = await auth.BeginWithCredentials("nightfall_main", enc, ts, default);
            Check("Perfis: palavra-passe vai cifrada com RSA do Steam", decrypted == "hunter2 ç" && fake.Seen.All(s => !s.Body.Contains("hunter2") && !s.Uri.ToString().Contains("hunter2")), "");
            Check("Perfis: o login vai urlencoded (multipart faz o Steam responder em JSON)", fake.Seen.Where(s => s.Uri.AbsolutePath.Contains("/IAuthenticationService/") && s.Method == "POST").All(s => s.Body.StartsWith("input_protobuf_encoded=")) && fake.Seen.Any(s => s.Uri.AbsolutePath.EndsWith("/BeginAuthSessionViaCredentials/v1/")), "");
            Check("Perfis: guardas lidas (email + máquina)", start.Guards.SequenceEqual(new[] { GuardType.EmailCode, GuardType.MachineToken }) && start.SteamId == Id1 && start.ClientId == 999, string.Join(",", start.Guards));
            await auth.SendEmailCode(start, default);
            var wrong = "";
            try { await auth.SubmitCode(start, "zzzzz", GuardType.EmailCode, default); } catch (SteamWebException ex) { wrong = ex.Message; }
            Check("Perfis: código errado diz-se em palavras simples", wrong == "Wrong code. Check it and try again.", wrong);
            await auth.SubmitCode(start, " AB12C ", GuardType.EmailCode, default);
            var poll = await auth.Poll(start, default);
            Check("Perfis: poll dá o refresh token", poll.RefreshToken == FakeToken(Id1) || SteamToken.Read(poll.RefreshToken)?.SteamId == Id1, "");
            var badPw = "";
            try { var (e2, t2) = await auth.EncryptPassword("nightfall_main", Encoding.UTF8.GetBytes("errada"), default); await auth.BeginWithCredentials("nightfall_main", e2, t2, default); }
            catch (SteamWebException ex) { badPw = ex.Message; }
            Check("Perfis: palavra-passe errada", badPw == "Wrong password.", badPw);
            Check("Perfis: só HTTPS para a Valve no sign-in", fake.Seen.All(s => SteamWeb.Allowed(s.Uri)), "");
        }

        // ---------------- Aplicar a 4 contas: cópia antes, campos certos, 429 com nova tentativa, parar no 2.º 429
        {
            var tokens = new Dictionary<string, string> { [Id1] = FakeToken(Id1, marker: "TOK1"), [Id2] = FakeToken(Id2, marker: "TOK2"), [Id3] = FakeToken(Id3, marker: "TOK3"), [Id4] = FakeToken(Id4, marker: "TOK4") };
            var editPosts = new Dictionary<string, int>();
            var fake = new Fake();
            string IdOf(HttpRequestMessage r) => Regex.Match(r.Headers.TryGetValues("Cookie", out var c) ? c.First() : "", @"steamLoginSecure=(\d{17})").Groups[1].Value;
            fake.Answer = (req, body) =>
            {
                var u = req.RequestUri!;
                if (u.Host == "login.steampowered.com" && u.AbsolutePath == "/jwt/finalizelogin")
                {
                    var nonce = FormField(body, "nonce");
                    var id = tokens.FirstOrDefault(kv => kv.Value == nonce).Key;
                    return id == null ? Json("{\"error\":15}") : Json($"{{\"steamID\":\"{id}\",\"transfer_info\":[{{\"url\":\"https://evil.example/login/settoken\",\"params\":{{\"nonce\":\"x\"}}}},{{\"url\":\"https://steamcommunity.com/login/settoken\",\"params\":{{\"nonce\":\"n-{id}\",\"auth\":\"a\"}}}}]}}");
                }
                if (u.Host == "steamcommunity.com" && u.AbsolutePath == "/login/settoken")
                {
                    var id = FormField(body, "steamID");
                    var r = Json("{\"result\":1}");
                    r.Headers.Add("Set-Cookie", $"steamLoginSecure={id}%7C%7C{tokens[id].Replace("TOK", "ACC")}; Path=/; Secure; HttpOnly");
                    return r;
                }
                var me = IdOf(req);
                if (u.AbsolutePath == "/my") { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new Uri($"https://steamcommunity.com/profiles/{me}/"); return r; }
                if (u.AbsolutePath == $"/profiles/{me}/edit/info")
                    return Text(Html(new { strPersonaName = "old" + me[^1], strRealName = "Real " + me[^1], strSummary = "bio", strCustomURL = "", LocationData = new { locCountryCode = "PT", locStateCode = "14", locCityCode = "1" } }));
                if (u.AbsolutePath == $"/profiles/{me}/edit/settings")
                    return Text(Html(new { Privacy = new { PrivacySettings = new { PrivacyProfile = 3, PrivacyInventory = 3, PrivacyInventoryGifts = 1, PrivacyOwnedGames = 3, PrivacyPlaytime = 3, PrivacyFriendsList = 3 }, eCommentPermission = 1 } }));
                if (u.AbsolutePath == $"/profiles/{me}/" && u.Query == "?xml=1")
                    return Text($"<?xml version=\"1.0\"?><profile><steamID64>{me}</steamID64><steamID><![CDATA[old{me[^1]}]]></steamID><avatarFull><![CDATA[https://avatars.steamstatic.com/abc_full.jpg]]></avatarFull><summary><![CDATA[bio]]></summary></profile>");
                if (u.Host == "avatars.steamstatic.com")
                {
                    // O "?" por defeito do Steam é outra imagem
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(u.AbsolutePath.Contains(CommunitySession.DefaultAvatarHash) ? Png(184, 184) : Png(200, 200)) };
                    r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    return r;
                }
                if (u.AbsolutePath == $"/profiles/{me}/edit/")
                {
                    editPosts[me] = editPosts.GetValueOrDefault(me) + 1;
                    if ((me == Id2 && editPosts[me] == 1) || me == Id3) return new HttpResponseMessage((HttpStatusCode)429);
                    return Json("{\"success\":1,\"errmsg\":\"\"}");
                }
                if (u.AbsolutePath == "/actions/FileUploader/") return Json("{\"success\":true,\"images\":{\"full\":\"https://avatars.steamstatic.com/new_full.jpg\"}}");
                if (u.AbsolutePath == $"/profiles/{me}/ajaxsetprivacy/") return Json("{\"success\":1}");
                return null;
            };
            var snaps = new SnapshotStore(Path.Combine(tmp, "snapshots"));
            var waits = new List<TimeSpan>();
            var applier = new ProfileApplier(new SteamWeb(fake), id => tokens.GetValueOrDefault(id), snaps, (t, _) => { waits.Add(t); return Task.CompletedTask; });
            var avatar = AvatarImage.Prepare(Png(400, 400), out _);
            var targets = new List<ProfileTarget> { new(Id1, "loginum", "old1"), new(Id2, "logindois", "old2"), new(Id3, "logintres", "old3"), new(Id4, "loginquatro", "old4"), new("76561198000000005", "semtoken", "old5") };
            var jobs = ProfilePlan.Make(new ProfileTemplate { Name = "nv {n}", Privacy = new PrivacyPreset { Inventory = 1 } }, ProfileFields.Name | ProfileFields.Avatar | ProfileFields.Privacy, targets, avatar, false);
            jobs.Insert(0, ProfilePlan.Make(new ProfileTemplate { Name = "x" }, ProfileFields.Name, new List<ProfileTarget> { new("76561198000000005", "semtoken", "old5") }, null, false)[0]);
            var logged = new List<string>();
            void OnLog(LogEntry e) => logged.Add(e.Text);
            Log.Added += OnLog;
            var results = await applier.Apply(jobs, null, r => Log.Info("Profiles: " + r.Shown + ": " + r.Message), default);
            Log.Added -= OnLog;
            var byId = results.GroupBy(r => r.SteamId).ToDictionary(g => g.Key, g => g.First());
            Check("Perfis: sem sessão fica de fora com a razão", results[0].Skipped && results[0].Message.StartsWith("Not signed in"), results[0].Message);
            Check("Perfis: conta 1 mudada (nome, imagem, privacidade)", byId[Id1].Ok && byId[Id1].Message.Contains("name") && byId[Id1].Message.Contains("picture") && byId[Id1].Message.Contains("privacy"), byId[Id1].Message);
            Check("Perfis: conta 2 passou o 429 à 2.ª tentativa", byId[Id2].Ok && editPosts[Id2] == 2 && waits.Contains(TimeSpan.FromSeconds(30)), byId[Id2].Message);
            Check("Perfis: conta 3 parou no 2.º 429, com razão clara", !byId[Id3].Ok && byId[Id3].Message.Contains("limiting"), byId[Id3].Message);
            Check("Perfis: conta 4 não foi tentada depois do limite", byId[Id4].Skipped && byId[Id4].Message.StartsWith("Not tried") && !fake.Seen.Any(s => s.Cookie?.Contains(Id4) == true), byId[Id4].Message);
            var form = fake.Seen.First(s => s.Uri.AbsolutePath == $"/profiles/{Id1}/edit/" && s.Method == "POST").Body;
            var fv = System.Web.HttpUtility.ParseQueryString(form);
            Check("Perfis: formulário enviado mantém o resto", fv["personaName"] == "nv 1" && fv["real_name"] == "Real 1" && fv["summary"] == "bio" && fv["country"] == "PT" && fv["state"] == "14" && fv["city"] == "1", form);
            var privBody = fake.Seen.First(s => s.Uri.AbsolutePath == $"/profiles/{Id1}/ajaxsetprivacy/").Body;
            Check("Perfis: privacidade enviada só com o inventário mudado", FormField(privBody, "Privacy") is string pj && pj.Contains("\"PrivacyInventory\":1") && pj.Contains("\"PrivacyProfile\":3") && pj.Contains("\"PrivacyInventoryGifts\":1"), FormField(privBody, "Privacy"));
            var upload = fake.Seen.First(s => s.Uri.AbsolutePath == "/actions/FileUploader/" && s.Cookie.Contains(Id1));
            Check("Perfis: upload com type=player_avatar_image e sessionid", FormField(upload.Body, "type") == "player_avatar_image" && FormField(upload.Body, "sId") == Id1 && FormField(upload.Body, "sessionid")?.Length == 24, "");
            Check("Perfis: nunca para hosts de fora (transfer falso ignorado)", fake.Seen.All(s => SteamWeb.Allowed(s.Uri)) && !fake.Seen.Any(s => s.Uri.Host.Contains("evil")), "");
            Check("Perfis: cookies só para steamcommunity.com", fake.Seen.Where(s => s.Cookie != null).All(s => s.Uri.Host == "steamcommunity.com") && fake.Seen.Where(s => s.Uri.Host.StartsWith("avatars")).All(s => s.Cookie == null), "");
            Check("Perfis: Log sem tokens nem cookies", logged.Count > 0 && logged.All(l => !l.Contains("eyJ") && !l.Contains("%7C%7C") && !l.Contains("TOK")), string.Join(" | ", logged.Take(3)));
            var orig = snaps.Original(Id1);
            Check("Perfis: cópia antes da mudança (nome, privacidade, imagem)", orig != null && orig.Info.PersonaName == "old1" && orig.Info.RealName == "Real 1" && orig.Privacy?.Inventory == 3 && snaps.AvatarOf(orig)?.Length > 100, orig?.Info?.PersonaName);
            Check("Perfis: conta parada no 429 também tem cópia", snaps.Original(Id3) != null, "");
            var restore = snaps.RestoreJob(orig, "nv 1");
            Check("Perfis: repor usa a cópia toda", restore.Restore && restore.PersonaName == "old1" && restore.Country == "PT" && restore.Fields.HasFlag(ProfileFields.Avatar) && restore.Fields.HasFlag(ProfileFields.Privacy) && restore.Privacy.Inventory == 3, restore.Fields.ToString());
            int Uploads() => fake.Seen.Count(s => s.Uri.AbsolutePath == "/actions/FileUploader/" && s.Cookie?.Contains(Id1) == true);
            int PrivacySets() => fake.Seen.Count(s => s.Uri.AbsolutePath == $"/profiles/{Id1}/ajaxsetprivacy/");
            var (upBefore, privBefore) = (Uploads(), PrivacySets());
            var res2 = await applier.Apply(new[] { restore }, null, null, default);
            Check("Perfis: repor não volta a enviar a imagem que já lá está (o Steam recomprime)", Uploads() == upBefore && !res2[0].Message.Contains("picture"), res2[0].Message);
            Check("Perfis: repor não volta a mexer na privacidade igual", PrivacySets() == privBefore && !res2[0].Message.Contains("privacy"), res2[0].Message);
            var otherPic = new ProfileJob { SteamId = Id1, Shown = "nv 1", Restore = true, Fields = ProfileFields.Avatar, Avatar = AvatarImage.Prepare(Png(300, 300), out _) };
            var res3 = await applier.Apply(new[] { otherPic }, null, null, default);
            Check("Perfis: imagem diferente da que lá está é enviada", Uploads() == upBefore + 1 && res3[0].Message.Contains("picture"), res3[0].Message);
            // A cópia tinha o "?" por defeito do Steam: nunca se envia uma imagem dele (ficaria uma imagem própria parecida)
            var defJob = new ProfileJob { SteamId = Id1, Shown = "nv 1", Restore = true, Fields = ProfileFields.Avatar, Avatar = AvatarImage.Prepare(Png(184, 184), out _) };
            var upDef = Uploads();
            var resDef = await applier.Apply(new[] { defJob }, null, null, default);
            Check("Perfis: repor o avatar por defeito não envia imagem e diz como o pôr", Uploads() == upDef && resDef[0].Ok && resDef[0].Message.Contains("default picture") && resDef[0].Message.Contains("Edit Profile"), resDef[0].Message);
            var flagged = new ProfileJob { SteamId = Id1, Shown = "nv 1", Restore = true, Fields = ProfileFields.Avatar, Avatar = AvatarImage.Prepare(Png(300, 300), out _), AvatarIsDefault = true };
            var resFlag = await applier.Apply(new[] { flagged }, null, null, default);
            Check("Perfis: cópia marcada com o avatar por defeito também não envia", Uploads() == upDef && resFlag[0].Message.Contains("default picture"), resFlag[0].Message);
            Check("Perfis: o URL do avatar por defeito é reconhecido", CommunitySession.IsDefaultAvatar("https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg") && !CommunitySession.IsDefaultAvatar("https://avatars.steamstatic.com/4e00ab_full.jpg") && !CommunitySession.IsDefaultAvatar(null), "");
            Check("Perfis: mesma imagem por SHA-256, desconhecida conta como diferente", ProfileApplier.SamePicture(new byte[] { 1, 2 }, new byte[] { 1, 2 }) && !ProfileApplier.SamePicture(null, new byte[] { 1 }) && !ProfileApplier.SamePicture(new byte[] { 1 }, new byte[] { 2 }), "");
            var lastForm = System.Web.HttpUtility.ParseQueryString(fake.Seen.Last(s => s.Uri.AbsolutePath == $"/profiles/{Id1}/edit/").Body);
            Check("Perfis: repor original envia os valores antigos e não faz nova cópia", res2[0].Ok && lastForm["personaName"] == "old1" && snaps.All(Id1).Count == 1, res2[0].Message);

            // Sessão expirada no Steam: razão clara, não estoura
            tokens[Id1] = FakeToken(Id1, marker: "OUTRO");
            var expired = await new ProfileApplier(new SteamWeb(fake), id => id == Id1 ? FakeToken(Id1, marker: "REVOGADO") : null, snaps, (_, _) => Task.CompletedTask)
                .Apply(new[] { jobs.First(j => j.SteamId == Id1) }, null, null, default);
            Check("Perfis: sessão revogada pede novo sign-in", !expired[0].Ok && expired[0].Message.Contains("Sign in again"), expired[0].Message);
            Check("Perfis: sessão revogada chega marcada como expirada", expired[0].Expired, expired[0].Message);
        }

        // ---------------- Conta com URL próprio (/my vai para /id/oldname): depois de mudar o URL, o resto vai para /profiles/<id>
        {
            HttpResponseMessage Ok() => Json("{\"success\":1}");
            Fake Vanity(bool refuseUpload)
            {
                var saved = false;
                var fake = new Fake();
                fake.Answer = (req, body) =>
                {
                    var u = req.RequestUri!;
                    var p = u.AbsolutePath;
                    if (p == "/jwt/finalizelogin") return Json($"{{\"steamID\":\"{Id1}\",\"transfer_info\":[{{\"url\":\"https://steamcommunity.com/login/settoken\",\"params\":{{\"nonce\":\"n\"}}}}]}}");
                    if (p == "/login/settoken") { var r = Json("{\"result\":1}"); r.Headers.Add("Set-Cookie", $"steamLoginSecure={Id1}%7C%7CACCESSFALSO; Path=/"); return r; }
                    if (p == "/my") { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new Uri("https://steamcommunity.com/id/oldname/"); return r; }
                    // Depois de o URL mudar, /id/oldname já não é esta conta
                    if (p.StartsWith("/id/oldname/") && saved) return null;
                    if (p is "/id/oldname/edit/info" or $"/profiles/{Id1}/edit/info")
                        return Text(Html(new { strPersonaName = "old", strRealName = "", strSummary = "", strCustomURL = "oldname", LocationData = new { locCountryCode = "PT", locStateCode = "14", locCityCode = "1" } }));
                    if (p is "/id/oldname/edit/settings" or $"/profiles/{Id1}/edit/settings")
                        return Text(Html(new { Privacy = new { PrivacySettings = new { PrivacyProfile = 3, PrivacyInventory = 3 }, eCommentPermission = 1 } }));
                    if (p == $"/profiles/{Id1}/" && u.Query == "?xml=1") return Text("<profile><steamID>old</steamID></profile>");
                    if (p == "/id/oldname/edit/" && req.Method == HttpMethod.Post) { saved = true; return Ok(); }
                    if (p == $"/profiles/{Id1}/edit/" && req.Method == HttpMethod.Post) return Ok();
                    if (p == "/actions/FileUploader/") return refuseUpload ? Json("{\"success\":2}") : Ok();
                    if (p is $"/profiles/{Id1}/ajaxsetprivacy/" or $"/profiles/{Id1}/ajaxclearaliashistory/") return Ok();
                    return null;
                };
                return fake;
            }
            var target = new List<ProfileTarget> { new(Id1, "loginum", "old") };
            var snaps = new SnapshotStore(Path.Combine(tmp, "snapshots-vanity"));
            ProfileApplier Applier(Fake f, Func<TimeSpan, CancellationToken, Task> delay = null) => new(new SteamWeb(f), _ => FakeToken(Id1), snaps, delay ?? ((_, _) => Task.CompletedTask));

            var f1 = Vanity(false);
            var job = ProfilePlan.Make(new ProfileTemplate { CustomUrl = "newname", Privacy = new PrivacyPreset { Inventory = 1 } }, ProfileFields.CustomUrl | ProfileFields.Privacy, target, null, true)[0];
            var res = (await Applier(f1).Apply(new[] { job }, null, null, default))[0];
            var posts = f1.Seen.Where(s => s.Method == "POST").Select(s => s.Uri.AbsolutePath).ToList();
            Check("Perfis: depois de mudar o URL, privacidade e nomes antigos vão para /profiles/<id>", res.Ok && posts.Contains($"/profiles/{Id1}/ajaxsetprivacy/") && posts.Contains($"/profiles/{Id1}/ajaxclearaliashistory/") && !posts.Any(x => x.StartsWith("/id/oldname/ajax")),
                res.Message + " | " + string.Join(",", posts));

            // Falha a meio: o nome já mudou, a imagem não. A mensagem diz o que mudou.
            var f2 = Vanity(true);
            var avatar = AvatarImage.Prepare(Png(300, 300), out _);
            var half = ProfilePlan.Make(new ProfileTemplate { Name = "nv" }, ProfileFields.Name | ProfileFields.Avatar, target, avatar, false)[0];
            var r2 = (await Applier(f2).Apply(new[] { half }, null, null, default))[0];
            Check("Perfis: falha a meio diz o que já mudou", !r2.Ok && r2.Partial && r2.Message.StartsWith("Changed: name. Then:") && r2.Message.Contains("Undo"), r2.Message);

            // Parar durante a pausa entre contas: "Stopped." nessa e "Not tried" nas seguintes, sem perder os resultados
            var f3 = Vanity(false);
            using var cts = new CancellationTokenSource();
            ProfileApplier stopper = null;
            stopper = Applier(f3, (t, c) => { if (t == stopper.AccountGap) { cts.Cancel(); c.ThrowIfCancellationRequested(); } return Task.CompletedTask; });
            var nameJob = ProfilePlan.Make(new ProfileTemplate { Name = "nv" }, ProfileFields.Name, target, null, false)[0];
            List<ProfileResult> stopped = null;
            var threw = false;
            try { stopped = await stopper.Apply(new[] { nameJob, nameJob, nameJob }, null, null, cts.Token); } catch (OperationCanceledException) { threw = true; }
            Check("Perfis: parar na pausa entre contas dá Stopped e Not tried", !threw && stopped?.Count == 3 && stopped[0].Ok && stopped[1].Message == "Stopped." && stopped[2].Skipped && stopped[2].Message.StartsWith("Not tried"),
                threw ? "rebentou" : string.Join(" | ", stopped.Select(x => x.Message)));

            // "Main look" de origem: Name ({name}) e Picture marcados, sem imagem. Não apaga a imagem nem manda o nome igual.
            var f4 = Vanity(false);
            var look = ProfilePlan.Make(new ProfileTemplate { Title = "Main look", Name = "{name}" }, ProfileFields.Name | ProfileFields.Avatar, target, null, false)[0];
            Check("Perfis: modelo sem imagem tira a imagem do plano", look.Problem == null && !look.Fields.HasFlag(ProfileFields.Avatar) && look.Avatar == null && look.PersonaName == "old", look.Fields.ToString());
            Check("Perfis: {name} igual ao nome atual não conta como mudança", ProfileForm.Changing(new EditConfig { PersonaName = "old" }, look) == ProfileFields.None
                && ProfileForm.Changing(new EditConfig { PersonaName = "outro" }, look) == ProfileFields.Name, "");
            var r4 = (await Applier(f4).Apply(new[] { look }, null, null, default))[0];
            var posts4 = f4.Seen.Where(s => s.Method == "POST").Select(s => s.Uri.AbsolutePath).ToList();
            Check("Perfis: Main look sem imagem não envia nada", r4.Ok && r4.Message == "Nothing to change." && !posts4.Any(p => p.EndsWith("/edit/") || p == "/actions/FileUploader/"), r4.Message + " | " + string.Join(",", posts4));
            var rest = snaps.RestoreJob(snaps.Original(Id1), "old");
            Check("Perfis: repor envia sempre o formulário", ProfileForm.Changing(new EditConfig { PersonaName = "old" }, rest).HasFlag(ProfileFields.Name), rest.Fields.ToString());
        }

        // ---------------- Plano: URL vazio, {n} a partir de outro número, nome atual desconhecido, chavetas literais
        {
            var one = new List<ProfileTarget> { new(Id1, "nightfall_main", "nightfall") };
            var noUrl = ProfilePlan.Make(new ProfileTemplate { CustomUrl = "" }, ProfileFields.CustomUrl, one, null, false)[0];
            var noUrlForm = ProfileForm.Build(new EditConfig { PersonaName = "a", CustomUrl = "oldurl" }, noUrl, "sid").ToDictionary(k => k.Key, k => k.Value);
            Check("Perfis: URL vazio com o campo ligado tira o URL (e diz-se)", noUrl.Problem == null && noUrl.CustomUrl == "" && noUrlForm["customURL"] == "", noUrl.Problem);

            var third = ProfilePlan.Make(new ProfileTemplate { Name = "player {n}", CustomUrl = "pl{n}" }, ProfileFields.Name | ProfileFields.CustomUrl, one, null, false, null, firstN: 3)[0];
            var fourth = ProfilePlan.Make(new ProfileTemplate { Name = "player {n}", CustomUrl = "pl{n}" }, ProfileFields.Name | ProfileFields.CustomUrl, one, null, false, null, firstN: 4)[0];
            Check("Perfis: conta nova sozinha numera pelo lugar na lista", third.PersonaName == "player 3" && third.CustomUrl == "pl3" && fourth.CustomUrl == "pl4" && third.Problem == null, third.PersonaName + "/" + third.CustomUrl);
            // A lista ordena por nome: "alpha" fica em 1.º lugar, mas o 1 já é do "player 1".
            var pl = new ProfileTemplate { Name = "player {n}", CustomUrl = "pl{n}" };
            var freeN = ProfilePlan.FreeN(pl, 3, "alpha", new[] { "player 1", "player 2" });
            var freeSkip = ProfilePlan.FreeN(pl, 3, "alpha", new[] { "player 1", "player 2", "Player 3", "player 5" });
            Check("Perfis: conta nova sozinha não repete um número já dado", freeN == 3 && freeSkip == 4 && ProfilePlan.FreeN(new ProfileTemplate { Name = "nv" }, 2, "x", new[] { "nv" }) == 2, freeN + "/" + freeSkip);
            var fixedUrl = ProfilePlan.Make(new ProfileTemplate { CustomUrl = "teamnv" }, ProfileFields.CustomUrl, one, null, false, null, firstN: 2)[0];
            Check("Perfis: conta nova sozinha com URL fixo recusada", fixedUrl.Problem?.Contains("{n}") == true, fixedUrl.Problem);

            var unknown = new List<ProfileTarget> { new(Id1, "drift_faceit", null, "drift_faceit") };
            var u1 = ProfilePlan.Make(new ProfileTemplate { Name = "{name} | NV" }, ProfileFields.Name, unknown, null, false)[0];
            Check("Perfis: {name} com nome desconhecido não publica o login", u1.Problem != null && u1.PersonaName?.Contains("drift_faceit") != true && u1.Shown == "drift_faceit", u1.Problem + " / " + u1.PersonaName);
            var u2 = ProfilePlan.Make(new ProfileTemplate { Name = "{name} | NV" }, ProfileFields.Name, new List<ProfileTarget> { new(Id1, "drift_faceit", "drift_faceit") }, null, false)[0];
            Check("Perfis: {name} igual ao login quando é mesmo o nome no Steam", u2.Problem == null && u2.PersonaName == "drift_faceit | NV", u2.Problem);

            Check("Perfis: {{ e }} são chavetas", NamePattern.Problem("{{NV}} nightfall") == null && NamePattern.Render("{{NV}} {n}", "a", 2) == "{NV} 2" && !NamePattern.HasN("{{n}}"), NamePattern.Render("{{NV}} {n}", "a", 2));
            Check("Perfis: nome copiado com chavetas fica igual", NamePattern.Render(NamePattern.Escape("pro{n} {x}"), "z", 7) == "pro{n} {x}" && NamePattern.Problem(NamePattern.Escape("{NV} nightfall")) == null, NamePattern.Render(NamePattern.Escape("pro{n} {x}"), "z", 7));
        }

        // ---------------- Repor depois de mudar o país: estado e cidade voltam
        {
            var snaps = new SnapshotStore(Path.Combine(tmp, "snapshots-loc"));
            var restore = snaps.RestoreJob(new ProfileSnapshot { SteamId = Id1, Info = new EditConfig { PersonaName = "a", Country = "PT", State = "14", City = "123" } }, "a");
            var f = ProfileForm.Build(new EditConfig { PersonaName = "b", Country = "", State = "", City = "" }, restore, "sid").ToDictionary(k => k.Key, k => k.Value);
            Check("Perfis: repor põe o país, o estado e a cidade", restore.State == "14" && restore.City == "123" && f["country"] == "PT" && f["state"] == "14" && f["city"] == "123", string.Join(";", f));
            var clean = ProfilePlan.Make(new ProfileTemplate(), ProfileFields.Country, new List<ProfileTarget> { new(Id1, "loginum", "a") }, null, false)[0];
            var g = ProfileForm.Build(new EditConfig { PersonaName = "a", Country = "PT", State = "14", City = "123" }, clean, "sid").ToDictionary(k => k.Key, k => k.Value);
            Check("Perfis: modelo sem país continua a limpar estado e cidade", g["country"] == "" && g["state"] == "" && g["city"] == "", string.Join(";", g));
        }

        // ---------------- Respostas estragadas do Steam dão SteamWebException (a janela de sign-in não fica presa)
        {
            var badJson = false;
            try { CommunitySession.ParseEditInfo("<div id=\"profile_edit_config\" data-profile-edit=\"{bad\"></div>"); } catch (SteamWebException) { badJson = true; }
            Check("Perfis: JSON estragado na página de edição dá erro claro", badJson, "");
            var html = new Fake { Answer = (_, _) => Text("<html>not protobuf</html>") };
            var htmlErr = false;
            try { await new SteamAuth(new SteamWeb(html)).EncryptPassword("x", new byte[] { 1 }, default); } catch (SteamWebException) { htmlErr = true; }
            Check("Perfis: resposta HTML no sign-in dá erro claro", htmlErr, "");
            var badKey = new Fake
            {
                Answer = (_, _) => { var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new ProtoWriter().Str(1, "ZZZZ").Str(2, "010001").ToArray()) }; r.Headers.Add("x-eresult", "1"); return r; },
            };
            var keyErr = false;
            try { await new SteamAuth(new SteamWeb(badKey)).EncryptPassword("x", new byte[] { 1 }, default); } catch (SteamWebException) { keyErr = true; }
            Check("Perfis: chave do Steam estragada dá erro claro", keyErr, "");
            var noId = false;
            try { await new SteamAuth(new SteamWeb(html)).SubmitCode(new AuthStart(), "ABCDE", GuardType.EmailCode, default); } catch (SteamWebException) { noId = true; }
            Check("Perfis: código sem SteamID na sessão dá erro claro", noId, "");
        }

        // ---------------- Gravação: localização pública e palavras dos modelos
        {
            var loc = "Seixal, Setubal, Portugal";
            var shown = Rec.Scrub("Now: " + loc, SRT.ProfileRow.LocationWords(loc));
            Check("Perfis: gravação tapa a localização pública", !shown.Contains("Seixal") && !shown.Contains("Setubal") && !shown.Contains("Portugal"), shown);
            var words = SRT.TemplateItem.Words(new ProfileTemplate { Title = "Team", Name = "xX_nick {n}", CustomUrl = "nick{n}" }).ToList();
            Check("Perfis: palavras de um modelo (nome e URL à volta das etiquetas)", words.Contains("xX_nick") && words.Contains("nick") && words.Contains("Team"), string.Join(",", words));
            // Nome copiado com chavetas: a caixa mostra "{{NV}} nightfall" e o filtro tem de a tapar inteira, não só "} nightfall".
            var copied = SRT.TemplateItem.Words(new ProfileTemplate { Name = NamePattern.Escape("{NV} nightfall") + " {n}" }).ToList();
            var box = Rec.Scrub("Name: {{NV}} nightfall {n}", copied);
            Check("Perfis: gravação tapa o nome copiado com chavetas", !box.Contains("NV") && !box.Contains("nightfall") && copied.Contains("{NV} nightfall"), box + " / " + string.Join(",", copied));
            var priv = CommunitySession.ParseLook("<profile><steamID>n</steamID><privacyState>private</privacyState><avatarFull>https://avatars.steamstatic.com/a.jpg</avatarFull></profile>");
            Check("Perfis: perfil privado não tem nome real, bio nem localização (ficam como estão)", priv.RealName == null && priv.Summary == null && priv.Location == null, "");
        }

        // ---------------- Ligações (junction) plantadas: nada se escreve nem se apaga do outro lado
        {
            var alvo = Path.Combine(tmp, "alvo");
            Directory.CreateDirectory(alvo);
            for (var i = 0; i < 8; i++) File.WriteAllText(Path.Combine(alvo, $"2026010{i}-000000-000.json"), "{}");
            File.WriteAllText(Path.Combine(alvo, Id1 + ".bin"), "x");
            var before = Directory.GetFiles(alvo).Length;
            var snapRoot = Path.Combine(tmp, "snapj");
            Directory.CreateDirectory(snapRoot);
            bool Junction(string link)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{alvo}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = System.Diagnostics.Process.Start(psi);
                p!.WaitForExit(10000);
                return p.ExitCode == 0 && Directory.Exists(link);
            }
            if (!Junction(Path.Combine(snapRoot, Id1)) || !Junction(Path.Combine(tmp, "sessj")))
                Env("Perfis: junction plantada (mklink /J)", false, "não deu para criar a junction aqui");
            else
            {
                var refused = false;
                try { new SnapshotStore(snapRoot).Save(Id1, new EditConfig { PersonaName = "a" }, null, new byte[] { 1, 2, 3 }); } catch (IOException) { refused = true; }
                var sess = new SessionStore(Path.Combine(tmp, "sessj"));
                var forgot = sess.ForgetAll() + (sess.Forget(Id1) ? 1 : 0);
                Check("Perfis: junction nas cópias ou nas sessões: nada escrito nem apagado", refused && forgot == 0 && !sess.Accounts().Any() && Directory.GetFiles(alvo).Length == before, $"{refused} {forgot} {Directory.GetFiles(alvo).Length}/{before}");
            }
        }

        // ---------------- Leitura das páginas do Steam
        {
            var info = CommunitySession.ParseEditInfo(Html(new { strPersonaName = "a\"b<c>", strRealName = "", strSummary = "x\ny", strCustomURL = "nf", LocationData = new { locCountryCode = "", locStateCode = "", locCityCode = "" } }));
            Check("Perfis: página de edição lida (aspas e < >)", info.PersonaName == "a\"b<c>" && info.Summary == "x\ny" && info.CustomUrl == "nf", info.PersonaName);
            var threw = false;
            try { CommunitySession.ParseEditInfo("<html>login</html>"); } catch (SteamWebException) { threw = true; }
            Check("Perfis: página inesperada dá erro claro", threw, "");
            var look = CommunitySession.ParseLook("<profile><steamID>n</steamID><summary><![CDATA[linha<br>dois &amp; <b>três</b>]]></summary><location>Lisboa, Portugal</location></profile>");
            Check("Perfis: XML público lido (bio sem HTML)", look.PersonaName == "n" && look.Summary == "linha\ndois & três", look.Summary);
            Env("Perfis: país a partir da localização", Countries.FromLocation(look.Location) == "PT", Countries.FromLocation(look.Location) ?? "nulo");
        }

        // ---------------- Copy look de qualquer link de perfil (outra conta, outro PC): só o perfil público, só hosts da Valve
        {
            Check("Link: /id/ com https", ProfileLink.Path("https://steamcommunity.com/id/nightfall") == "/id/nightfall", "");
            Check("Link: /id/ com barra, www e sem esquema", ProfileLink.Path("www.steamcommunity.com/id/night_fall-2/") == "/id/night_fall-2" && ProfileLink.Path("steamcommunity.com/id/ab") == "/id/ab", "");
            Check("Link: /profiles/ com http e query", ProfileLink.Path("http://steamcommunity.com/profiles/76561198000000042/?l=english") == "/profiles/76561198000000042", "");
            Check("Link: SteamID64 sozinho", ProfileLink.Path("  76561198000000042 ") == "/profiles/76561198000000042", "");
            var bad = new[]
            {
                "https://evil.example/steamcommunity.com/id/x", "https://steamcommunity.com.evil.example/id/x", "https://evilsteamcommunity.com/id/x",
                "https://user:pw@steamcommunity.com/id/x", "https://steamcommunity.com:8443/id/x", "ftp://steamcommunity.com/id/x",
                "https://steamcommunity.com/profiles/123", "https://steamcommunity.com/id/x/../../profiles/76561198000000042", "https://steamcommunity.com/id/a",
                "https://steamcommunity.com/id/nightfall/edit/info", "https://steamcommunity.com/groups/abc", "https://store.steampowered.com/app/730",
                "https://steamcommunity.com/id/n%2Fx", "javascript:alert(1)", "nightfall", "7656119800000004", "", null,
            };
            var through = bad.Where(b => ProfileLink.Path(b) != null).ToList();
            Check("Link: outros hosts, logins, portas, outras páginas e lixo recusados", through.Count == 0, string.Join(" | ", through));

            var fake = new Fake();
            fake.Answer = (req, _) =>
            {
                var u = req.RequestUri!;
                if (u.Host == "steamcommunity.com" && u.AbsolutePath == "/id/ghost/" && u.Query == "?xml=1")
                    return Text("<?xml version=\"1.0\"?><profile><steamID64>76561198000000099</steamID64><steamID><![CDATA[{NV} ghost]]></steamID><privacyState>private</privacyState>" +
                                "<avatarFull><![CDATA[https://avatars.steamstatic.com/ghost_full.jpg]]></avatarFull></profile>");
                if (u.Host == "steamcommunity.com" && u.AbsolutePath == "/profiles/76561198000000098/" && u.Query == "?xml=1")
                    return Text("<?xml version=\"1.0\"?><profile><steamID64>76561198000000098</steamID64><steamID>open</steamID><realname>Real Open</realname>" +
                                "<summary><![CDATA[bio<br>dois]]></summary><location>Lisboa, Portugal</location><avatarFull>https://evil.example/a.jpg</avatarFull></profile>");
                if (u.Host == "steamcommunity.com" && u.AbsolutePath == "/id/nobody/") return Text("<?xml version=\"1.0\"?><response><error><![CDATA[The specified profile could not be found.]]></error></response>");
                if (u.Host == "avatars.steamstatic.com")
                {
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png(184, 184)) };
                    r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    return r;
                }
                return null;
            };
            var web = new SteamWeb(fake);
            var priv = await CommunitySession.ReadPublicLookAt(web, ProfileLink.Path("https://steamcommunity.com/id/ghost/"), default);
            var copy = LookCopy.FromPublic(priv);
            var raw = await CommunitySession.DownloadAvatar(web, copy.AvatarUrl, default);
            var jpg = raw == null ? null : AvatarImage.Prepare(raw, out _);
            var t = new ProfileTemplate { Name = "antes", RealName = "x", Summary = "y", Country = "DE", CustomUrl = "meu{n}" };
            copy.Into(t, avatar: jpg != null);
            Check("Link: perfil privado copia nome e imagem, o resto fica como está", priv.SteamId64 == "76561198000000099" && t.Name == "{{NV}} ghost" && jpg != null
                && t.Fields == (ProfileFields.Name | ProfileFields.Avatar) && t.CustomUrl == "", $"{t.Name} {t.Fields}");
            var open = LookCopy.FromPublic(await CommunitySession.ReadPublicLookAt(web, ProfileLink.Path("76561198000000098"), default));
            var evilPic = await CommunitySession.DownloadAvatar(web, open.AvatarUrl, default);
            var t2 = new ProfileTemplate();
            open.Into(t2, avatar: evilPic != null);
            Env("Link: perfil público copia nome real, bio e país", t2.Name == "open" && t2.RealName == "Real Open" && t2.Summary == "bio\ndois" && t2.Country == "PT"
                && t2.Fields == (ProfileFields.Look & ~ProfileFields.Avatar), $"{t2.Country} {t2.Fields}");
            var emptyBio = LookCopy.FromPublic(new PublicLook { PersonaName = "src", RealName = "", Summary = " ", Location = "" }, onlyFilled: true);
            var t3 = new ProfileTemplate();
            emptyBio.Into(t3, avatar: false);
            Check("Link: campos vazios na origem ficam desmarcados (o Apply não apaga a bio nem o nome real)", t3.Fields == ProfileFields.Name, t3.Fields.ToString());
            var ownEmpty = LookCopy.FromPublic(new PublicLook { PersonaName = "src", RealName = "", Summary = "", Location = "" });
            Check("Link: da própria conta, vazio continua a ser um valor", ownEmpty.Unknown == 0, ownEmpty.Unknown.ToString());
            Check("Link: imagem fora dos hosts da Valve não é descarregada", evilPic == null && !fake.Seen.Any(s => s.Uri.Host.Contains("evil")), "");
            var missing = "";
            try { await CommunitySession.ReadPublicLookAt(web, "/id/nobody", default); } catch (SteamWebException ex) { missing = ex.Message; }
            Check("Link: perfil que não existe dá erro claro", missing == "Steam didn't show this profile.", missing);
            var refused = false;
            try { await CommunitySession.ReadPublicLookAt(web, "/id/x/../../evil", default); } catch (SteamWebException) { refused = true; }
            Check("Link: caminho fora do formato recusado antes de ir à rede", refused, "");
            Check("Link: só pedidos a steamcommunity.com e avatars, sem cookies", fake.Seen.All(s => SteamWeb.Allowed(s.Uri) && s.Cookie == null), string.Join(" ", fake.Seen.Select(s => s.Uri.Host).Distinct()));
        }
    }
}
