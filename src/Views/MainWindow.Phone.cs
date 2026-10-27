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

// Telemóvel e troca de IP
public partial class MainWindow
{
    async Task RefreshPhone()
    {
        if (_demo) return;
        _adb ??= Adb.Find();
        if (_adb == null)
        {
            SetPhone(Ui.Faint, "ADB não instalado", "Precisas dele para trocar o IP pelo botão. Instala com um clique.");
            AdbInstallBtn.Visibility = Visibility.Visible;
            return;
        }
        AdbInstallBtn.Visibility = Visibility.Collapsed;
        var info = await Adb.Status(_adb);
        _phone = info;
        DiagPhone(info);
        if (info == null) SetPhone(Ui.Bad, "ADB não arrancou", "Tenta reinstalar o ADB.");
        else if (info.State == "none") SetPhone(Ui.Faint, "Telemóvel não visto pelo ADB", "Liga a Depuração USB nas opções de programador. Vê \"Preparar o telemóvel\".");
        else if (!info.Ready) SetPhone(Ui.Warn, "À espera de autorização", info.Summary);
        else if (info.WifiOn)
            SetPhone(Ui.Warn, "Wi-Fi do telemóvel ligado",
                (info.WifiSsid != null ? $"Ligado a {info.WifiSsid}. " : "") + "Com o Wi-Fi ligado, o PC pode receber a rede de casa em vez dos dados móveis.");
        else SetPhone(Ui.Good, "Pronto para trocar IP", info.Summary);
        WifiOffBtn.Visibility = info?.Ready == true && info.WifiOn ? Visibility.Visible : Visibility.Collapsed;
        UpdatePhoneCheck();
    }

