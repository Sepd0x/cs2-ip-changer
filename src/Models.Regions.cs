using System.Windows.Media;
using SRT.Core;

namespace SRT;

public sealed partial class Settings
{
    public List<string> BlockedRegions { get; set; } = new();   // Regions page: cities whose Valve relays cs2.exe can't reach
    public List<string> RegionsKeep { get; set; }                 // set by a shortcut: only these stay on (ids or "continent:Europe"); null = turned off one by one
    public long RegionsRevision { get; set; }                     // revision of Valve's relay list the rule was last built from
    public int RegionsMaxPing { get; set; } = 60;                // the "Ping under" shortcut
}

/// <summary>One row on the Regions page.</summary>
public sealed class RegionItem : Observable
{
    bool _allowed = true;
    int _pingMs = -2;   // -2 = not measured, -1 = no answer

    public RegionInfo Info { get; init; }
    public string Id => Info.Id;
    public string City => Info.City;
    public string Country => Info.Area != null ? $"{Info.Country} · {Info.Area}" : Info.Country;
    public string Continent => Info.Continent;
    public int ContinentRank => Regions.ContinentRank(Info.Continent);
    public string RelayText => Info.RelayIps.Count == 1 ? "1 relay" : $"{Info.RelayIps.Count} relays";
    public string Tip => $"Valve code: {string.Join(", ", Info.Codes)} · {RelayText}";

    /// <summary>On = CS2 can reach this region. Off = its relays are blocked for cs2.exe.</summary>
    public bool Allowed { get => _allowed; set => Set(ref _allowed, value); }

    public int PingMs
    {
        get => _pingMs;
        set { if (Set(ref _pingMs, value)) { Raise(nameof(PingText)); Raise(nameof(PingBrush)); Raise(nameof(SortPing)); } }
    }
    public int SortPing => PingMs < 0 ? int.MaxValue : PingMs;
    public string PingText => PingMs switch { -2 => "—", -1 => "no answer", _ => $"{PingMs} ms" };
    // The same colors as the Servers page.
    public Brush PingBrush => PingMs < 0 ? Ui.Faint : PingMs < 60 ? Ui.Good : PingMs < 120 ? Ui.Warn : Ui.Bad;
}
