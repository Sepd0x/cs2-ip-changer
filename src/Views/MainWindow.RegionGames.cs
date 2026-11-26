using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using SRT.Core;

namespace SRT;

public partial class MainWindow
{
    sealed class RegionGameState(RegionGame game, RegionSettings settings)
    {
        public readonly RegionGame Game = game;
        public readonly RegionSettings Settings = settings;
        public readonly ObservableCollection<RegionItem> Rows = new();
        public ICollectionView View;
        public SdrConfig Config;
        public List<RegionInfo> Infos;
        public bool Loading, Pinging, RuleOn, NoGame, Checking, PingedHome, HomeIsPhone;
        public string Error;
        public DateTime PingedAt, RetryAt, TriedAt;
        public (int Pid, DateTime Start, Guid Id)? PendingPreset;
        public DateTime LastObservedStart, PresetSetAt;
    }

    RegionGameState _cs2Regions, _regionGame;
    RegionGameState[] _regionGames = Array.Empty<RegionGameState>();
    bool _regionRulesReady;
    string _cs2RegionWarning, _cs2RegionTip, _cs2AllowAllTip, _cs2RefreshTip;

    void InitRegionGames()
    {
        _cs2RegionWarning = RegionsWarning.Text;
        _cs2RegionTip = RegionsWarningNote.ToolTip as string;
        _cs2AllowAllTip = RegionsAllowAllBtn.ToolTip as string;
        _cs2RefreshTip = RegionsRefreshBtn.ToolTip as string;
        _cs2Regions = new(RegionGames.Cs2, _s);
        _regionGames = new[]
        {
            _cs2Regions, new RegionGameState(RegionGames.Dota2, _s.DotaRegions ??= new()),
            new RegionGameState(RegionGames.Deadlock, _s.DeadlockRegions ??= new()),
        };
        RegionGamePicker.ItemsSource = _regionGames.Select(state => state.Game);
        RegionGamePicker.SelectedItem = RegionGames.Cs2;
    }

    async void RegionGamePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RegionGamePicker.SelectedItem is not RegionGame game) return;
        var state = _regionGames.Single(entry => entry.Game == game);
        _regionGame = state;
        var cs2 = state == _cs2Regions;
        if (!cs2) RegionsPitch.Visibility = System.Windows.Visibility.Collapsed;
        RegionsWarning.Text = cs2 ? _cs2RegionWarning : Loc.F("Change regions between matches. If {0} keeps using the previous regions, restart it. Leaving too few regions on can make the queue longer.", game.Name);
        RegionsWarningMore.Visibility = cs2 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;   // CS2's line opens to the rest of it
        RegionsWarningNote.ToolTip = cs2 ? _cs2RegionTip : Loc.F("Only {0}'s access to Valve's relays changes. Valve can still send a match to a blocked region through a relay you allow.", game.Name);
        RegionsAllowAllBtn.ToolTip = cs2 ? _cs2AllowAllTip : Loc.F("Turns every region back on and removes the Firewall rule for {0}.", game.Name);
        RegionsRefreshBtn.ToolTip = cs2 ? _cs2RefreshTip : Loc.T("Reads Valve's relay list again and measures the ping to every region.");
        if (state.View == null)
        {
            state.View = CollectionViewSource.GetDefaultView(state.Rows);
            state.View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegionItem.Continent), new LocName()));   // continents in the app's language
            state.View.SortDescriptions.Add(new SortDescription(nameof(RegionItem.ContinentRank), ListSortDirection.Ascending));
            state.View.SortDescriptions.Add(new SortDescription(nameof(RegionItem.SortPing), ListSortDirection.Ascending));
            state.View.SortDescriptions.Add(new SortDescription(nameof(RegionItem.City), ListSortDirection.Ascending));
        }
        RegionList.ItemsSource = state.View;
        RegionDialView.ItemsSource = state.Rows;
        RegionDialView.SpokeClicked -= RegionDial_SpokeClicked;
        RegionDialView.SpokeClicked += RegionDial_SpokeClicked;
        RegionsMaxPing.Value = Math.Clamp(state.Settings.RegionsMaxPing, 20, 200);
        RegionsMaxPingText.Text = Loc.F("{0} ms", (int)RegionsMaxPing.Value);
        RefreshRegionPresets();
        ShowRegionsStatus();
        if (_regionRulesReady && !_demo) await OpenRegionGame(state);
    }

    async Task OpenRegionGame(RegionGameState state)
    {
        if (state.Config == null || (DateTime.Now - state.Config.FetchedAt).TotalHours >= 1)
            await LoadRegions(atStart: false, state: state);
        if (_regionGame == state && (DateTime.Now - state.PingedAt).TotalMinutes >= 2) await PingRegions(quiet: true);
    }

    string RegionGameExe(RegionGameState state) =>
        state == _cs2Regions ? Cs2Exe() : RegionGames.FindInstalledExe(state.Game);

    void WatchOtherRegionGames()
    {
        foreach (var state in _regionGames.Where(state => state != _cs2Regions && state.Settings.RegionPresetOnStart != null))
        {
            var processes = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(state.Game.Exe));
            foreach (var process in processes)
            {
                using (process)
                {
                    DateTime start;
                    try { start = process.StartTime; }
                    catch (InvalidOperationException) { continue; }
                    catch (Win32Exception) { continue; }
                    if (start <= state.LastObservedStart) continue;
                    state.LastObservedStart = start;
                    RegionGameStarted(state, process.Id, start);
                }
            }
        }
    }
}