    /// <summary>Verificação "Dados móveis": o telemóvel partilha os dados e não o Wi-Fi de casa.</summary>
    void UpdatePhoneCheck()
    {
        // Trocar IP só faz sentido com o telemóvel pronto no ADB.
        ChangeIpBtn.IsEnabled = !_changingIp && _phone?.Ready == true;
        var egress = _last?.Egress;
        if (egress != null && egress.IsWifi)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Detail = "Pelo ponto de acesso: o telemóvel já usa os dados";
        }
        else if (_phone?.Ready != true)
        {
            _cPhoneData.Ok = null;
            _cPhoneData.Detail = _adb == null ? "Sem ADB: confirma que o Wi-Fi do telemóvel está desligado" : "Telemóvel não visto pelo ADB";
        }
        else
        {
            _cPhoneData.Ok = !_phone.WifiOn;
            _cPhoneData.Detail = _phone.WifiOn ? $"O Wi-Fi do telemóvel está ligado{(_phone.WifiSsid != null ? $" ({_phone.WifiSsid})" : "")}: desliga-o" : $"{_phone.Carrier} · {_phone.NetType}".Trim(' ', '·');
        }
    }

    async void WifiOff_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) return;
        await TurnPhoneWifiOff();
        await Task.Delay(5000);
        await RefreshPhone();
        await RefreshPublicIps();
    }

    async Task TurnPhoneWifiOff()
    {
        var r = await Adb.DisableWifi(_adb, _phone.Serial);
        if (r.Ok) { Log.Ok("Wi-Fi do telemóvel desligado: o tethering passa a usar os dados móveis."); Toast("Wi-Fi do telemóvel desligado.", true); }
        else { Log.Warn("Desligar o Wi-Fi do telemóvel: " + r.Output); Toast("O telemóvel recusou. Desliga o Wi-Fi à mão.", false); }
    }

    void SetPhone(Brush dot, string state, string detail)
    {
        PhoneDot.Fill = dot;
        PhoneState.Text = state;
        PhoneDetail.Text = detail;
    }

    async void AdbInstall_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Ask("Instalar o ADB", "Vou descarregar as platform-tools oficiais da Google (cerca de 7 MB) de dl.google.com para a pasta SRT_Dados ao lado da app. Só é preciso uma vez.", "Descarregar");
        if (!ok) return;
        AdbInstallBtn.IsEnabled = false;
        ShowProgress("A descarregar o ADB da Google…");
        try
        {
            _adb = await Adb.Download(new Progress<string>(t => ProgressText.Text = t));
            if (_adb != null) { Log.Ok("ADB instalado em " + _adb); Toast("ADB instalado.", true); }
            else Toast("Descarreguei, mas não encontrei o adb.exe.", false);
        }
        catch (Exception ex) { Log.Error("Download do ADB: " + ex.Message); Toast("Não consegui descarregar o ADB: " + ex.Message, false); }
        finally { HideProgress(); AdbInstallBtn.IsEnabled = true; await RefreshPhone(); }
    }

    async void Tether_Click(object sender, RoutedEventArgs e)
    {
        if (_phone?.Ready != true) { Toast("O telemóvel tem de estar pronto no ADB primeiro.", false); return; }
        var r = await Adb.EnableUsbTether(_adb, _phone.Serial);
        Log.Info("Pedido de tethering USB: " + (r.Ok ? "enviado" : r.Output));
        Toast(r.Ok ? "Pedido enviado. Se não ligar em 10 s, liga a partilha USB à mão." : "O telemóvel recusou. Liga a partilha USB à mão.", r.Ok);
        await Task.Delay(4000);
        await SyncNow(force: true);
    }

    void PhoneHelp_Click(object sender, RoutedEventArgs e) => _ = Ask("Preparar o telemóvel (uma vez)",
        "1. Definições > Acerca do telemóvel > Informações do software: toca 7 vezes em \"Número de compilação\".\n" +
        "2. Opções de programador: liga \"Depuração USB\".\n" +
        "3. Liga o cabo. No telemóvel aceita \"Permitir depuração USB\" (marca \"Permitir sempre\").\n" +
        "4. Definições > Ligações > Hotspot e tethering: liga \"Tethering USB\".\n" +
        "5. Desliga o Wi-Fi do telemóvel: senão ele partilha a rede de casa e o IP fica igual.\n\n" +
        "Se a app não vir o telemóvel no ADB: instala o \"Samsung USB Driver for Mobile Phones\" (site da Samsung) e volta a ligar o cabo.\n" +
        "Sem depuração USB tudo funciona, exceto o botão Trocar IP: nesse caso liga e desliga o modo avião à mão.",
        "Percebi", null);

    async void ChangeIp_Click(object sender, RoutedEventArgs e)
    {
        if (_changingIp) return;
        if (_demo) { Toast("Demonstração: IP trocado.", true); return; }
        if (_last?.Egress != null && !_last.Egress.IsPhone)
        {
            Toast("A ligação escolhida não é um telemóvel: o botão Trocar IP só funciona com o telemóvel por ADB.", false);
            return;
        }
        if (_adb == null || _phone?.Ready != true)
        {
            Toast(_adb == null ? "Instala o ADB primeiro (cartão Telemóvel)." : "O telemóvel não está pronto no ADB. Vê \"Preparar o telemóvel\".", false);
            return;
        }
        if (Steam.Cs2Running && !await Ask("O CS2 está aberto", "Se estiveres dentro de um servidor, vais cair quando o IP mudar. Continuar?", "Trocar IP")) return;

        _changingIp = true;
        ChangeIpBtn.IsEnabled = false;
        ChangeIpBtn.Content = "A trocar…";
        var method = _s.ChangeMethod == "airplane" ? Adb.Method.Airplane : Adb.Method.MobileData;
        var old = _egressPublic;
        string got = null;
        _ipChangedAt = DateTime.Now.AddTicks(-(DateTime.Now.Ticks % TimeSpan.TicksPerSecond));
        Diag.Write("trocar-ip", new { de = old });
        try
        {
            if (_phone.WifiOn)
            {
                ShowProgress("O Wi-Fi do telemóvel está ligado: a desligá-lo para usar os dados móveis…");
                await TurnPhoneWifiOff();
                await Task.Delay(3000);
            }
            for (var attempt = 1; attempt <= Math.Max(1, _s.MaxAttempts); attempt++)
            {
                ShowProgress($"Tentativa {attempt}/{_s.MaxAttempts}: a desligar os dados do telemóvel por {_s.OffSeconds} s…");
                var err = await Adb.Cycle(_adb, _phone.Serial, method, Math.Clamp(_s.OffSeconds, 1, 60));
                if (err != null) { Log.Error(err); Toast(err, false); return; }

                ProgressText.Text = $"Tentativa {attempt}/{_s.MaxAttempts}: à espera que a rede móvel volte…";
                got = await WaitForEgressIp(TimeSpan.FromSeconds(50));
                if (got == null) { ProgressText.Text = "A rede ainda não voltou…"; continue; }
                if (got != old) break;
                Log.Info($"A operadora devolveu o mesmo IP ({got}).");
            }

            if (got != null) ResetSteamAfterIpChange();
            Diag.Write("ip-trocado", new { de = old, para = got });
            if (got != null && got != old)
            {
                _egressPublic = got;
                _egressInfo = await Net.Lookup(got);
                AddHistory(got, _egressInfo, "trocado");
                Log.Ok($"IP trocado: {old ?? "?"} → {got}");
                Toast($"IP novo: {got}", true);
            }
            else if (got == old)
                Toast("A operadora deu sempre o mesmo IP. Tenta o modo avião nas Definições ou espera uns minutos.", false);
            else
                Toast("A rede móvel não voltou a tempo. Confirma o telemóvel.", false);
        }
        finally
        {
            _changingIp = false;
            ChangeIpBtn.Content = "Trocar IP";
            UpdatePhoneCheck();
            HideProgress();
            ShowIps();
            await SyncNow(force: true);
        }
    }

    async Task<string> WaitForEgressIp(TimeSpan max)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(2500);
        while (sw.Elapsed < max)
        {
            var adapters = await Task.Run(Net.Scan);
            var egress = Net.PickEgress(adapters, _s.EgressAdapterId, _s.EgressSsid);
            if (egress != null)
            {
                // Se o cabo voltou com outra placa, a rota do teste de IP tem de ser refeita antes de perguntar.
                if (Net.BestInterface(Net.EchoIp) != egress.Index) await SyncNow(force: true);
                try
                {
                    using var cts = new CancellationTokenSource(6000);
                    var ip = await Net.EgressPublicIp(egress, cts.Token);
                    if (ip != null) return ip;
                }
                catch { }
            }
            await Task.Delay(2000);
        }
        return null;
    }
}
