using System.IO;
using System.Net.Http;
using SRT.Core;

// Regions (beta.5): a lista real da Valve guardada em tests/fixtures/sdrconfig.json (29 out. 2026), a regra, os atalhos e a cache.
static class RegionTests
{
    public delegate void CheckFn(string name, bool ok, string detail = "");

    static string Fixture()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "tests", "fixtures", "sdrconfig.json");
            if (File.Exists(f)) return f;
            f = Path.Combine(d.FullName, "fixtures", "sdrconfig.json");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    public static void Run(CheckFn check, CheckFn env)
    {
        var path = Fixture();
        check("Regiões: fixture encontrada", path != null, path ?? "");
        if (path == null) return;
        var json = File.ReadAllText(path);

        // Ler o JSON real
        var cfg = Regions.Parse(json, DateTime.Now);
        check("Regiões: revisão lida", cfg.Revision == 1793064244, cfg.Revision.ToString());
        check("Regiões: 45 pops com relays (os sem relays ficam de fora)", cfg.Pops.Count == 45, cfg.Pops.Count.ToString());
        var fra = cfg.Pops.FirstOrDefault(p => p.Code == "fra");
        check("Regiões: Frankfurt com 14 relays e portas 27015-27060", fra?.Relays.Count == 14 && fra.Relays.All(r => r.PortLo == 27015 && r.PortHi == 27060), fra?.Relays.Count.ToString());
        check("Regiões: pop sem relays (fsn) fora", cfg.Pops.All(p => p.Code != "fsn"));
        check("Regiões: JSON sem pops recusado", Throws(() => Regions.Parse("{\"revision\":1}", DateTime.Now)));

        // Agrupar por cidade, país e continente
        var regions = Regions.Group(cfg);
        RegionInfo R(string id) => regions.FirstOrDefault(r => r.Id == id);
        var fr = R("frankfurt-germany");
        check("Regiões: Frankfurt junta fra e dfra", fr != null && fr.Codes.Contains("fra") && fr.Codes.Contains("dfra") && fr.RelayIps.Count == 16, fr == null ? "" : string.Join(",", fr.Codes) + " " + fr.RelayIps.Count);
        var sto = R("stockholm-sweden");
        check("Regiões: Estocolmo junta Kista e Bromma", sto != null && sto.Codes.Contains("sto") && sto.Codes.Contains("sto2"));
        var atl = R("atlanta-united-states");
        check("Regiões: Atlanta (Georgia) é dos Estados Unidos", atl?.Country == "United States" && atl.Area == "Georgia" && atl.Continent == "North America");
        check("Regiões: London no Reino Unido, Europa", R("london-united-kingdom")?.Continent == "Europe");
        check("Regiões: Tokyo Koto City vira Tokyo", R("tokyo-japan") != null);
        check("Regiões: Hong Kong na Ásia", R("hong-kong-hong-kong")?.Continent == "Asia");
        check("Regiões: Dubai no Médio Oriente", R("dubai-united-arab-emirates")?.Continent == "Middle East");
        check("Regiões: Fujairah (UAE) com o nome do país completo", R("fujairah-united-arab-emirates") != null);
        check("Regiões: todas com continente conhecido", regions.All(r => Regions.ContinentRank(r.Continent) < 99), string.Join(",", regions.Where(r => Regions.ContinentRank(r.Continent) >= 99).Select(r => r.Id)));
        check("Regiões: ids únicos", regions.Select(r => r.Id).Distinct().Count() == regions.Count);
        check("Regiões: ordenadas por continente", regions.Select(r => Regions.ContinentRank(r.Continent)).SequenceEqual(regions.Select(r => Regions.ContinentRank(r.Continent)).OrderBy(x => x)));
        var lagos = new Pop("los", "Lagos (Nigeria)", 3.4, 6.5, new[] { new Relay("203.0.113.1", 27015, 27060) });
        check("Regiões: país novo vai pelo mapa (Lagos, África)", Regions.Continent(Regions.Describe(lagos).Country, lagos.Lon, lagos.Lat) == "Africa");

        // A regra: só cs2.exe, só os relays das regiões bloqueadas
        const string cs2 = @"C:\Games\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";
        var rule = Regions.Rule(regions, new[] { "frankfurt-germany", "madrid-spain" }, cs2);
        var expected = fr.RelayIps.Concat(R("madrid-spain").RelayIps).ToHashSet();
        check("Regiões: regra com o nome da app", rule?.Name == Firewall.RegionsRuleName && rule.Name.StartsWith("ServerRouteTool v9"));
        check("Regiões: regra só para o cs2.exe", rule?.Program == cs2);
        check("Regiões: regra com os IPs certos, nem mais nem menos", rule != null && rule.RemoteIps.ToHashSet().SetEquals(expected), $"{rule?.RemoteIps.Count} de {expected.Count}");
        check("Regiões: regra bloqueia de qualquer origem, qualquer protocolo", rule?.AllowLocalIp == null && rule.LocalAddresses == null && rule.Protocol == Firewall.NET_FW_IP_PROTOCOL_ANY && rule.RemotePorts == null);
        check("Regiões: Amsterdão não entra", !rule.RemoteIps.Intersect(R("amsterdam-netherlands").RemoteIpsOrEmpty()).Any());
        check("Regiões: nada bloqueado = sem regra", Regions.Rule(regions, Array.Empty<string>(), cs2) == null);
        check("Regiões: sem CS2 = sem regra", Regions.Rule(regions, new[] { "frankfurt-germany" }, null) == null);
        check("Regiões: cidade que a Valve tirou não acrescenta nada", Regions.BlockedIps(regions, new[] { "atlantis-nowhere" }).Count == 0);
        var allIps = regions.SelectMany(r => r.RelayIps).ToList();
        check("Regiões: os relays estão nos blocos da Valve (o Steam usa os mesmos, só o cs2.exe é bloqueado)", allIps.Count(Net.IsValve) * 2 > allIps.Count, $"{allIps.Count(Net.IsValve)} de {allIps.Count}");

        // Atalhos
        var pings = new Dictionary<string, int> { ["a"] = 40, ["b"] = 12, ["c"] = -1, ["d"] = 25, ["e"] = 90, ["f"] = 25 };
        check("Regiões: 3 mais perto (sem resposta fica de fora)", Regions.Closest(pings, 3).SequenceEqual(new[] { "b", "d", "f" }), string.Join(",", Regions.Closest(pings, 3)));
        check("Regiões: mais perto com poucas medidas", Regions.Closest(new Dictionary<string, int> { ["a"] = -1, ["b"] = 30 }, 3).SequenceEqual(new[] { "b" }));
        var only = Regions.AllExcept(regions, new[] { "frankfurt-germany" });
        check("Regiões: só esta região bloqueia todas as outras", only.Count == regions.Count - 1 && !only.Contains("frankfurt-germany"));
        var europe = Regions.AllExcept(regions, regions.Where(r => r.Continent == "Europe").Select(r => r.Id));
        check("Regiões: Só Europa", europe.All(id => R(id).Continent != "Europe") && europe.Count == regions.Count(r => r.Continent != "Europe"));

        // Atalho guardado como o que fica ligado: uma cidade que a Valve junta depois fica desligada (F21)
        var lisbon = new Pop("lis", "Lisbon (Portugal)", -9.1, 38.7, new[] { new Relay("203.0.113.2", 27015, 27060) });
        var grown = Regions.Group(new SdrConfig { Revision = cfg.Revision + 1, Pops = cfg.Pops.Concat(new[] { lagos, lisbon }).ToList(), FetchedAt = DateTime.Now });
        var keptFra = Regions.KeepOnly(grown, new[] { "frankfurt-germany" });
        check("Regiões: só Frankfurt guardado, Lagos nova fica desligada", keptFra != null && keptFra.Contains("lagos-nigeria") && keptFra.Contains("lisbon-portugal") && !keptFra.Contains("frankfurt-germany") && keptFra.Count == grown.Count - 1, keptFra?.Count.ToString());
        check("Regiões: a regra refeita apanha o relay novo", Regions.Rule(grown, keptFra, cs2)?.RemoteIps.Contains("203.0.113.1") == true);
        var keptEu = Regions.KeepOnly(grown, new[] { Regions.ContinentKey("Europe") });
        check("Regiões: Só Europa guardado, Lisboa nova fica ligada e Lagos desligada", keptEu != null && !keptEu.Contains("lisbon-portugal") && keptEu.Contains("lagos-nigeria") && !keptEu.Contains("frankfurt-germany"));
        check("Regiões: atalho com a cidade renomeada não bloqueia tudo", Regions.KeepOnly(grown, new[] { "atlantis-nowhere" }) == null);

        // A leitura de hora a hora só refaz a lista quando ela mudou (senão a lista salta para o topo)
        var moved = cfg.Pops.Select(p => p.Code == "fra" ? p with { Relays = p.Relays.Take(p.Relays.Count - 1).Append(new Relay("203.0.113.9", 27015, 27060)).ToList() } : p).ToList();
        check("Regiões: a mesma lista lida outra vez não refaz as linhas", Regions.SameRows(regions, Regions.Group(Regions.Parse(json, DateTime.Now.AddHours(1)))));
        check("Regiões: cidade nova refaz as linhas", !Regions.SameRows(regions, grown));
        check("Regiões: relay trocado na mesma cidade refaz as linhas", !Regions.SameRows(regions, Regions.Group(new SdrConfig { Revision = cfg.Revision, Pops = moved, FetchedAt = DateTime.Now })));
        check("Regiões: sem lista antes refaz as linhas", !Regions.SameRows(null, regions));

        // Regra que sobrou: o nome da app, a cópia a meio de uma atualização, e uma Firewall que não se lê (F33, F57)
        check("Regiões: regra das regiões encontrada", Regions.RuleLeft(new[] { Firewall.RuleName, Firewall.RegionsRuleName }));
        check("Regiões: cópia temporária conta como regra", Regions.RuleLeft(new[] { Firewall.RegionsRuleName + " (updating 0123456789a)" }));
        check("Regiões: só as outras regras = sem regra das regiões", !Regions.RuleLeft(new[] { Firewall.RuleName, Firewall.Cs2RuleName }));
        check("Regiões: Firewall ilegível só conta quando pedido", !Regions.RuleLeft(new[] { "(could not read the Firewall: x)" }) && Regions.RuleLeft(new[] { "(could not read the Firewall: x)" }, unreadableCounts: true));

        // Verificação de 30 s: um Allow all que falhou também se tenta outra vez depois dos 2 min (F56, F57)
        var agora = new DateTime(2026, 10, 29, 12, 0, 0);
        check("Regiões: Allow all falhado volta a tentar depois da espera", Regions.ApplyDue(0, false, "Couldn't change the Firewall: x", false, agora, agora.AddMinutes(-1)));
        check("Regiões: Allow all falhado espera os 2 min", !Regions.ApplyDue(0, false, "Couldn't change the Firewall: x", false, agora, agora.AddMinutes(1)));
        check("Regiões: nada bloqueado e sem erro = nada a fazer", !Regions.ApplyDue(0, false, null, false, agora, agora.AddMinutes(-1)));
        check("Regiões: escrita falhada volta a tentar depois da espera", Regions.ApplyDue(3, false, "x", false, agora, agora.AddMinutes(-1)) && !Regions.ApplyDue(3, false, "x", false, agora, agora.AddMinutes(1)));
        check("Regiões: regra ligada não se reescreve", !Regions.ApplyDue(3, true, null, false, agora, agora.AddMinutes(-1)));

        // Cache: a última lista boa serve sem rede
        var dir = Path.Combine(Path.GetTempPath(), "cs2ipchanger-regions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cache = Path.Combine(dir, "sdrconfig.json");
            var none = Regions.Load(cache, () => throw new HttpRequestException("offline")).GetAwaiter().GetResult();
            check("Regiões: sem rede e sem cache = nada", none == null);
            var online = Regions.Load(cache, () => Task.FromResult(json)).GetAwaiter().GetResult();
            check("Regiões: com rede lê e guarda a cópia", online is { FromCache: false } && File.Exists(cache));
            var offline = Regions.Load(cache, () => throw new HttpRequestException("offline")).GetAwaiter().GetResult();
            check("Regiões: sem rede usa a cópia guardada", offline is { FromCache: true } && offline.Revision == cfg.Revision && offline.Pops.Count == cfg.Pops.Count);
            var garbage = Regions.Load(cache, () => Task.FromResult("<html>Steam is down</html>")).GetAwaiter().GetResult();
            check("Regiões: resposta estragada não estraga a cópia", garbage is { FromCache: true } && Regions.Parse(File.ReadAllText(cache), DateTime.Now).Revision == cfg.Revision);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // A lista ao vivo (depende da rede deste PC)
        // A cópia vai para o %TEMP% e apaga-se logo (F66)
        var liveCache = Path.Combine(Path.GetTempPath(), "cs2ipchanger-live-" + Guid.NewGuid().ToString("N") + ".json");
        SdrConfig live;
        try { live = Regions.Load(liveCache).GetAwaiter().GetResult(); }
        finally { try { File.Delete(liveCache); File.Delete(liveCache + ".tmp"); } catch { } }
        check("Regiões: a lista ao vivo não fica no %TEMP%", !File.Exists(liveCache));
        env("Regiões: lista ao vivo da Valve", live is { FromCache: false } && live.Pops.Count > 20, live == null ? "sem resposta" : $"revisão {live.Revision}, {live.Pops.Count} pops");
    }

    /// <summary>"&lt;continente&gt; only": o do ping mais baixo; sem pings, o do país de casa; sem nada, nenhum (o botão some).</summary>
    public static void Continent(CheckFn check)
    {
        var rs = new List<RegionInfo>
        {
            new("lisboa", "Lisboa", null, "Portugal", "Europe", new[] { "lis" }, new[] { "203.0.113.1" }),
            new("ashburn", "Ashburn", "Virginia", "United States", "North America", new[] { "iad" }, new[] { "203.0.113.2" }),
            new("tokyo", "Tokyo", null, "Japan", "Asia", new[] { "tyo" }, new[] { "203.0.113.3" }),
            new("sao-paulo", "Sao Paulo", null, "Brazil", "South America", new[] { "gru" }, new[] { "203.0.113.4" }),
        };
        var pings = new Dictionary<string, int> { ["lisboa"] = 140, ["ashburn"] = 22, ["tokyo"] = -1, ["sao-paulo"] = 160 };
        check("Regiões: continente do ping mais baixo (ganha ao país de casa)", Regions.HomeContinent(rs, pings, "Portugal") == "North America", Regions.HomeContinent(rs, pings, "Portugal") ?? "nulo");
        check("Regiões: região sem resposta (-1) não conta", Regions.HomeContinent(rs, new Dictionary<string, int> { ["tokyo"] = -1, ["sao-paulo"] = 90 }, null) == "South America");
        check("Regiões: sem pings, o país de casa com região na lista", Regions.HomeContinent(rs, new Dictionary<string, int>(), "Japan") == "Asia");
        check("Regiões: sem pings, país da tabela sem região própria", Regions.HomeContinent(rs, null, "Germany") == "Europe");
        check("Regiões: nome de país como o ipwho.is o dá (England)", Regions.HomeContinent(rs, null, "England") == "Europe");
        check("Regiões: continente que não está na lista não conta", Regions.HomeContinent(rs.Where(r => r.Continent != "Europe").ToList(), null, "Portugal") == null);
        check("Regiões: sem pings nem país, nenhum (botão escondido)", Regions.HomeContinent(rs, null, null) == null && Regions.HomeContinent(rs, null, "Atlantis") == null);
        check("Regiões: sem lista, nenhum", Regions.HomeContinent(new List<RegionInfo>(), pings, "Portugal") == null);
        check("Regiões: Turn off protection guarda a regra das regiões", !Firewall.RulesToRemove(keepRegions: true).Contains(Firewall.RegionsRuleName)
            && Firewall.RulesToRemove(keepRegions: true).Contains(Firewall.RuleName) && Firewall.RulesToRemove(keepRegions: true).Contains(Firewall.Cs2RuleName));
        check("Regiões: --restore tira tudo, a regra das regiões também", Firewall.RulesToRemove(keepRegions: false).Contains(Firewall.RegionsRuleName)
            && Firewall.RulesToRemove(keepRegions: false).Length == Firewall.RulesToRemove(keepRegions: true).Length + 1);
    }

    static IEnumerable<string> RemoteIpsOrEmpty(this RegionInfo r) => r?.RelayIps ?? Enumerable.Empty<string>();

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    // Com admin (AdminTests): a regra é criada com o prefixo da app, atualizada sem duplicados, e o Restore tira-a.
    // Só IPs de documentação (RFC 5737) e o Bloco de Notas como programa. Apaga a regra no fim, mesmo que falhe.
    public static async Task Admin(CheckFn check)
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var regions = new List<RegionInfo>
        {
            new("teste-a", "Teste A", null, "Testland", "Europe", new[] { "tsa" }, new[] { "203.0.113.21", "203.0.113.22" }),
            new("teste-b", "Teste B", null, "Testland", "Europe", new[] { "tsb" }, new[] { "198.51.100.31" }),
            new("teste-c", "Teste C", null, "Testland", "Asia", new[] { "tsc" }, new[] { "192.0.2.41" }),
        };
        try
        {
            Regions.Apply(regions, new[] { "teste-a" }, notepad);
            var rule = Regions.Rule(regions, new[] { "teste-a" }, notepad);
            check("Regiões admin: regra criada e verificada", Firewall.Verify(rule), "");
            check("Regiões admin: com o prefixo da app", Firewall.Remaining().Contains(Firewall.RegionsRuleName), "");
            check("Regiões admin: regra que sobrou é vista", Regions.RuleLeft(Firewall.Remaining()), "");

            Regions.Apply(regions, new[] { "teste-a", "teste-b" }, notepad);
            rule = Regions.Rule(regions, new[] { "teste-a", "teste-b" }, notepad);
            check("Regiões admin: atualizada com a região nova", Firewall.Verify(rule), "");
            check("Regiões admin: uma só regra", Firewall.Remaining().Count(n => n.StartsWith(Firewall.RegionsRuleName)) == 1, "");

            Regions.Apply(regions, Array.Empty<string>(), notepad);
            check("Regiões admin: tudo permitido tira a regra", !Firewall.Remaining().Any(n => n.StartsWith(Firewall.RegionsRuleName)), "");
            check("Regiões admin: a leitura depois de tirar diz que saiu", !Regions.RuleLeft(Firewall.Remaining(), unreadableCounts: true), "");

            Regions.Apply(regions, new[] { "teste-c" }, notepad);
            var kept = await new Guard().RestoreAll(Array.Empty<string>(), keepRegions: true);
            check("Regiões admin: Turn off protection deixa a regra", Firewall.Remaining().Any(n => n.StartsWith(Firewall.RegionsRuleName)) && kept == null, kept ?? "");
            var left = await new Guard().RestoreAll(Array.Empty<string>(), keepRegions: false);
            check("Regiões admin: --restore tira a regra", !Firewall.Remaining().Any(n => n.StartsWith(Firewall.RegionsRuleName)), left ?? "");
        }
        finally { Firewall.Remove(Firewall.RegionsRuleName); }
    }
}
