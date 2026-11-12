using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using SRT;
using SRT.Core;

// 10 nov. (sessão 10, onboarding): dos 14 utilizadores reais de fora nenhum ligou o telemóvel (publish/DASHBOARD-2026-11-10.md).
// Um parou em "Sem ADB" (saiu 2 vezes), outro em "Sem cabo USB". Cada mudança leva aqui o seu teste.
static class OnboardingTests
{
    public static void Run(Action<string, bool, string> check)
    {
        AdbSozinho(check);
        Cabo(check);
        Depuracao(check);
        Visita(check);
        Regions(check);
        Textos(check);
    }

    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { SecurityTests.Repo(), "src" }.Concat(p).ToArray()));

    static void AdbSozinho(Action<string, bool, string> check)
    {
        check("Onboarding ADB: o progresso diz MB e percentagem", Texts.AdbDownloading(3_250_000, 7_610_000) == "Downloading from Google: 3.1 of 7.3 MB (42%)",
            Texts.AdbDownloading(3_250_000, 7_610_000));
        check("Onboarding ADB: sem tamanho do Google, só os MB", Texts.AdbDownloading(1_048_576, null) == "Downloading from Google: 1.0 MB", Texts.AdbDownloading(1_048_576, null));
        check("Onboarding ADB: nunca passa de 100%", Texts.AdbDownloading(9_000_000, 7_000_000).EndsWith("(100%)"), "");

        var said = new List<string>();
        var pct = new List<double>();
        var to = new MemoryStream();
        Adb.CopyWithProgress(new MemoryStream(new byte[300_000]), to, 300_000, new SyncProgress<string>(said.Add), new SyncProgress<double>(pct.Add)).GetAwaiter().GetResult();
        check("Onboarding ADB: a cópia escreve tudo e acaba nos 100%", to.Length == 300_000 && said.Last().EndsWith("(100%)") && pct.Last() == 100, $"{to.Length} {said.LastOrDefault()}");

        var st = new PhoneSetupState { AdbTool = false, AdbState = "none" };
        var auto = PhoneSetup.Steps(st)[0];
        check("Onboarding ADB: sem ADB a linha diz que já está a descarregar, sem botão", auto.Step == PhoneStep.NoAdbTool && auto.State == PhoneSetup.StepState.Todo
            && auto.Action == null && auto.Detail == Texts.AdbStarting, auto.Detail);
        var wait = PhoneSetup.Steps(st with { AdbWaits = true })[0];
        check("Onboarding ADB: nos dados móveis espera pelo Install", wait.Action == Texts.AdbInstall && wait.Detail.Contains("mobile data"), wait.Detail);
        var fail = PhoneSetup.Steps(st with { AdbFailed = "no connection to dl.google.com" })[0];
        check("Onboarding ADB: falhou, diz porquê, Try again e o caminho à mão", fail.Action == Texts.AdbTryAgain && fail.Detail.Contains("no connection to dl.google.com")
            && fail.Detail.Contains("developer.android.com") && fail.Detail.Contains("Settings > Maintenance > Open data folder"), fail.Detail);
        check("Onboarding ADB: com ADB a linha fica verde como antes", PhoneSetup.Steps(st with { AdbTool = true })[0].State == PhoneSetup.StepState.Ok, "");
        check("Onboarding ADB: o passo continua a ser noadbtool (o funil da dashboard)", PhoneSetup.StepKey(PhoneSetup.Steps(st)) == "noadbtool", "");

        check("Onboarding ADB: descarrega sozinho ao abrir o Phone setup", MainWindow.AutoInstallAdb(false, false, false, false), "");
        check("Onboarding ADB: não quando já está, já falhou, nos dados móveis ou num iPhone",
            !MainWindow.AutoInstallAdb(true, false, false, false) && !MainWindow.AutoInstallAdb(false, true, false, false)
            && !MainWindow.AutoInstallAdb(false, false, true, false) && !MainWindow.AutoInstallAdb(false, false, false, true), "");
        check("Onboarding ADB: o erro em poucas palavras", MainWindow.AdbErrorText(new TaskCanceledException()) == "it took too long"
            && MainWindow.AdbErrorText(new System.Net.Http.HttpRequestException("x")) == "no connection to dl.google.com"
            && MainWindow.AdbErrorText(new System.Net.Http.HttpRequestException("x", null, System.Net.HttpStatusCode.NotFound)) == "Google answered 404", "");

        var phone = Src("Views", "MainWindow.Phone.cs");
        check("Onboarding ADB: o Phone setup arranca o download sozinho", phone.Contains("await SetupInstallAdb(auto: true);"), "");
        check("Onboarding ADB: sem a pergunta de antes no Phone setup", !phone.Contains("\"Download\", \"Not now\""), "");
        check("Onboarding ADB: o clique Setup_noadbtool continua a contar (pressed na dashboard)",
            phone.Contains("Telemetry.Click(\"Setup_\" + PhoneSetup.Key(PhoneStep.NoAdbTool));") && phone.Contains("Telemetry.Count(ok ? \"adb_ok\" : \"adb_fail\");"), "");
        check("Onboarding ADB: a barra de progresso na linha", Src("MainWindow.xaml").Contains("Value=\"{Binding PercentValue, Mode=OneWay}\""), "");
    }

    static void Cabo(Action<string, bool, string> check)
    {
        var noUsb = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none" }).First(i => i.Step == PhoneStep.NoUsb);
        check("Onboarding cabo: diz que é um cabo de dados e porquê", noUsb.State == PhoneSetup.StepState.Todo && noUsb.Detail.Contains("data cable") && noUsb.Detail.Contains("only charge"), noUsb.Detail);
        var early = PhoneSetup.LiveDetail(noUsb, 6);
        check("Onboarding cabo: mostra o que está a ver, ao vivo", early.Contains(Texts.CableLive(6)) && !early.Contains("Still nothing"), early);
        var late = PhoneSetup.LiveDetail(noUsb, Texts.CableHelpAfterSeconds);
        check("Onboarding cabo: depois de 20 s, o que tentar e o hotspot sem cabo", late.Contains("another cable") && late.Contains("Wi-Fi hotspot")
            && late.Contains("How the PC connects to the phone"), late);
        var hot = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Hotspot = true }).First(i => i.Step == PhoneStep.NoUsb);
        check("Onboarding cabo: com o hotspot nada muda (o cabo é só para o Change IP)", PhoneSetup.LiveDetail(hot, 60) == hot.Detail, hot.Detail);
        var allow = PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "unauthorized", Usb = new(true, null, "Google") }).First(i => i.Step == PhoneStep.Unauthorized);
        check("Onboarding Allow: ao fim de 15 s diz o que fazer se o telemóvel não pergunta",
            PhoneSetup.LiveDetail(allow, 5) == allow.Detail && PhoneSetup.LiveDetail(allow, 15).Contains("Revoke USB debugging authorizations"), "");
        check("Onboarding: uma linha feita nunca leva texto ao vivo", PhoneSetup.LiveDetail(PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "device", Tethered = true })[1], 99) == "Phone plugged in", "");
        check("Onboarding cabo: o painel conta o tempo do passo atual", Src("Views", "MainWindow.Phone.cs").Contains("PhoneSetup.LiveDetail(i, seconds)"), "");
    }

    static void Depuracao(Action<string, bool, string> check)
    {
        check("Onboarding marca: Samsung pelo fabricante ou pelo modelo SM-", PhoneSetup.GuideFor("Samsung", null).Brand == "samsung" && PhoneSetup.GuideFor("Android", "SM-S921B").Brand == "samsung", "");
        check("Onboarding marca: Xiaomi (Redmi e POCO têm o mesmo ID) e Pixel", PhoneSetup.GuideFor("Xiaomi", null).Brand == "xiaomi" && PhoneSetup.GuideFor("Google", "Pixel 8").Brand == "google", "");
        check("Onboarding marca: outra marca ou nenhuma, o genérico", PhoneSetup.GuideFor("OnePlus", null).Brand == "other" && PhoneSetup.GuideFor(null, null).Brand == "other", "");
        check("Onboarding marca: a escolhida à mão ganha à vista no USB", PhoneSetup.GuideFor("Samsung", null, "xiaomi").Brand == "xiaomi", "");
        check("Onboarding marca: \"seen on USB\" só para uma marca com guia", PhoneSetup.SeenBrand("Samsung", null) == "samsung" && PhoneSetup.SeenBrand("OnePlus", null) == null, "");
        check("Onboarding guia: Samsung, Xiaomi, Pixel e Outra, 3 passos cada, o último a pergunta Allow",
            Texts.Guides.Select(g => g.Brand).SequenceEqual(new[] { "samsung", "xiaomi", "google", "other" })
            && Texts.Guides.All(g => g.Steps.Length == 3 && g.Steps[2].Screen.Prompt && g.Steps[1].Screen.Switch && g.Steps[1].Screen.Row == "USB debugging"), "");
        check("Onboarding guia: só a Samsung está verificada num telemóvel real (as outras levam a dica da pesquisa)",
            Texts.Guides.Where(g => g.Checked).Select(g => g.Brand).SequenceEqual(new[] { "samsung" }), "");
        check("Onboarding guia: Xiaomi toca em OS version e abre Additional settings", Texts.Guides[1].Steps[0].Screen.Row == "OS version"
            && Texts.Guides[1].Steps[1].Text.Contains("Additional settings > Developer options"), "");
        check("Onboarding guia: a linha da depuração fica curta (os passos estão no guia)",
            PhoneSetup.Steps(new PhoneSetupState { AdbTool = true, AdbState = "none", Usb = new(true, null, "Google") }).First(i => i.Step == PhoneStep.NoUsbDebugging).Detail == Texts.DebugTodo, "");
        check("Onboarding depuração: ligada é dita (o telemóvel pede Allow)", PhoneSetup.DebugJustOn("nousbdebugging", "unauthorized") && PhoneSetup.DebugJustOn("nousbdebugging", "tetheroff"), "");
        check("Onboarding depuração: nada a dizer noutras mudanças", !PhoneSetup.DebugJustOn("nousb", "nousbdebugging") && !PhoneSetup.DebugJustOn(null, "unauthorized")
            && !PhoneSetup.DebugJustOn("nousbdebugging", "nousb"), "");
        check("Onboarding depuração: o guia só aparece no passo da depuração", Src("Views", "MainWindow.Phone.cs").Contains("ShowGuide(now?.Step == PhoneStep.NoUsbDebugging && _setupBusy == null ? st : null);"), "");
    }

    static void Visita(Action<string, bool, string> check)
    {
        check("Onboarding visita: abre sozinha uma vez, antes de qualquer telemóvel", MainWindow.TourDue(false, false, false), "");
        check("Onboarding visita: não depois de vista, com telemóvel já visto, nem no demo",
            !MainWindow.TourDue(true, false, false) && !MainWindow.TourDue(false, true, false) && !MainWindow.TourDue(false, false, true), "");
        check("Onboarding visita: 4 a 6 paragens", Texts.Tour.Length is >= 4 and <= 6, Texts.Tour.Length.ToString());
        var xaml = Src("MainWindow.xaml");
        check("Onboarding visita: cada paragem aponta para um controlo real da janela", Texts.Tour.All(s => xaml.Contains($"x:Name=\"{s.Target}\"")),
            string.Join(",", Texts.Tour.Where(s => !xaml.Contains($"x:Name=\"{s.Target}\"")).Select(s => s.Target)));
        check("Onboarding visita: acaba no Phone setup", Texts.Tour.Last().Target == "PhoneGroup", "");
        check("Onboarding visita: as contas ao fechar", MainWindow.TourCounts(MainWindow.TourEnd.Skip, 1).SequenceEqual(new[] { "tour_skip_2" })
            && MainWindow.TourCounts(MainWindow.TourEnd.Later, 4).SequenceEqual(new[] { "tour_done" })
            && MainWindow.TourCounts(MainWindow.TourEnd.Setup, 4).SequenceEqual(new[] { "tour_done", "tour_setup" }), "");
        var name = new Regex(@"^[A-Za-z0-9_]{1,48}$");
        check("Onboarding visita: os nomes passam no formato da telemetria", Enumerable.Range(0, 5).All(i => name.IsMatch(MainWindow.TourCounts(MainWindow.TourEnd.Skip, i)[0])), "");
        var win = new Size(1240, 756);
        var box = new Size(360, 150);
        var right = MainWindow.TourBoxAt(new Rect(100, 300, 550, 200), box, win);
        check("Onboarding visita: a caixa vai à direita do controlo quando cabe", right.X == 664 && right.Y == 300, right.ToString());
        var below = MainWindow.TourBoxAt(new Rect(100, 100, 1000, 100), box, win);
        check("Onboarding visita: senão por baixo", below.X == 100 && below.Y == 214, below.ToString());
        var inside = MainWindow.TourBoxAt(new Rect(0, 0, 1240, 756), box, win);
        check("Onboarding visita: sempre dentro da janela", inside.X >= 12 && inside.Y >= 12 && inside.X + box.Width <= win.Width - 12 + 0.1 && inside.Y + box.Height <= win.Height, inside.ToString());
        check("Onboarding visita: Skip, Esc e Settings > App > Show the tour again", xaml.Contains("Click=\"TourSkip_Click\"") && Src("Views", "MainWindow.Feedback.cs").Contains("TourKey(e.Key)")
            && xaml.Contains("Click=\"TourAgain_Click\"") && Texts.TourAgainHint.Contains("Settings > App > Show the tour again"), "");
        check("Onboarding visita: vista fica guardada ao abrir", Src("Views", "MainWindow.Tour.cs").Contains("_s.TourSeen = true;"), "");
        check("Onboarding visita: as definições antigas não a trazem a quem já tem telemóvel (PhoneSeen)", !new Settings().TourSeen && !MainWindow.TourDue(new Settings().TourSeen, true, false), "");
    }

    static void Regions(Action<string, bool, string> check)
    {
        check("Onboarding Regions: diz uma vez o que o telemóvel dá", MainWindow.RegionsPitchDue(false, false, false, false), "");
        check("Onboarding Regions: não a quem já viu, já tem telemóvel, escolheu No phone, ou no demo",
            !MainWindow.RegionsPitchDue(true, false, false, false) && !MainWindow.RegionsPitchDue(false, true, false, false)
            && !MainWindow.RegionsPitchDue(false, false, true, false) && !MainWindow.RegionsPitchDue(false, false, false, true), "");
        check("Onboarding Regions: a nota tem Set up the phone e Hide", Src("MainWindow.xaml").Contains("Click=\"RegionsPitchSetup_Click\"") && Src("MainWindow.xaml").Contains("Click=\"RegionsPitchHide_Click\""), "");
    }

    static void Textos(Action<string, bool, string> check)
    {
        var texts = Src("Texts.cs");
        check("Onboarding textos: sem travessões", !texts.Contains('—') && !texts.Contains('–'), "");
        // Os textos novos vêm de Texts (a tradução a seguir só procura lá): nenhum literal novo no XAML da visita, da nota e do botão
        var xaml = Src("MainWindow.xaml");
        foreach (var t in new[] { Texts.TourSkip, Texts.TourAgain, Texts.RegionsPitch, Texts.RegionsPitchSetup })
            check($"Onboarding textos: \"{t[..Math.Min(20, t.Length)]}\" não está escrito no XAML", !xaml.Contains($"\"{t}\""), "");
        check("Onboarding textos: o XAML lê-os de Texts", Regex.Matches(xaml, @"\{x:Static local:Texts\.").Count >= 8, "");
    }

    /// <summary>Progress that reports at once, on the same thread (Progress&lt;T&gt; posts to the context, later).</summary>
    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
