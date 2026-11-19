using System.IO;
using System.Text.Json;
using SRT;
using SRT.Core;

static class RegionGamesTests
{
    public static void Run(Action<string, bool, string> check)
    {
        // 19 nov.: the closed Game box of Regions showed "RegionGame { AppId = 730, ... }" (the theme's ComboBox shows the item's
        // ToString, not DisplayMemberPath); the name a player reads is the game's name
        check("Jogos: a caixa Game mostra o nome do jogo, não o objeto",
            RegionGames.All.All(g => g.ToString() == g.Name), string.Join(" | ", RegionGames.All.Select(g => g.ToString())));
        // 19 nov.: Valve's real answers for Dota 2 and Deadlock (the Codex folder had no network; the format was only guessed)
        foreach (var (app, min) in new[] { (570, 20), (1422450, 20) })
        {
            var real = File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tests", "fixtures", $"sdr-{app}-valve-2026-11-19.json"));
            var groups = Regions.Group(Regions.Parse(real, DateTime.Now));
            check($"Jogos: a lista real da Valve para o appid {app} dá regiões com relays", groups.Count >= min && groups.All(g => g.RelayIps.Count > 0),
                $"{groups.Count} regiões");
        }
        var preset = new RegionPreset(Guid.NewGuid(), "Europe evenings", new() { "lax" }, null);
        check("Presets: as caixas de presets mostram o nome do preset, não o objeto", preset.ToString() == "Europe evenings", preset.ToString());
        const string primary = @"C:\Example\Steam";
        const string library = @"D:\Games Library";
        const string folders = """ "libraryfolders" { "0" { "path" "C:\\Example\\Steam" } "1" { "path" "D:\\Games Library" } } """;
        const string dota = """ "AppState" { "appid" "570" "installdir" "Dota Test" } """;
        const string deadlock = """ "AppState" { "appid" "1422450" "installdir" "Deadlock Test" } """;
        var pathDota = Path.Combine(library, @"steamapps\common\Dota Test\game\bin\win64\dota2.exe");
        var pathDeadlock = Path.Combine(primary, @"steamapps\common\Deadlock Test\game\bin\win64\deadlock.exe");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(library, @"steamapps\appmanifest_570.acf")] = dota,
            [Path.Combine(primary, @"steamapps\appmanifest_1422450.acf")] = deadlock,
            [Path.Combine(primary, @"steamapps\appmanifest_570.acf")] = deadlock,
        };
        var executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pathDota, pathDeadlock };
        string Find(RegionGame game) => RegionGames.FindExe(game, primary, folders, p => files.GetValueOrDefault(p), executables.Contains);
        check("Jogos: encontra Dota 2 noutra biblioteca e Deadlock na biblioteca principal",
            Find(RegionGames.Dota2) == pathDota && Find(RegionGames.Deadlock) == pathDeadlock, "");
        files[Path.Combine(library, @"steamapps\appmanifest_570.acf")] = dota.Replace("Dota Test", "..\\\\game");
        check("Jogos: manifesto com outro appid ou com installdir fora da biblioteca é recusado", Find(RegionGames.Dota2) == null, "");
        executables.Remove(pathDeadlock);
        check("Jogos: jogo sem o executável indicado não fornece um alvo", Find(RegionGames.Deadlock) == null, "");

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(SecurityTests.Repo(), "tests", "fixtures", "sdr-games.json")));
        var dotaRows = Regions.Group(Regions.Parse(json.RootElement.GetProperty("570").GetRawText(), DateTime.UnixEpoch));
        var deadlockRows = Regions.Group(Regions.Parse(json.RootElement.GetProperty("1422450").GetRawText(), DateTime.UnixEpoch));
        var dotaRule = Regions.Rule(dotaRows, new[] { dotaRows.Single().Id }, pathDota, RegionGames.Dota2);
        var deadlockRule = Regions.Rule(deadlockRows, new[] { deadlockRows.Single().Id }, pathDeadlock, RegionGames.Deadlock);
        check("Jogos: cada lista só fornece os POPs do seu JSON e o alvo da sua regra",
            dotaRows.Single().Codes.Single() == "lim" && deadlockRows.Single().Codes.Single() == "osl" &&
            dotaRule.Program == pathDota && deadlockRule.Program == pathDeadlock &&
            dotaRule.RemoteIps.Single() == "192.0.2.21" && deadlockRule.RemoteIps.Single() == "198.51.100.31", "");
        var names = RegionGames.All.Select(game => game.RuleName).ToArray();
        check("Jogos: nomes de Firewall e caches distintos com nome antigo do CS2 preservado",
            names.Distinct().Count() == 3 && names[0] == Firewall.RegionsRuleName &&
            RegionGames.All.Select(game => game.CacheFile).Distinct().Count() == 3 &&
            RegionGames.Dota2.ConfigUrl.EndsWith("appid=570") && RegionGames.Deadlock.ConfigUrl.EndsWith("appid=1422450"), "");
        check("Jogos: restauro remove todas as regras e desligar proteção preserva-as",
            names.All(Firewall.RulesToRemove(false).Contains) && !names.Any(Firewall.RulesToRemove(true).Contains) &&
            Regions.RuleLeft(new[] { dotaRule.Name }, game: RegionGames.Dota2) && !Regions.RuleLeft(new[] { dotaRule.Name }), "");
        var oldNames = new[] { "ServerRouteTool v9 - CS2 regioes bloqueadas",
            "ServerRouteTool v9 - Dota 2 regions turned off", "ServerRouteTool v9 - Deadlock regions turned off" };
        check("Jogos: restauro e verificação reconhecem ambos os nomes de cada regra, incluindo temporárias",
            RegionGames.All.Zip(oldNames).All(pair => new[] { pair.First.RuleName, pair.Second }
                .All(name => Firewall.IsRegionRule(name) && Regions.RuleLeft(new[] { name + " (updating test)" }, game: pair.First)
                    && Firewall.Has(new[] { name }, pair.First.RuleName)) && Firewall.LegacyName(pair.First.RuleName) == pair.Second)
            && !Firewall.IsRegionRule(dotaRule.Name + " unrelated") && !Firewall.IsRegionRule(Firewall.RuleName), "");

        var settings = JsonSerializer.Deserialize<Settings>("""{"BlockedRegions":["old"]}""");
        var id = Guid.NewGuid();
        settings.DotaRegions.RegionPresets.Add(new(id, "Same name", new() { dotaRows.Single().Id }, null));
        settings.DotaRegions.RegionPresetOnStart = id;
        settings.DeadlockRegions.RegionPresets.Add(new(Guid.NewGuid(), "Same name", new() { deadlockRows.Single().Id }, null));
        var saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings));
        check("Jogos: presets e seleções separados e JSON antigo de CS2 preservado",
            saved.BlockedRegions.SequenceEqual(new[] { "old" }) && saved.RegionPresets.Count == 0 &&
            saved.DotaRegions.RegionPresets.Single().Id == saved.DotaRegions.RegionPresetOnStart &&
            saved.DeadlockRegions.RegionPresets.Single().Blocked.Single() == deadlockRows.Single().Id, "");
    }
}
