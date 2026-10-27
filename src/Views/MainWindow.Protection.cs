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

// Proteção
public partial class MainWindow
{
    async Task SyncNow(bool force = false)
    {
        if (_demo) return;
        if (_syncing) { _syncAgain = true; return; }
        _syncing = true;
        try
        {
            var ips = _servers.Select(x => x.Ip).Distinct().ToList();
            var legacyBefore = _s.LegacyCleaned;
            var cs2Before = _s.Cs2Path;
            var cs2Running = Steam.Cs2Running;
            if (cs2Running) WatchCs2Exit();
            if (cs2Running != _cs2WasRunning)
            {
                _cs2WasRunning = cs2Running;
                if (_s.GameMode) Log.Info(cs2Running ? "CS2 aberto: todo o PC passa pelo telemóvel." : "CS2 fechado: o resto do PC volta à rede de casa.");
                force = true;
            }
            var rep = await _guard.Sync(_s, ips, force, _distrust, cs2Running);
            if (legacyBefore != _s.LegacyCleaned || cs2Before != _s.Cs2Path) SaveSettings();
            ApplyReport(rep);
        }
        finally { _syncing = false; }
        if (_syncAgain) { _syncAgain = false; await SyncNow(); }
    }

    void ApplyReport(GuardReport rep)
    {
        if (rep.Error != null && rep.Adapters.Count == 0) { SetHero("bad", "Erro a aplicar a proteção", rep.Error); return; }
        var egressKey = rep.Egress == null ? null : $"{rep.Egress.Index}|{rep.Egress.LocalIp}";
        var egressChanged = egressKey != _lastEgressKey;
        if (egressChanged && _lastEgressKey != null)
        {
            if (rep.Egress == null) Log.Warn("A saída caiu. Kill switch ativo: servidores e CS2 bloqueados.");
            else Log.Ok($"Saída: {rep.Egress.Name} ({rep.Egress.LocalIp}).");
        }
        _lastEgressKey = egressKey;
        _last = rep;

        // Alerta quando a proteção cai (com som se o CS2 estiver aberto).
        var protectedNow = rep.Egress != null && rep.FirewallProblem == null && rep.KillSwitchOk;
        if (_wasProtected && !protectedNow)
        {
            if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
            Toast((rep.EgressProblem ?? "A ligação ao telemóvel caiu.") + " Bloqueei os servidores e o CS2: o teu IP não sai.", false);
        }
        else if (!_wasProtected && protectedNow && _lastEgressKey != null && _everProtected)
            Toast("Proteção de volta.", true);
        if (protectedNow) _everProtected = true;
        _wasProtected = protectedNow;

        FillEgressCombo(rep.Adapters);

        foreach (var srv in _servers)
        {
            var ok = rep.RouteOk.TryGetValue(srv.Ip, out var r) && r && rep.KillSwitchOk;
            srv.Protected = rep.Egress == null ? (rep.KillSwitchOk ? null : false) : ok;
            srv.RouteText = rep.Egress == null ? (rep.KillSwitchOk ? "Bloqueado" : "Sem proteção") : ok ? "Protegido" : !rep.KillSwitchOk ? "Sem kill switch" : "Rota falhou";
        }

        TrackSteam(rep);
        DiagReport(rep);
        UpdateStatusUi();
        if (egressChanged) _ = RefreshPublicIps();
    }

