using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SRT.Core;

namespace SRT;

// Definições
public partial class MainWindow
{
    void ApplySettingsToUi()
    {
        _uiLock = true;
        try
        {
            ModeServers.IsChecked = _s.Mode != "all" && !_s.GameMode;
            ModeGame.IsChecked = _s.Mode != "all" && _s.GameMode;
            ModeAll.IsChecked = _s.Mode == "all";
            DefaultPortBox.Text = _s.DefaultPort.ToString();
            AutoQueryBox.IsChecked = _s.AutoQuery;
            MethodCombo.SelectedIndex = _s.ChangeMethod == "airplane" ? 1 : 0;
            OffSecondsBox.Text = _s.OffSeconds.ToString();
            AttemptsBox.Text = _s.MaxAttempts.ToString();
            KeepOffBox.IsChecked = _s.KeepPcOffPhone;
            LockCs2Box.IsChecked = _s.LockCs2;
            SteamEgressBox.IsChecked = _s.SteamOnEgress;
            ApplyDevSettings();
            CleanupBox.IsChecked = _s.CleanupOnExit;
            ApiKeyBox.Text = _s.SteamApiKey ?? "";
            OptCs2.IsChecked = _s.LaunchCs2;
            OptSilent.IsChecked = _s.SteamSilent;
            OptOffline.IsChecked = _s.SteamOffline;
            OptStreamer.IsChecked = _s.StreamerMode;
            AccSort.SelectedItem = AccSort.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.AccountSort) ?? AccSort.Items[0];
        }
        finally { _uiLock = false; }
    }

    void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        _s.ChangeMethod = (MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "data";
        if (int.TryParse(OffSecondsBox.Text, out var off)) _s.OffSeconds = Math.Clamp(off, 1, 60);
        if (int.TryParse(AttemptsBox.Text, out var att)) _s.MaxAttempts = Math.Clamp(att, 1, 10);
        _s.KeepPcOffPhone = KeepOffBox.IsChecked == true;
        var guardChanged = _s.LockCs2 != (LockCs2Box.IsChecked == true) || _s.SteamOnEgress != (SteamEgressBox.IsChecked == true);
        _s.LockCs2 = LockCs2Box.IsChecked == true;
        _s.SteamOnEgress = SteamEgressBox.IsChecked == true;
        ReadDevSettings();
        if (guardChanged) _ = SyncNow(force: true);
        _s.CleanupOnExit = CleanupBox.IsChecked == true;
        _s.SteamApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Text) ? null : ApiKeyBox.Text.Trim();
        OffSecondsBox.Text = _s.OffSeconds.ToString();
        AttemptsBox.Text = _s.MaxAttempts.ToString();
        SaveSettings();
    }

    void SaveSettings() { if (!_demo) Store.Save(Paths.File("settings.json"), _s); }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask("Restaurar a rede", "Isto tira o bloqueio da Firewall, as rotas e o modo «Todo o PC». Os servidores da lista passam a ver o IP de casa até voltares a abrir a app. A lista fica guardada.", "Restaurar", danger: true)) return;
        if (_demo) return;
        _tick.Stop();
        await _guard.RestoreAll(_servers.Select(s => s.Ip).Distinct().ToList());
        Toast("Rede restaurada. A proteção volta quando reabrires a app ou carregares em Testar fugas.", true);
        foreach (var s in _servers) { s.Protected = false; s.RouteText = "Desligado"; }
        SetHero("bad", "Proteção desligada", "Restauraste a rede. Carrega em Testar fugas para voltar a proteger.");
    }

    void OpenFirewall_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("wf.msc") { UseShellExecute = true }); }
        catch (Exception ex) { Toast("Não consegui abrir a Firewall: " + ex.Message, false); }
        Toast("Em Regras de Saída procura 'ServerRouteTool v9 - kill switch'.", true);
    }

    void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(Paths.Data) { UseShellExecute = true });
    void ApiKeyLink_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true });
}
