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

// Servidores
public partial class MainWindow
{
    void LoadServers()
    {
        var list = _demo ? null : Store.Load<List<ServerItem>>(Paths.File("servers.json"), () => null);
        if (list == null && !_demo)
        {
            // Migração: a lista da v8 (servers.json ao lado do .exe) passa para SRT_Dados.
            var old = Path.Combine(Paths.AppDir, "servers.json");
            list = File.Exists(old) ? Store.Load<List<ServerItem>>(old, () => null) : null;
        }
        if (list == null)
        {
            // Primeira vez: a lista começa vazia (cola-se em Servidores).
            var defaults = new List<(string Ip, int Port)>();
            AddDefaultServers(defaults);
            list = defaults.Select(d => new ServerItem { Ip = d.Ip, Port = d.Port }).ToList();
        }
        foreach (var s in list.Where(s => ValidIp(s.Ip) && s.Port is >= 1 and <= 65535).GroupBy(s => s.Key).Select(g => g.First()))
        {
            s.PlayersText = "—"; s.PingText = "—"; s.Map = "—";
            _servers.Add(s);
        }
        if (!_demo) SaveServers();
    }

    void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RemoveBtn.IsEnabled = ServerList.SelectedItems.Count > 0;

    void SaveServers() { if (!_demo) Store.Save(Paths.File("servers.json"), _servers.ToList()); }

    static bool ValidIp(string ip)
    {
        if (ip == null) return false;
        var p = ip.Split('.');
        if (p.Length != 4 || !p.All(x => int.TryParse(x, out var n) && n is >= 0 and <= 255)) return false;
        var first = int.Parse(p[0]);
        return first != 0 && first != 127 && first < 224;
    }

    int ParseInto(string text, out List<string> ignored)
    {
        ignored = new();
        var port = int.TryParse(DefaultPortBox.Text.Trim(), out var dp) && dp is >= 1 and <= 65535 ? dp : 27015;
        var added = 0;
        foreach (Match m in ServerRegex.Matches(text ?? ""))
        {
            var ip = string.Join(".", m.Groups[1].Value.Split('.').Select(o => int.TryParse(o, out var n) ? n.ToString() : o));
            if (!ValidIp(ip)) { ignored.Add(m.Value.Trim()); continue; }
            var p = port;
            if (m.Groups[2].Success && (!int.TryParse(m.Groups[2].Value, out p) || p < 1 || p > 65535)) { ignored.Add(m.Value.Trim()); continue; }
            if (_servers.Any(s => s.Ip == ip && s.Port == p)) continue;
            _servers.Add(new ServerItem { Ip = ip, Port = p, PlayersText = "—", PingText = "—", Map = "—" });
            added++;
        }
        return added;
    }