    /// <summary>
    /// Regista por onde o Steam fala com a Valve e quando se religa. Um "No Steam logon" a meio do jogo
    /// coincide com uma destas linhas no Registo (é a prova para saber a causa).
    /// </summary>
    void TrackSteam(GuardReport rep)
    {
        var link = rep.Steam;
        var path = !link.Connected ? "off" : rep.SteamOnEgress == true ? "egress" : rep.SteamOnEgress == false ? "home" : "?";
        if (path != _steamPath)
        {
            if (path == "egress") Log.Ok($"O Steam está ligado à Valve pelo telemóvel ({rep.Egress?.LocalIp}).");
            else if (path == "home")
            {
                Log.Warn($"O Steam está ligado à Valve por outra rede ({string.Join(", ", link.LocalIps)}), não pela saída. Reinicia o Steam antes de jogar.");
                if (_s.SteamOnEgress) Toast("O Steam está noutra rede: reinicia-o (ou troca de conta) antes de jogar, senão levas kick com \"No Steam logon\".", false);
            }
        }
        _steamPath = path;
    }

    void UpdateStatusUi()
    {
        var rep = _last;
        if (rep == null) return;
        var n = _servers.Select(s => s.Ip).Distinct().Count();
        var routesOk = rep.RouteOk.Values.Count(v => v);

        _cFirewall.Ok = rep.FirewallProblem == null;
        _cFirewall.Detail = rep.FirewallProblem ?? "Ligado";
        _cKill.Ok = n == 0 ? null : rep.KillSwitchOk;
        _cKill.Detail = n == 0 ? "Sem servidores na lista" : rep.KillSwitchOk ? (n == 1 ? "1 IP bloqueado na rede de casa" : $"{n} IPs bloqueados na rede de casa") : "Regra não confirmada";
        _cEgress.Ok = rep.Egress != null;
        _cEgress.Detail = rep.Egress != null
            ? $"{rep.Egress.Name} · {rep.Egress.LocalIp}" + (rep.Egress.IsWifi && rep.Egress.Ssid != null ? $" · {rep.Egress.Ssid}" : "")
            : rep.EgressProblem ?? "Liga o telemóvel (cabo USB ou ponto de acesso)";
        _cCs2.Ok = !_s.LockCs2 ? null : rep.Cs2LockOk;
        _cCs2.Detail = !_s.LockCs2 ? "Desligado nas Definições"
            : rep.Cs2LockOk == null ? "Não encontrei o cs2.exe (abre o CS2 uma vez)"
            : rep.Cs2LockOk == true ? (rep.Egress != null ? "O CS2 só sai pelo telemóvel" : "CS2 bloqueado até ligares o telemóvel") : "Regra não confirmada";
        _cRoutes.Ok = n == 0 ? null : rep.Egress != null && routesOk == n;
        _cRoutes.Detail = n == 0 ? "Sem servidores na lista" : $"{routesOk} de {n} {(n == 1 ? "IP" : "IPs")} pelo telemóvel";
        var same = _egressPublic != null && _homePublic != null && _egressPublic == _homePublic;
        _cDiff.Ok = _egressPublic == null || _homePublic == null ? null : !same;
        _cDiff.Detail = _egressPublic == null ? "À espera do IP do telemóvel" : _homePublic == null ? "Sem rede de casa para comparar" : same ? "É o mesmo IP! O telemóvel está a partilhar o Wi-Fi" : "Diferentes";
        _cSteam.Ok = Steam.IsRunning && _steamLog.Online == false ? false
            : !SteamReady ? null
            : !_s.SteamOnEgress || rep.Egress == null ? null
            // Por UDP a tabela TCP não mostra a ligação: vale o registo do Steam mais as rotas da Valve.
            : !rep.Steam.Connected && Steam.IsRunning && _steamLog.Online == true ? rep.SteamRoutesOk
            : rep.SteamOnEgress;
        _cSteam.Detail = Steam.IsRunning && _steamLog.Online == false ? $"Sem sessão na Valve desde as {_steamLog.LastDrop:HH:mm:ss}: entrar agora dá kick"
            : !SteamReady ? "A religar-se depois da troca de IP"
            : !_s.SteamOnEgress ? "Desligado nas Definições"
            : rep.Egress == null ? "À espera do telemóvel"
            : !rep.Steam.Connected && Steam.IsRunning && _steamLog.Online == true ? "Ligado por UDP (a rota da Valve vai pela saída)"
            : !rep.Steam.Connected ? (Steam.IsRunning ? "O Steam não está ligado à Valve agora" : "Steam fechado")
            : rep.SteamOnEgress == true ? (rep.SteamRoutesOk == false ? "Ligado pelo telemóvel (rotas da Valve por confirmar)" : "Ligado pelo telemóvel, como o CS2")
            : "Ligado pela rede de casa: reinicia o Steam (ou troca de conta) antes de jogar";

        if (rep.EffectiveMode == "all")
        {
            _cPc.Ok = rep.Egress == null ? null : rep.FullModeOk;
            _cPc.Detail = rep.FullModeOk ? (_s.Mode == "all" ? "Todo o PC sai pelo telemóvel" : "CS2 aberto: todo o PC sai pelo telemóvel") : "O PC todo ainda não passou";
        }
        else
        {
            _cPc.Ok = rep.Egress == null ? null : !rep.PcDefaultIsEgress;
            _cPc.Detail = rep.PcDefaultIsEgress ? $"O PC todo está a usar {rep.PcDefaultName} (gasta dados)" : $"Rede de casa: {rep.PcDefaultName}";
        }

        var wifiLock = !string.IsNullOrEmpty(_s.EgressSsid) ? $" · só vale no ponto de acesso «{_s.EgressSsid}»" : "";
        EgressDetail.Text = rep.Egress == null
            ? (rep.EgressProblem ?? "Sem ligação ao telemóvel. Liga-o por cabo USB ou ao ponto de acesso Wi-Fi.") + wifiLock
            : $"{rep.Egress.Kind} · IP local {rep.Egress.LocalIp} · gateway {rep.Egress.Gateway ?? "direto"}{wifiLock}";
        PcRouteText.Text = rep.EffectiveMode == "all"
            ? (_s.Mode == "all" ? "Tudo, incluindo o Steam, sai pelo telemóvel." : "Modo jogo: com o CS2 aberto, todo o PC sai pelo telemóvel.")
            : $"O resto do PC continua em: {rep.PcDefaultName}";

        // Herói
        if (rep.FirewallProblem != null)
            SetHero("bad", "Firewall do Windows desligado", rep.FirewallProblem + ". Sem Firewall não há kill switch; liga-o em Segurança do Windows > Firewall.");
        else if (rep.Distrusted || same)
            SetHero("bad", "O IP é o mesmo da tua casa",
                "Bloqueei os servidores e o CS2 até o IP voltar a ser diferente. O telemóvel está a partilhar o Wi-Fi de casa, ou o PC voltou ao router. Desliga o Wi-Fi do telemóvel (cartão Telemóvel).");
        else if (rep.Egress == null && rep.EgressProblem != null)
            SetHero("bad", "Saíste da rede do telemóvel", rep.EgressProblem + " Os servidores e o CS2 estão bloqueados até voltares ao ponto de acesso: o teu IP não sai.");
        else if (rep.Egress == null)
            SetHero(n > 0 && rep.KillSwitchOk ? "warn" : "idle", "Telemóvel desligado",
                n > 0 && rep.KillSwitchOk
                    ? "Estás seguro: os servidores da lista e o CS2 estão bloqueados até ligares o telemóvel (cabo USB ou ponto de acesso)."
                    : "Liga o telemóvel por cabo USB ou ao ponto de acesso Wi-Fi.");
        else if (rep.EffectiveMode == "all" && rep.FullModeOk && (n == 0 || rep.KillSwitchOk))
            SetHero("good", _s.Mode == "all" ? "Protegido · todo o PC" : "Protegido · todo o PC com o CS2", $"Todo o tráfego sai por {rep.Egress.Name}. Qualquer servidor vê só o IP da saída.");
        else if (n == 0)
            SetHero("idle", "Pronto", "O telemóvel está ligado. Adiciona servidores na página Servidores, ou escolhe «Todo o PC» em O que sai pelo telemóvel.");
        else if (rep.KillSwitchOk && routesOk == n)
            SetHero("good", "Protegido", "Os servidores da lista só te veem pelo telemóvel. Pela rede de casa estão bloqueados.");
        else
            SetHero("warn", "Proteção incompleta", !rep.KillSwitchOk ? "O bloqueio na Firewall não foi confirmado. Vê o Registo." : $"Só {routesOk} de {n} rotas confirmadas. Outra VPN pode estar a interferir.");

        SideIp.Text = _egressPublic ?? "";
        UpdatePhoneCheck();
        ServerCount.Text = _servers.Count == 0 ? "" : $"{_servers.Count} {(_servers.Count == 1 ? "servidor" : "servidores")} em {n} {(n == 1 ? "IP" : "IPs")}";
        ServersEmpty.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryBtn.IsEnabled = CopyBtn.IsEnabled = ServerSearch.IsEnabled = _servers.Count > 0;
        ServerHeader.Visibility = _servers.Count == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    void SetHero(string kind, string title, string sub)
    {
        var (fg, bg, glyph, side) = kind switch
        {
            "good" => (Ui.Good, Ui.GoodSoft, "", "Protegido"),
            "bad" => (Ui.Bad, Ui.BadSoft, "", "Atenção"),
            "warn" => (Ui.Warn, Ui.WarnSoft, "", "Telemóvel desligado"),
            _ => (Ui.Muted, (Brush)FindResource("Surface3"), "", "Pronto"),
        };
        HeroBadge.Background = bg;
        HeroIcon.Foreground = fg;
        HeroIcon.Text = glyph;
        HeroTitle.Text = title;
        HeroSub.Text = sub;
        SideDot.Fill = fg;
        // A barra de cima repete o estado da página (só o "Protegido · …" encolhe).
        SideState.Text = kind == "good" ? side : title;
    }

    void FillEgressCombo(List<Adapter> adapters)
    {
        var usable = adapters.Where(a => a.Up && a.LocalIp != null).ToList();
        var sig = string.Join(";", usable.Select(a => a.Id + a.LocalIp + a.Ssid)) + "|" + _s.EgressAdapterId + _s.EgressSsid;
        if ((string)EgressCombo.Tag == sig) return;
        _uiLock = true;
        try
        {
            EgressCombo.Tag = sig;
            EgressCombo.Items.Clear();
            EgressCombo.Items.Add(new ComboBoxItem { Content = "Cabo USB (automático)", Tag = null });
            foreach (var a in usable.OrderByDescending(a => a.IsPhone).ThenByDescending(a => a.IsWifi))
            {
                var label = a.IsWifi
                    ? $"Ponto de acesso Wi-Fi · {(a.Id == _s.EgressAdapterId && _s.EgressSsid != null ? _s.EgressSsid : a.Ssid ?? a.Name)}"
                    : $"{a.Kind} · {a.Name}";
                EgressCombo.Items.Add(new ComboBoxItem
                {
                    Content = label, Tag = a.Id,
                    ToolTip = a.IsWifi
                        ? $"{a.Description}\nLigado agora a: {a.Ssid ?? "nenhuma rede"}.\nAo escolher, a app guarda esta rede: se o PC saltar para outra (ex.: o router de casa), bloqueia tudo."
                        : a.Description,
                });
            }
            if (_s.EgressAdapterId != null && !usable.Any(a => a.Id == _s.EgressAdapterId))
                EgressCombo.Items.Add(new ComboBoxItem { Content = _s.EgressSsid != null ? $"Ponto de acesso · {_s.EgressSsid} (desligado)" : "Placa escolhida (desligada)", Tag = _s.EgressAdapterId });
            EgressCombo.SelectedItem = EgressCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.EgressAdapterId) ?? EgressCombo.Items[0];
        }
        finally { _uiLock = false; }
    }

