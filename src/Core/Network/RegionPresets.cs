namespace SRT.Core;

public sealed record RegionPreset(Guid Id, string Name, List<string> Blocked, List<string> Keep)
{
    /// <summary>The preset's name: the closed preset boxes of Regions show the item's text, not DisplayMemberPath (theme ComboBox).</summary>
    public override string ToString() => Name;
}

public static class RegionPresets
{
    public const int Limit = 20;
    public const int NameLimit = 48;

    public static string NameError(IReadOnlyList<RegionPreset> presets, string name, Guid? except)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Any(char.IsControl)) return "Enter a preset name on one line.";
        if (name.Length > NameLimit) return "The preset name is too long.";
        if (presets?.Any(p => p != null && p.Id != except && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == true)
            return "A preset already has that name.";
        return null;
    }

    public static List<RegionPreset> Save(IReadOnlyList<RegionPreset> presets, Guid id, string name,
        IEnumerable<string> blocked, IEnumerable<string> keep, out string error)
    {
        error = presets?.Count >= Limit ? "The preset list is full. Remove a preset to save another." : NameError(presets, name, null);
        if (error != null) return null;
        var copy = presets?.ToList() ?? new();
        copy.Add(new RegionPreset(id, name.Trim(), blocked.ToList(), keep?.ToList()));
        return copy;
    }

    public static List<RegionPreset> Rename(IReadOnlyList<RegionPreset> presets, Guid id, string name, out string error)
    {
        error = NameError(presets, name, id);
        return error == null ? presets?.Select(p => p?.Id == id ? p with { Name = name.Trim() } : p).ToList() : null;
    }

    public static List<RegionPreset> Remove(IReadOnlyList<RegionPreset> presets, Guid id) =>
        presets?.Where(p => p != null && p.Id != id).ToList() ?? new();

    public static RegionPreset OnStart(IReadOnlyList<RegionPreset> presets, Guid? id) =>
        id is Guid key && key != Guid.Empty ? presets?.FirstOrDefault(p => p?.Id == key) : null;

    public static (HashSet<string> Blocked, IReadOnlyList<string> Keep)? Resolve(RegionPreset preset, IReadOnlyList<RegionInfo> regions)
    {
        if (regions is not { Count: > 0 }) return null;
        var known = regions.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (preset.Keep is { Count: > 0 } && Regions.KeepOnly(regions, preset.Keep) is { } shortcut)
            return (shortcut, preset.Keep);

        var blocked = new HashSet<string>(preset.Blocked ?? new(), StringComparer.Ordinal);
        blocked.IntersectWith(known);
        return blocked.Count == known.Count ? null : (blocked, null);
    }
}