    async Task AddFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Toast("Nada para adicionar.", false); return; }
        var n = ParseInto(text, out var ignored);
        SaveServers();
        ShowLastServer();
        if (n == 0 && ignored.Count == 0) { Toast("Esses servidores já estão na lista.", false); return; }
        Log.Info($"Adicionados {n} servidor(es)." + (ignored.Count > 0 ? " Ignorados: " + string.Join(", ", ignored) : ""));
        Toast(n > 0 ? $"{n} servidor(es) adicionados e protegidos." + (ignored.Count > 0 ? $" {ignored.Count} ignorados." : "") : "Nenhum IP:porta válido.", n > 0);
        _s.DefaultPort = int.TryParse(DefaultPortBox.Text, out var dp) ? dp : 27015;
        SaveSettings();
        await SyncNow(force: true);
        await QueryServers(true);
    }

    async void AddServers_Click(object sender, RoutedEventArgs e) { var t = BulkInput.Text; BulkInput.Clear(); await AddFromText(t); }

    async void PasteServers_Click(object sender, RoutedEventArgs e)
    {
        string t = null;
        try { t = Clipboard.GetText(); } catch { }
        await AddFromText(t);
    }

    async void ImportServers_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Listas (*.txt;*.csv;*.cfg;*.log)|*.txt;*.csv;*.cfg;*.log|Todos (*.*)|*.*", Title = "Escolhe uma lista de servidores" };
        if (dlg.ShowDialog(this) == true) await AddFromText(await File.ReadAllTextAsync(dlg.FileName));
    }

    void ImportStartupFile()
    {
        var f = Path.Combine(Paths.AppDir, "importar.txt");
        if (!File.Exists(f)) return;
        _ = AddFromText(File.ReadAllText(f));
        try { File.Move(f, Path.Combine(Paths.Data, $"importar_feito_{DateTime.Now:yyyyMMdd_HHmmss}.txt")); } catch { }
    }

    async void BulkInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            var t = BulkInput.Text; BulkInput.Clear();
            await AddFromText(t);
        }
    }

    void ServerSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = ServerSearch.Text.Trim();
        _serverView.Filter = string.IsNullOrEmpty(q) ? null : o =>
        {
            var s = (ServerItem)o;
            return s.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || (s.ServerName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (s.Map ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (s.Label ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        };
    }

    List<ServerItem> SelectedOrAll()
    {
        var sel = ServerList.SelectedItems.Cast<ServerItem>().ToList();
        return sel.Count > 0 ? sel : _serverView.Cast<ServerItem>().ToList();
    }

    async void QueryServers_Click(object sender, RoutedEventArgs e) => await QueryServers(false);

    async Task QueryServers(bool quiet)
    {
        if (_demo || _querying) return;
        var egress = _last?.Egress;
        if (egress == null) { if (!quiet) Toast("Telemóvel desligado: não consulto servidores pela rede de casa.", false); return; }
        _querying = true;
        try
        {
            var targets = quiet ? _servers.ToList() : SelectedOrAll();
            var gate = new SemaphoreSlim(8);
            var ok = 0;
            await Task.WhenAll(targets.Select(async srv =>
            {
                if (_last?.RouteOk.TryGetValue(srv.Ip, out var routed) != true || !routed) { srv.PingText = "—"; return; }
                await gate.WaitAsync();
                try
                {
                    var info = await A2S.Query(srv.Ip, srv.Port, egress.LocalIp);
                    srv.ServerName = info.Name;
                    srv.Map = info.Map;
                    srv.PlayerCount = info.Players;
                    srv.MaxPlayers = info.MaxPlayers;
                    srv.PlayersText = info.Bots > 0 ? $"{info.Players}/{info.MaxPlayers} ({info.Bots} bots)" : $"{info.Players}/{info.MaxPlayers}";
                    srv.PingMs = info.PingMs;
                    srv.PingText = $"{info.PingMs} ms";
                    srv.PlayerNames = info.PlayerNames;
                    Interlocked.Increment(ref ok);
                }
                catch
                {
                    srv.PingMs = -1;
                    srv.PingText = "—";
                    srv.PlayersText = "—";
                }
                finally { gate.Release(); }
            }));
            if (!quiet) Toast($"{ok} de {targets.Count} servidor(es) responderam.", ok > 0);
        }
        finally { _querying = false; }
    }

    void AutoQuery_Click(object sender, RoutedEventArgs e) { _s.AutoQuery = AutoQueryBox.IsChecked == true; SaveSettings(); }

    void CopyServers_Click(object sender, RoutedEventArgs e)
    {
        var list = SelectedOrAll();
        if (list.Count == 0) return;
        Clipboard.SetText(string.Join("\r\n", list.Select(s => s.Key)));
        Toast($"{list.Count} servidor(es) copiados.", true);
    }

    async void RemoveServers_Click(object sender, RoutedEventArgs e)
    {
        var sel = ServerList.SelectedItems.Cast<ServerItem>().ToList();
        if (sel.Count == 0) { Toast("Seleciona primeiro os servidores a remover.", false); return; }
        foreach (var s in sel) _servers.Remove(s);
        SaveServers();
        var still = _servers.Select(s => s.Ip).ToHashSet();
        var gone = sel.Select(s => s.Ip).Where(ip => !still.Contains(ip)).Distinct().ToList();
        // Primeiro o kill switch deixa de cobrir esses IPs só depois de as rotas saírem (sem janela de fuga para os que ficam).
        if (!_demo && gone.Count > 0) await _guard.RemoveIps(gone, _s);
        Log.Info($"Removidos {sel.Count} servidor(es).");
        await SyncNow(force: true);
        UpdateStatusUi();
    }

    void SrvFav_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerItem s) return;
        s.Favorite = !s.Favorite;
        _serverView.Refresh();
        SaveServers();
    }

    async void SrvConnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServerItem s) await Connect(s);
    }

    async void ServerList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ServerItem s) await Connect(s);
    }

    async Task Connect(ServerItem s)
    {
        if (!_demo)
        {
            await SyncNow(force: true);
            if (_distrust || _cDiff.Ok == false) { Toast("Não abro: o IP do telemóvel é igual ao de casa.", false); return; }
            var fullOk = _last?.Egress != null && _last.EffectiveMode == "all" && _last.FullModeOk;
            if (s.Protected != true && !fullOk)
            {
                Toast($"Não abro o {s.Key}: a proteção não está confirmada ({s.RouteText}).", false);
                return;
            }
            // As duas causas conhecidas do kick "No Steam logon": o Steam em modo offline, ou noutra rede que o CS2.
            if (Steam.WantsOffline(Steam.ActiveSteamId()) && !await Ask("O Steam está em modo offline",
                    "Em modo offline o Steam não confirma a tua conta e o servidor dá kick com \"No Steam logon\" passados uns minutos.\n\n" +
                    "Desliga \"Offline\" em Contas Steam e volta a entrar na conta.", "Entrar mesmo assim"))
                return;
            if (!await WaitAfterCrash(s)) return;
            if (!SteamReady && !await WaitSteamReady(60) && !await Ask("O Steam ainda não tem sessão",
                    "O Steam ainda não voltou a ligar-se à Valve (por exemplo, depois de trocares o IP). Se entrares agora, o servidor dá kick com \"No Steam logon\" passados uns minutos.",
                    "Entrar mesmo assim"))
                return;
            if (_s.SteamOnEgress && _last?.SteamOnEgress == false && await Ask("O Steam está noutra rede",
                    "O CS2 vai pelo telemóvel, mas o Steam está ligado pela rede de casa. Quando o Steam se religar, o servidor dá kick com \"No Steam logon\".\n\n" +
                    $"Reinicio o Steam pelo telemóvel e entro no {s.Title}?" + (Steam.Cs2Running ? " O CS2 fecha e volta a abrir." : ""),
                    "Reiniciar e entrar", "Entrar assim"))
            {
                await RestartSteamAndConnect(s);
                return;
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo($"steam://connect/{s.Key}") { UseShellExecute = true });
            Diag.Write("entrar", new { servidor = s.Key, nome = s.Title, protegido = s.Protected, modo = _last?.EffectiveMode, steamNaSaida = _last?.SteamOnEgress, steamIps = _last?.Steam.LocalIps });
            Log.Ok($"A entrar em {s.Key} pelo telemóvel.");
            Toast($"A abrir {s.Title} no Steam…", true);
            MarkLast(s);
        }
        catch (Exception ex) { Toast("Não consegui abrir o Steam: " + ex.Message, false); }
    }

    async Task RestartSteamAndConnect(ServerItem s)
    {
        ShowProgress("A reiniciar o Steam pelo telemóvel…");
        try
        {
            foreach (var p in Process.GetProcessesByName("cs2")) try { p.Kill(true); } catch { }
            for (var i = 0; i < 20 && Steam.Cs2Running; i++) await Task.Delay(150);
            // As rotas da Valve já estão na saída: o Steam novo liga-se por lá, e o CS2 abre e entra no servidor.
            await Steam.Restart((_s.SteamSilent ? "-silent " : "") + $"-applaunch 730 -condebug +connect {s.Key}");
            Diag.Write("steam-reiniciado", new { servidor = s.Key });
            Log.Ok($"Steam reiniciado pelo telemóvel; o CS2 abre e entra em {s.Key}.");
            Toast($"Steam a reiniciar. O CS2 abre e entra em {s.Title}.", true);
            MarkLast(s);
        }
        catch (Exception ex) { Log.Error("Reiniciar o Steam: " + ex.Message); Toast(ex.Message, false); }
        finally { HideProgress(); }
    }

    /// <summary>Guarda o servidor como o último (na app e na conta com sessão iniciada) e pinta-o na lista.</summary>
    void MarkLast(ServerItem s)
    {
        _s.LastServer = s.Key;
        _s.LastServerAt = DateTime.Now;
        SaveSettings();
        ShowLastServer();
        var activeId = _demo ? null : Steam.ActiveSteamId();
        var acc = _accounts.FirstOrDefault(a => a.SteamId == activeId) ?? _accounts.FirstOrDefault(a => a.Active);
        if (acc != null)
        {
            acc.Meta.LastServer = s.Key;
            acc.Meta.LastServerName = s.ServerName ?? s.Label;
            acc.Meta.LastServerAt = _s.LastServerAt;
            acc.RefreshAll();
            SaveAccounts();
        }
    }

    void ShowLastServer()
    {
        // Versões antigas só guardavam o último servidor em cada conta: usa-se o mais recente de todas.
        if (_s.LastServer == null && _accounts.Select(a => a.Meta).Where(m => m.LastServer != null).MaxBy(m => m.LastServerAt) is AccountMeta m)
        {
            _s.LastServer = m.LastServer;
            _s.LastServerAt = m.LastServerAt;
        }
        foreach (var x in _servers)
        {
            x.IsLast = x.Key == _s.LastServer;
            x.LastAt = x.IsLast ? _s.LastServerAt : null;
        }
        _serverView.Refresh();
    }
}
