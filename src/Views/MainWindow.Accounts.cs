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

// Contas Steam
public partial class MainWindow
{
    void LoadAccounts()
    {
        _accMeta = Store.Load(Paths.File("accounts.json"), () => new Dictionary<string, AccountMeta>());
        _accounts.Clear();
        List<LocalAccount> locals;
        try { locals = Steam.ReadAccounts(); }
        catch (Exception ex) { Log.Warn("Não consegui ler as contas do Steam: " + ex.Message); locals = new(); }
        var active = Steam.ActiveSteamId();
        foreach (var l in locals)
        {
            if (!_accMeta.TryGetValue(l.SteamId, out var meta)) _accMeta[l.SteamId] = meta = new AccountMeta();
            if (meta.Hidden) continue;
            _accounts.Add(new AccountItem(l, meta) { Active = l.SteamId == active, Streamer = _s.StreamerMode });
        }
        AccountsEmptyText.Text = Steam.InstallDir() == null
            ? "O Steam não está instalado neste PC."
            : "Não encontrei contas guardadas.\nEntra no Steam com \"Lembrar-me\" marcado, ou usa Adicionar conta.";
        ApplyAccountFilter();
        Log.Info($"{_accounts.Count} conta(s) Steam neste PC.");
    }

    void SaveAccounts() { if (!_demo) Store.Save(Paths.File("accounts.json"), _accMeta); }

