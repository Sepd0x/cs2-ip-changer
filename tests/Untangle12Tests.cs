using System.IO;
using SRT;
using SRT.Core;

// Beta.12 (6 nov.): o refactor do untangle-codebase (publish/untangle-2026-11-06/registo.md). Testes de comportamento: cada um
// falhava antes da sua correção.
static class Untangle12Tests
{
    public static async Task Run(Action<string, bool, string> check)
    {
        await MetricBack(check);
        IphoneOnWifi(check);
        StoppedClock(check);
    }

    // O relógio do demo parado: o que a app mostra e carimba lê o AppClock, por isso as capturas saem iguais em cada corrida
    static void StoppedClock(Action<string, bool, string> check)
    {
        var at = new DateTime(2026, 11, 6, 16, 44, 0, DateTimeKind.Local);
        LogEntry got = null;
        void Seen(LogEntry e) { if (e.Text == "untangle12 clock") got = e; }
        AppClock.StopAt(at);
        try
        {
            Log.Added += Seen;
            Log.Info("untangle12 clock");
            check("Relógio: parado, diz sempre a mesma hora", AppClock.Now == at && AppClock.Today == at.Date, "");
            check("Relógio: a linha do Log leva a hora do relógio da app", got?.Time == at, got?.Time.ToString("O") ?? "sem linha");
            check("Relógio: \"há 5 min\" conta a partir do relógio da app", Fmt.Ago(at.AddMinutes(-5)) == "5 min ago", Fmt.Ago(at.AddMinutes(-5)));
            check("Relógio: Previous IPs mostra a hora no dia do relógio", new IpHistoryEntry { Time = at.AddHours(-3) }.TimeText == "13:44"
                && new IpHistoryEntry { Time = at.AddDays(-1) }.TimeText == "5 Nov", "");
        }
        finally { Log.Added -= Seen; AppClock.Run(); }
        check("Relógio: volta a andar com o Windows", Math.Abs((AppClock.Now - DateTime.Now).TotalSeconds) < 5, "");
    }

    /// <summary>Um owned.json desta sessão do Windows (a métrica só vive até reiniciar).</summary>
    static string OwnedWithMetric(int ifIndex)
    {
        var dir = Path.Combine(Path.GetTempPath(), "srt-untangle12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "owned.json");
        Store.Save(file, new Guard.OwnedState { MetricIf = ifIndex, Boot = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64) });
        return file;
    }

    // A7: devolver o adaptador do telemóvel ao fechar com o PowerShell a falhar não pode esquecer que a métrica 900 ficou lá
    static async Task MetricBack(Action<string, bool, string> check)
    {
        var file = OwnedWithMetric(7);
        var failing = new Guard(file, (args, ms) => Task.FromResult(new ProcResult(1, "NetTCPIP\\Set-NetIPInterface : Access is denied.")));
        check("A7: o Guard lê a métrica aplicada do owned.json", failing.PcOffPhoneApplied, "");
        await failing.ReleasePcOffPhone();
        check("A7: com o PowerShell a falhar a métrica continua marcada", failing.PcOffPhoneApplied && failing.Owned().MetricIf == 7, "");
        check("A7: e o owned.json guarda-a para o próximo arranque ou o --restore",
            Store.Load(file, () => new Guard.OwnedState()).MetricIf == 7, "");

        string ran = null;
        var working = new Guard(file, (args, ms) => { ran = args; return Task.FromResult(new ProcResult(0, "")); });
        await working.ReleasePcOffPhone();
        check("A7: com o PowerShell a funcionar a métrica sai", !working.PcOffPhoneApplied && Store.Load(file, () => new Guard.OwnedState()).MetricIf == -1, "");
        check("A7: e o comando devolve a rota por defeito ao adaptador 7", ran?.Contains("-InterfaceIndex 7 ") == true && ran.Contains("-IgnoreDefaultRoutes Disabled"), ran ?? "");
    }

    // A8: um iPhone só por hotspot Wi-Fi, sem cabo, recebe os passos do iPhone e não os do Android
    static void IphoneOnWifi(Action<string, bool, string> check)
    {
        var none = PhoneUsbState.None;
        check("A8: um hotspot com nome de iPhone diz que é um iPhone", PhoneUsb.IphoneSeen(false, none, null, "Ana's iPhone") == true, "");
        check("A8: um Android pronto no ADB diz que não é", PhoneUsb.IphoneSeen(true, none, null, "Ana's iPhone") == false, "");
        check("A8: um hotspot Wi-Fi com outro nome e sem cabo não diz nada", PhoneUsb.IphoneSeen(false, none, null, "MEO-5G") == null, "");
        check("A8: sem nada visto, o iPhone guardado vale enquanto há hotspot", PhoneUsb.IsIphoneNow(null, true, "MEO-5G"), "");
        check("A8: sem hotspot, o iPhone guardado não vale (o cabo decide)", !PhoneUsb.IsIphoneNow(null, true, null), "");
        check("A8: o que se vê ganha ao guardado", !PhoneUsb.IsIphoneNow(false, true, "MEO-5G") && PhoneUsb.IsIphoneNow(true, false, null), "");

        var steps = PhoneSetup.Steps(new PhoneSetupState { Iphone = true, Hotspot = true, Usb = none }, ssid: "MEO-5G");
        check("A8: o Phone setup de um iPhone só por Wi-Fi diz que o cabo é opcional",
            steps.Any(s => s.Detail?.Contains("the cable is optional") == true) && !steps.Any(s => s.Detail?.Contains("USB debugging") == true), "");
        check("A8: o iPhone fica guardado nas definições", typeof(Settings).GetProperty(nameof(Settings.PhoneIsIphone)) != null, "");
    }
}
