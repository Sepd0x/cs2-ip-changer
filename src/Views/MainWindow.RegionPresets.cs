using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SRT.Core;

namespace SRT;

public partial class MainWindow
{
    readonly DateTime _regionPresetWatchSince = DateTime.Now;
    bool _updatingRegionPresets;

    void RefreshRegionPresets(Guid? selected = null)
    {
        _updatingRegionPresets = true;
        try
        {
            var settings = _regionGame.Settings;
            var presets = (settings.RegionPresets ?? new()).Where(p => p != null).ToList();
            AutomationProperties.SetName(RegionPresetPicker, Loc.T(Texts.RegionPresets));
            RegionPresetPicker.ItemsSource = presets;
            RegionPresetPicker.SelectedValue = selected;
            if (RegionPresetPicker.SelectedIndex < 0 && presets.Count > 0) RegionPresetPicker.SelectedIndex = 0;
            RegionPresetStartup.ItemsSource = presets.Prepend(new RegionPreset(Guid.Empty, Loc.T("No preset"), null, null)).ToList();
            RegionPresetStartup.SelectedValue = RegionPresets.OnStart(presets, settings.RegionPresetOnStart)?.Id ?? Guid.Empty;
            RegionPresetStartupLabel.Content = _regionGame == _cs2Regions ? Loc.T(Texts.RegionPresetStart) : Loc.F("Apply when {0} starts", _regionGame.Game.Name);
            RegionPresetStartupHint.Text = _regionGame == _cs2Regions ? Loc.T(Texts.RegionPresetStartHint)
                : Loc.T("Keep the app open. If the game still uses the previous regions, restart it between matches.");
        }
        finally { _updatingRegionPresets = false; }
        UpdateRegionPresetButtons();
    }

    void UpdateRegionPresetButtons()
    {
        if (RegionPresetApply == null || RegionPresetSave == null || RegionPresetRename == null || RegionPresetRemove == null) return;
        var selected = RegionPresetPicker.SelectedItem is RegionPreset;
        var hasRegions = _regionInfos is { Count: > 0 };
        RegionPresetApply.IsEnabled = selected && hasRegions;
        RegionPresetSave.IsEnabled = hasRegions;
        RegionPresetRename.IsEnabled = RegionPresetRemove.IsEnabled = selected;
    }

    void RegionPresetPicker_Changed(object sender, SelectionChangedEventArgs e) => UpdateRegionPresetButtons();

    void RegionPresetStartup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRegionPresets || _regionGame == null || RegionPresetStartup.SelectedValue is not Guid id) return;
        _regionGame.LastObservedStart = _regionGame.PresetSetAt = DateTime.Now;
        _regionGame.PendingPreset = null;
        _regionGame.Settings.RegionPresetOnStart = id == Guid.Empty ? null : id;
        SaveSettings();
    }

    async void RegionPresetSave_Click(object sender, RoutedEventArgs e) => await EditRegionPreset(null);

    async void RegionPresetRename_Click(object sender, RoutedEventArgs e)
    {
        if (RegionPresetPicker.SelectedItem is RegionPreset preset) await EditRegionPreset(preset);
    }

    async Task EditRegionPreset(RegionPreset preset)
    {
        var state = _regionGame;
        var settings = state.Settings;
        var id = preset?.Id ?? Guid.NewGuid();
        DialogInput.Text = preset?.Name ?? "";
        DialogInput.Tag = "";
        var title = preset == null ? Texts.RegionPresetSave : Texts.RegionPresetRename;
        if (!await Ask(title, Loc.F("Enter a name of up to {0} characters.", RegionPresets.NameLimit), Loc.T("Save"), inputs: 1,
            label1: Loc.T("Preset name"))) return;

        string error;
        var updated = preset == null
            ? RegionPresets.Save(settings.RegionPresets, id, DialogInput.Text, settings.BlockedRegions, settings.RegionsKeep, out error)
            : RegionPresets.Rename(settings.RegionPresets, id, DialogInput.Text, out error);
        if (error != null) { Toast(Loc.T(error), false); return; }
        settings.RegionPresets = updated;
        SaveSettings();
        if (state == _regionGame) RefreshRegionPresets(id);
    }

    async void RegionPresetRemove_Click(object sender, RoutedEventArgs e)
    {
        var state = _regionGame;
        var settings = state.Settings;
        if (RegionPresetPicker.SelectedItem is not RegionPreset preset ||
            !await Ask(Texts.RegionPresetRemove, Loc.F("Remove the saved preset “{0}”?", preset.Name), Loc.T("Remove"), danger: true)) return;
        settings.RegionPresets = RegionPresets.Remove(settings.RegionPresets, preset.Id);
        if (settings.RegionPresetOnStart == preset.Id)
        {
            settings.RegionPresetOnStart = null;
            state.PendingPreset = null;
        }
        SaveSettings();
        if (state == _regionGame) RefreshRegionPresets();
    }

    async void RegionPresetApply_Click(object sender, RoutedEventArgs e)
    {
        if (RegionPresetPicker.SelectedItem is RegionPreset preset)
        {
            _regionGame.PendingPreset = null;
            await ApplyRegionPreset(_regionGame, preset, onStart: false);
        }
    }

    async Task<bool> ApplyRegionPreset(RegionGameState state, RegionPreset preset, bool onStart)
    {
        if (RegionPresets.Resolve(preset, state.Infos) is not { } selection)
        {
            if (state == _regionGame)
                Toast(Loc.T("This preset no longer leaves a region available. Choose a different preset."), false);
            else Log.Warn($"Regions: skipped the startup preset for {state.Game.Name}, since no region would be left available.");
            return false;
        }
        await SetBlocked(selection.Blocked, keep: selection.Keep, onStart: onStart, state: state);
        return true;
    }

    async void RegionGameStarted(RegionGameState state, int pid, DateTime startedAt)
    {
        var settings = state.Settings;
        if (_restored || startedAt < _regionPresetWatchSince || startedAt < state.PresetSetAt ||
            RegionPresets.OnStart(settings.RegionPresets, settings.RegionPresetOnStart) is not { } preset) return;
        state.PendingPreset = (pid, startedAt, preset.Id);
        if (!_regionRulesReady || state.Loading) return;
        if (state.Infos == null) await LoadRegions(atStart: false, quiet: true, state: state);
        else await ApplyPendingRegionPreset(state);
    }

    async Task<bool> ApplyPendingRegionPreset(RegionGameState state)
    {
        if (state.PendingPreset is not { } pending) return false;
        state.PendingPreset = null;
        var settings = state.Settings;
        if (_restored || pending.Id != settings.RegionPresetOnStart ||
            RegionPresets.OnStart(settings.RegionPresets, pending.Id) is not { } preset) return false;
        try
        {
            using var process = Process.GetProcessById(pending.Pid);
            if (process.StartTime != pending.Start || process.HasExited) return false;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
        return await ApplyRegionPreset(state, preset, onStart: true);
    }
}
