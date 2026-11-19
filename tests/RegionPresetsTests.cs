using System.Text.Json;
using SRT;
using SRT.Core;

static class RegionPresetsTests
{
    public static void Run(Action<string, bool, string> check)
    {
        var old = JsonSerializer.Deserialize<Settings>("""{"BlockedRegions":["mad"]}""");
        check("Presets: settings antigas preservam a seleção e não ativam o arranque",
            old.BlockedRegions.SequenceEqual(new[] { "mad" }) && old.RegionPresets.Count == 0 && old.RegionPresetOnStart == null, "");

        var id = Guid.NewGuid();
        var list = RegionPresets.Save(old.RegionPresets, id, "  Europe  ", old.BlockedRegions, new[] { "fra" }, out var error);
        check("Presets: guardar recorta o nome e guarda uma cópia da seleção",
            error == null && list.Single().Name == "Europe" && list.Single().Blocked.SequenceEqual(new[] { "mad" }), "");
        old.BlockedRegions.Clear();
        check("Presets: editar a seleção atual não muda o preset", list.Single().Blocked.Count == 1, "");
        check("Presets: rejeita vazio, repetido, comprido e caracteres de controlo",
            RegionPresets.NameError(list, " ", null) != null &&
            RegionPresets.NameError(list, "eUROPE", null) != null &&
            RegionPresets.NameError(list, new string('x', RegionPresets.NameLimit + 1), null) != null &&
            RegionPresets.NameError(list, "Euro\npe", null) != null, "");

        var full = Enumerable.Range(0, RegionPresets.Limit)
            .Select(i => new RegionPreset(Guid.NewGuid(), i.ToString(), new(), null)).ToList();
        check("Presets: limite de criação sem impedir renomear",
            RegionPresets.Save(full, Guid.NewGuid(), "Extra", Array.Empty<string>(), null, out _) == null &&
            RegionPresets.Rename(full, full[0].Id, "New name", out _)?.Count == full.Count, "");

        var renamed = RegionPresets.Rename(list, id, "EU only", out _);
        old.RegionPresets = renamed;
        old.RegionPresetOnStart = id;
        var roundTrip = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(old));
        var startup = RegionPresets.OnStart(roundTrip.RegionPresets, roundTrip.RegionPresetOnStart);
        check("Presets: JSON preserva o nome novo, o atalho e a referência de arranque",
            startup?.Name == "EU only" && startup.Keep.SequenceEqual(new[] { "fra" }) && startup.Blocked.SequenceEqual(new[] { "mad" }), "");
        var removed = RegionPresets.Remove(roundTrip.RegionPresets, id);
        check("Presets: retirar invalida a referência de arranque e preserva a lista anterior",
            RegionPresets.OnStart(removed, id) == null && roundTrip.RegionPresets.Count == 1, "");
        check("Presets: arranque desativado ou sem lista não escolhe um preset",
            RegionPresets.OnStart(list, null) == null && RegionPresets.OnStart(null, id) == null, "");

        var today = new[] { Region("fra"), Region("osl") };
        check("Presets: região antiga ignorada e atalho inclui as regiões novas",
            RegionPresets.Resolve(list.Single(), today)?.Blocked.SetEquals(new[] { "osl" }) == true, "");
        var manual = list.Single() with { Keep = null };
        check("Presets: uma região bloqueada que desapareceu não cria regra",
            RegionPresets.Resolve(manual, today)?.Blocked.Count == 0, "");
        var lost = manual with { Blocked = new() { "fra", "osl" }, Keep = new() { "gone" } };
        check("Presets: uma lista atualizada nunca deixa todas as regiões bloqueadas",
            RegionPresets.Resolve(lost, today) == null && RegionPresets.Resolve(manual, Array.Empty<RegionInfo>()) == null, "");
    }

    static RegionInfo Region(string id) => new(id, id, null, null, "Europe", Array.Empty<string>(), Array.Empty<string>());
}