    void AccFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiLock || !IsLoaded) return;
        if (AccSort.SelectedItem is ComboBoxItem it && (string)it.Tag != _s.AccountSort) { _s.AccountSort = (string)it.Tag; SaveSettings(); }
        ApplyAccountFilter();
    }

    // Cartões em grelha: tantas colunas quantas cabem (mín. 250 px cada), todas da mesma largura.
    System.Windows.Controls.Primitives.UniformGrid _accGrid;

    void AccGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _accGrid = (System.Windows.Controls.Primitives.UniformGrid)sender;
        FitAccountColumns();
    }

    void AccountsPanel_SizeChanged(object sender, SizeChangedEventArgs e) => FitAccountColumns();

    void FitAccountColumns()
    {
        if (_accGrid == null || AccountsPanel.ActualWidth <= 0) return;
        _accGrid.Columns = Math.Max(1, (int)((AccountsPanel.ActualWidth + 10) / 260));
    }

    void ApplyAccountFilter()
    {
        var q = AccSearch.Text.Trim();
        IEnumerable<AccountItem> list = _accounts;
        if (q.Length > 0)
            list = list.Where(a => (a.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Login ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (a.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (a.Tags ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || a.SteamId.Contains(q));
        if (FFav.IsChecked == true) list = list.Where(a => a.Favorite);
        if (FClean.IsChecked == true) list = list.Where(a => a.Clean);
        if (FBan.IsChecked == true) list = list.Where(a => a.HasVac || a.HasGameBan || a.HasTradeBan || a.HasCommunityBan);
        if (FCool.IsChecked == true) list = list.Where(a => a.InCooldown);
        if (FOut.IsChecked == true) list = list.Where(a => a.Excluded);

        var ordered = list.OrderByDescending(a => a.Favorite);
        ordered = _s.AccountSort switch
        {
            "name" => ordered.ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            "clean" => ordered.ThenByDescending(a => a.Clean).ThenBy(a => a.InCooldown).ThenBy(a => a.Name),
            "hours" => ordered.ThenByDescending(a => a.Meta.Profile?.Cs2Hours ?? -1),
            _ => ordered.ThenByDescending(a => a.Meta.LastUsed ?? DateTimeOffset.FromUnixTimeSeconds(a.Local.Timestamp).LocalDateTime),
        };
        _accShown.Clear();
        foreach (var a in ordered) _accShown.Add(a);
        var bans = _accounts.Count(a => a.HasVac || a.HasGameBan);
        AccCount.Text = _accounts.Count == 0 ? "" : $"{_accShown.Count} de {_accounts.Count}" + (bans > 0 ? $" · {bans} com ban" : "");
        AccountsEmpty.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuickInfo();
    }

    // ------------------------------------------------------------ Troca rápida

    /// <summary>A próxima conta da rotação: fora da lista negra, sem cooldown nem ban, a menos usada há mais tempo.</summary>
    AccountItem NextQuick()
    {
        var active = _accounts.FirstOrDefault(a => a.Active)?.SteamId;
        return _accounts.Where(a => a.Usable && a.SteamId != active)
            .OrderBy(a => a.Meta.LastUsed ?? DateTime.MinValue)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    void UpdateQuickInfo()
    {
        var next = NextQuick();
        var pool = _accounts.Count(a => a.Usable);
        SideQuickLabel.Text = next == null ? "Troca rápida" : "Trocar para";
        SideQuickNext.Text = next?.Name ?? "";
        SideQuick.IsEnabled = QuickBtn.IsEnabled = next != null;
        if (next == null) SideQuick.ToolTip = QuickBtn.ToolTip = "Sem contas para a troca rápida: adiciona contas ou tira-as do cooldown.";
        var tip = next == null
            ? "Nenhuma conta disponível: estão todas fora da troca, em cooldown ou com ban."
            : $"Fecha o CS2, entra em {next.Name} e volta a abrir o CS2.\n{pool} conta(s) na rotação (sem lista negra, cooldown nem ban).\nAtalho em qualquer lado, mesmo dentro do jogo: Ctrl+Alt+Q.";
        QuickBtn.ToolTip = tip;
        SideQuick.ToolTip = tip;
    }

    async void Quick_Click(object sender, RoutedEventArgs e) => await QuickSwitch();

    async Task QuickSwitch()
    {
        var next = NextQuick();
        var skipped = _accounts.Where(a => !a.Usable).Select(a =>
            $"{a.Name} ({(a.Excluded ? "lista negra" : a.InCooldown ? "cooldown" : "ban")})").ToList();
        if (skipped.Count > 0) Log.Info("Troca rápida saltou: " + string.Join(", ", skipped));
        if (next == null) { Toast("Sem contas para a troca rápida: estão todas na lista negra, em cooldown ou com ban.", false); return; }
        if (next.Meta.ExcludeQuick) { Log.Error("Troca rápida escolheu uma conta da lista negra; cancelado."); return; }
        Log.Info($"Troca rápida: {next.Name}.");
        // Pedido explícito: fecha o CS2 sem perguntar.
        await SwitchTo(next, cs2: true, ask: false);
    }

    void AccToggleQuick_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Meta.ExcludeQuick = !a.Meta.ExcludeQuick;
        a.RefreshAll();
        SaveAccounts();
        ApplyAccountFilter();
        Toast(a.Excluded ? $"{a.Name} saiu da troca rápida." : $"{a.Name} voltou à troca rápida.", true);
    }

    static AccountItem Acc(object sender) => (sender as FrameworkElement)?.DataContext as AccountItem;

    async void AccSwitch_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await SwitchTo(a, OptCs2.IsChecked == true); }
    async void AccSwitchCs2_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await SwitchTo(a, true); }

    async Task SwitchTo(AccountItem a, bool cs2, bool ask = true)
    {
        if (_switching) return;
        if (_demo)
        {
            foreach (var x in _accounts) x.Active = x == a;
            a.Meta.LastUsed = DateTime.Now;
            UpdateQuickInfo();
            Toast($"Demonstração: a entrar em {a.Name}.", true);
            return;
        }
        if (Steam.Cs2Running)
        {
            if (ask && !await Ask("O CS2 está aberto", $"Para entrar em {a.Name} tenho de fechar o CS2 e o Steam. Continuar?", "Fechar e trocar")) return;
            foreach (var p in Process.GetProcessesByName("cs2")) try { p.Kill(true); } catch { }
            for (var i = 0; i < 20 && Steam.Cs2Running; i++) await Task.Delay(150);
        }
        _switching = true;
        a.Busy = true;
        Toast($"A entrar em {a.Name}…", true);
        try
        {
            // Sem o CS2 trancado à saída, só se abre o jogo com a proteção confirmada.
            if (cs2 && !_s.LockCs2 && (_last?.Egress == null || _distrust))
            {
                cs2 = false;
                Toast("Troquei de conta mas não abri o CS2: a proteção não está ativa e o CS2 não está trancado.", false);
            }
            var args = string.Join(" ", new[] { _s.SteamSilent ? "-silent" : null, cs2 ? "-applaunch 730 -condebug" : null }.Where(x => x != null));
            await Steam.SwitchTo(a.Login, a.SteamId, _s.SteamOffline, args);
            a.Meta.LastUsed = DateTime.Now;
            SaveAccounts();
            Log.Ok($"Conta trocada para {a.Name}" + (cs2 ? " (CS2 a abrir)." : "."));
            Diag.Write("conta", new { steamId = a.SteamId, cs2, offline = _s.SteamOffline });
            foreach (var x in _accounts) x.Active = x == a;
            UpdateQuickInfo();
            _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                var active = Steam.ActiveSteamId();
                if (active != null) foreach (var x in _accounts) x.Active = x.SteamId == active;
            }));
        }
        catch (Exception ex) { Log.Error("Troca de conta: " + ex.Message); Toast(ex.Message, false); }
        finally { a.Busy = false; _switching = false; UpdateQuickInfo(); }
    }

    async void AccAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (!await Ask("Adicionar conta", "O Steam vai fechar e abrir no ecrã de login. Entra com a conta nova e marca \"Lembrar-me\". Depois volta aqui e ela aparece na lista.", "Abrir login")) return;
        try { await Steam.AddNew(); WaitForNewAccount(); }
        catch (Exception ex) { Toast(ex.Message, false); }
    }

    async void WaitForNewAccount()
    {
        var before = _accounts.Count + _accMeta.Values.Count(m => m.Hidden);
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(5000);
            if (Steam.ReadAccounts().Count > before) { LoadAccounts(); Toast("Conta nova adicionada.", true); return; }
        }
    }

    async void AccRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAccounts(_accounts.ToList());

    async void AccRefreshOne_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) await RefreshAccounts(new() { a }); }

    async Task RefreshAccounts(List<AccountItem> list)
    {
        if (_demo || _refreshingAcc || list.Count == 0) return;
        _refreshingAcc = true;
        AccRefreshBtn.IsEnabled = false;
        var errors = 0;
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                AccRefreshText.Text = $"A verificar {i + 1}/{list.Count}…";
                try
                {
                    var oldUrl = a.Meta.Profile?.AvatarUrl;
                    var p = await Steam.FetchProfile(a.SteamId, _s.SteamApiKey);
                    a.Meta.Profile = p;
                    if (p.AvatarUrl != null && (p.AvatarUrl != oldUrl || a.Meta.AvatarPath == null || !File.Exists(a.Meta.AvatarPath)))
                    {
                        try { a.Meta.AvatarPath = await Steam.DownloadAvatar(a.SteamId, p.AvatarUrl); } catch { }
                    }
                    a.LoadAvatar();
                    a.RefreshAll();
                    if (p.VacBans > 0 || p.GameBans > 0) Log.Warn($"{a.Name}: {p.VacBans} VAC, {p.GameBans} game ban(s).");
                }
                catch (Exception ex) { errors++; Log.Warn($"{a.Name}: {ex.Message}"); }
                if (i % 5 == 4) SaveAccounts();
                if (i < list.Count - 1) await Task.Delay(1100);
            }
            SaveAccounts();
            ApplyAccountFilter();
            Toast(errors == 0 ? $"{list.Count} conta(s) atualizadas." : $"Atualizadas, com {errors} erro(s). A Steam pode estar a limitar pedidos: tenta daqui a um minuto.", errors == 0);
        }
        finally
        {
            _refreshingAcc = false;
            AccRefreshBtn.IsEnabled = true;
            AccRefreshText.Text = "Atualizar estado";
        }
    }

    void AccProfile_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://steamcommunity.com/profiles/{0}");
    void AccLeetify_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://leetify.com/app/profile/{0}");
    void AccCsstats_Click(object sender, RoutedEventArgs e) => OpenUrl(Acc(sender), "https://csstats.gg/player/{0}");

    static void OpenUrl(AccountItem a, string fmt)
    {
        if (a == null) return;
        try { Process.Start(new ProcessStartInfo(string.Format(fmt, a.SteamId)) { UseShellExecute = true }); } catch { }
    }

    void AccCopyId_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) { Clipboard.SetText(a.SteamId); Toast("SteamID64 copiado.", true); } }
    void AccCopyLogin_Click(object sender, RoutedEventArgs e) { if (Acc(sender) is AccountItem a) { Clipboard.SetText(a.Login ?? ""); Toast("Login copiado.", true); } }

    async void AccCooldown_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a || sender is not MenuItem mi) return;
        var minutes = int.Parse((string)mi.CommandParameter);
        if (minutes < 0)
        {
            DialogInput.Text = "";
            DialogInput.Tag = "Ex.: 21h, 3d, 90m, 2d 12h";
            if (!await Ask($"Cooldown de {a.Name}", "Quanto tempo falta? Usa d (dias), h (horas) e m (minutos).", "Marcar", "Cancelar", inputs: 1, label1: "Tempo que falta")) return;
            var t = Fmt.ParseDuration(DialogInput.Text);
            if (t == null) { Toast("Não percebi a duração. Exemplo: 3d 4h", false); return; }
            minutes = (int)Math.Ceiling(t.Value.TotalMinutes);
        }
        a.Meta.CooldownUntil = minutes == 0 ? null : DateTime.Now.AddMinutes(minutes);
        a.RefreshAll();
        SaveAccounts();
        ApplyAccountFilter();
        Toast(minutes == 0 ? $"Cooldown de {a.Name} retirado." : $"{a.Name}: cooldown até {a.Meta.CooldownUntil:dd/MM HH:mm}.", true);
    }

    void AccFav_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Favorite = !a.Favorite;
        SaveAccounts();
        ApplyAccountFilter();
    }

    async void AccNote_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        DialogInput.Text = a.Meta.Note ?? "";
        DialogInput.Tag = "Ex.: conta principal, premier 15k";
        DialogInput2.Text = a.Meta.Tags ?? "";
        DialogInput2.Tag = "Ex.: main, faceit";
        if (!await Ask($"Nota de {a.Name}", "Só fica neste PC. A pesquisa também procura aqui.", "Guardar", "Cancelar", inputs: 2, label1: "Nota", label2: "Etiquetas (separadas por vírgulas)")) return;
        a.Meta.Note = string.IsNullOrWhiteSpace(DialogInput.Text) ? null : DialogInput.Text.Trim();
        a.Meta.Tags = string.IsNullOrWhiteSpace(DialogInput2.Text) ? null : DialogInput2.Text.Trim();
        a.RefreshAll();
        SaveAccounts();
    }

    void AccHide_Click(object sender, RoutedEventArgs e)
    {
        if (Acc(sender) is not AccountItem a) return;
        a.Meta.Hidden = true;
        _accounts.Remove(a);
        SaveAccounts();
        ApplyAccountFilter();
        Toast($"{a.Name} escondida. Volta em Definições > Mostrar contas escondidas.", true);
    }

    void UnhideAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var m in _accMeta.Values) m.Hidden = false;
        SaveAccounts();
        LoadAccounts();
        Toast("Todas as contas voltaram a aparecer.", true);
    }

    void AccOpt_Click(object sender, RoutedEventArgs e)
    {
        _s.LaunchCs2 = OptCs2.IsChecked == true;
        _s.SteamSilent = OptSilent.IsChecked == true;
        _s.SteamOffline = OptOffline.IsChecked == true;
        _s.StreamerMode = OptStreamer.IsChecked == true;
        foreach (var a in _accounts) a.Streamer = _s.StreamerMode;
        SaveSettings();
    }
}