    async void EgressCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiLock || EgressCombo.SelectedItem is not ComboBoxItem it) return;
        var id = (string)it.Tag;
        if (id == _s.EgressAdapterId) return;
        var a = _last?.Adapters.FirstOrDefault(x => x.Id == id);
        if (a?.IsWifi == true)
        {
            if (a.Ssid == null)
            {
                Toast("Liga primeiro o Wi-Fi do PC ao ponto de acesso do telemóvel e escolhe outra vez.", false);
                FillEgressCombo(_last.Adapters);
                return;
            }
            if (!await Ask("Ponto de acesso do telemóvel",
                    $"O Wi-Fi do PC está ligado a «{a.Ssid}». Confirma que esta é a rede do telemóvel (ponto de acesso).\n\n" +
                    "A app guarda este nome: se o Wi-Fi do PC saltar para outra rede, como o router de casa, os servidores e o CS2 ficam bloqueados.",
                    "É o telemóvel"))
            {
                FillEgressCombo(_last.Adapters);
                return;
            }
            _s.EgressSsid = a.Ssid;
        }
        else _s.EgressSsid = null;
        _s.EgressAdapterId = id;
        _distrust = false;
        SaveSettings();
        EgressCombo.Tag = null;
        Log.Info(id == null ? "Saída: telemóvel por USB (automático)." : a?.IsWifi == true ? $"Saída: ponto de acesso «{_s.EgressSsid}» no {a.Name}." : $"Saída fixada: {it.Content}.");
        await SyncNow(force: true);
        _ = RefreshPublicIps();
    }

    async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_uiLock) return;
        var mode = ModeAll.IsChecked == true ? "all" : "servers";
        var game = ModeGame.IsChecked == true;
        if (mode == _s.Mode && (mode == "all" || game == _s.GameMode)) return;
        _s.Mode = mode;
        if (mode != "all") _s.GameMode = game;
        SaveSettings();
        Log.Info(mode == "all" ? "Tudo, sempre: todo o PC sai pelo telemóvel."
            : game ? "Tudo, com o CS2 aberto: enquanto o CS2 estiver aberto, todo o PC sai pelo telemóvel."
            : "Só o jogo: o resto do PC fica na rede de casa.");
        await SyncNow(force: true);
        _ = RefreshPublicIps();
    }

    async Task RefreshPublicIps()
    {
        if (_demo) return;
        _lastIpCheck = DateTime.Now;
        var rep = _last;
        // Mede-se pela candidata (mesmo quando já não se confia nela) para saber quando volta a ser segura.
        var candidate = rep?.Candidate;
        // Casa = a placa que o Windows usa para o resto do PC. Se essa placa for a do telemóvel (PC inteiro,
        // modo jogo, ou só há o ponto de acesso), não há como medir: mostra-se o último IP de casa confirmado.
        var homeIf = rep == null || rep.PcDefaultIsEgress || candidate?.Index == rep.PcDefaultIndex ? -1 : rep.PcDefaultIndex;

        var tE = Net.EgressPublicIp(candidate);
        var tH = Net.HomePublicIp(homeIf);
        string e = null, h = null;
        try { e = await tE; } catch { }
        try { h = await tH; } catch { }

        // 3.ª barreira: o IP da saída igual ao de casa = a saída não está a usar os dados móveis. Bloqueia tudo.
        var homeRef = h ?? _s.LastHomeIp;
        var same = e != null && homeRef != null && e == homeRef;
        if (same != _distrust)
        {
            _distrust = same;
            if (same)
            {
                Log.Error($"O IP da saída ({e}) é igual ao de casa. Servidores e CS2 bloqueados.");
                if (Steam.Cs2Running) System.Media.SystemSounds.Hand.Play();
                Toast("O IP do telemóvel é igual ao de casa. Bloqueei os servidores e o CS2.", false);
            }
            else Log.Ok("O IP da saída voltou a ser diferente do de casa.");
            _ = SyncNow(force: true);
        }

        if (e != _egressPublic && e != null && !same)
        {
            _egressInfo = await Net.Lookup(e);
            AddHistory(e, _egressInfo, "detetado");
        }
        if (h != null)
        {
            if (h != _homePublic) _homeInfo = await Net.Lookup(h);
            _homeCached = false;
            if (_s.LastHomeIp != h) { _s.LastHomeIp = h; SaveSettings(); }
        }
        else if (_s.LastHomeIp != null)
        {
            h = _s.LastHomeIp;
            _homeCached = true;
            if (h != _homePublic) _homeInfo = await Net.Lookup(h);
        }
        _egressPublic = e;
        _homePublic = h;
        DiagIps(e, h, same);
        ShowIps();
        UpdateStatusUi();
    }

    void ShowIps()
    {
        EgressIp.Text = _egressPublic ?? "—";
        EgressIsp.Text = _egressPublic == null ? (_last?.Egress == null ? "Sem ligação ao telemóvel" : "A obter o IP pelo telemóvel…")
            : (_egressInfo?.Short ?? "") + (_egressInfo?.Proxy == true ? "  ·  marcado como VPN/proxy" : "");
        HomeIp.Text = _homePublic == null ? "—" : _reveal ? _homePublic : Fmt.MaskIp(_homePublic);
        HomeIsp.Text = _homePublic == null ? "Sem rede de casa detetada"
            : (_homeInfo?.Short ?? "") + (_homeCached ? "  ·  último confirmado" : "");
        SideIp.Text = _egressPublic ?? "";
        CopyIpBtn.Visibility = _egressPublic == null ? Visibility.Collapsed : Visibility.Visible;
        RevealBtn.Visibility = _homePublic == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void AddHistory(string ip, Net.IpInfo info, string reason)
    {
        if (_history.Count > 0 && _history[0].Ip == ip) return;
        var h = new IpHistoryEntry { Time = DateTime.Now, Ip = ip, Isp = info?.Short, Reason = reason };
        _history.Insert(0, h);
        while (_history.Count > 30) _history.RemoveAt(_history.Count - 1);
        _s.IpHistory = _history.ToList();
        SaveSettings();
        UpdateHistoryEmpty();
    }

    void UpdateHistoryEmpty()
    {
        HistoryEmpty.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryBtn.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        _s.IpHistory = new();
        SaveSettings();
        UpdateHistoryEmpty();
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        _reveal = !_reveal;
        RevealBtn.Content = _reveal ? "" : "";
        ShowIps();
    }

    void CopyEgressIp_Click(object sender, RoutedEventArgs e)
    {
        if (_egressPublic == null) return;
        Clipboard.SetText(_egressPublic);
        Toast("IP copiado.", true);
    }

    async void LeakTest_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Teste concluído: tudo protegido.", true); return; }
        LeakBtn.IsEnabled = false;
        if (!_tick.IsEnabled) _tick.Start();
        ShowProgress("A testar: Firewall, kill switch, rotas e IP público…");
        try
        {
            await SyncNow(force: true);
            await RefreshPublicIps();
            var bad = _checks.Where(c => c.Ok == false).Select(c => c.Title).ToList();
            if (bad.Count == 0)
            {
                Log.Ok("Teste de fugas: tudo confirmado.");
                Toast(_egressPublic != null ? $"Sem fugas. Os servidores veem {_egressPublic}." : "Sem fugas: os servidores estão bloqueados até ligares o telemóvel.", true);
            }
            else
            {
                Log.Warn("Teste de fugas falhou em: " + string.Join(", ", bad));
                Toast("Atenção: " + string.Join(", ", bad) + ". Vê a Verificação.", false);
            }
        }
        finally { HideProgress(); LeakBtn.IsEnabled = true; }
    }
}
