using System.Text;
using System.Text.Json;
using System.Windows;
using SRT.Core;

namespace SRT;

// Log > Save bug report: a .zip for a bug report, without the API key and the home IP
public partial class MainWindow
{
    async void DeleteLogs_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask(Loc.T("Delete log files"), Loc.T("Deletes registo.log, its older copy and crash.txt from SRT_Dados. They hold your home IP, Steam names and servers. Save bug report won't have them afterwards."), Loc.T("Delete"))) return;
        var n = Log.DeleteFiles();
        LogClear_Click(sender, e);
        Toast(n > 0 ? Loc.T("Log files deleted.") : Loc.T("There were no log files."), true);
    }

    async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast(Loc.T("Demo: the .zip would go to the Desktop."), true); return; }
        if (Rec.On) { Toast(Loc.T("Off in the recording build: use the normal app to save a bug report."), false); return; }
        ShowProgress(Loc.T("Collecting info for the bug report…"));
        try
        {
            var now = await BuildReportText();
            var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(_s));
            copy.ForgetApiKey();
            copy.LastHomeIp = null;
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            var steamDir = Steam.InstallDir();
            var console = Cs2ConsoleLog();
            var hide = new[] { _s.LastHomeIp, _homePublic };
            var personal = PersonalWords().Where(w => w != _homeInfo?.Country && w != _egressInfo?.Country).ToList();   // the country helps a bug report
            var path = await Task.Run(() => Report.Export(now, json, steamDir, console, hide, personal: personal));
            Log.Ok("Bug report saved: " + path);
            Toast(Loc.F("Bug report saved: {0}. Your home IP, API key, usernames, Steam names and IDs are left out; check the zip before you post it.", path), true);
            ShellLaunch.OpenAsUser(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"", null);
        }
        catch (Exception ex) { Log.Error("Save bug report: " + ex.Message); Toast(Loc.F("Couldn't save the bug report: {0}", ex.Message), Tone.Error); }
        finally { HideProgress(); }
    }

    /// <summary>
    /// Log > Report a problem (4 nov.): a new GitHub issue in the browser, with the app and Windows versions and the mode filled in.
    /// Nothing else goes in the link (no IPs, names, servers or log), and nothing is sent until you press Submit on GitHub.
    /// </summary>
    public static string IssueUrl(string app, string windows, string mode)
    {
        static string Safe(string s) => new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '+').Take(40).ToArray()) is { Length: > 0 } x ? x : "?";
        var body = "**What happened**\n\n\n**What you expected**\n\n\n**Steps to get there**\n1. \n\n"
            + $"**Setup**\n- App: {Safe(app)}\n- Windows: {Safe(windows)}\n- Mode: {Safe(mode)}\n\n"
            + "Log > Save bug report makes a .zip without your home IP, API key, usernames or Steam names. Check it, then drag it here if it helps.";
        return $"https://github.com/{Updater.Repo}/issues/new?title={Uri.EscapeDataString("Problem: ")}&body={Uri.EscapeDataString(body)}";
    }

    async void ReportProblem_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ask(Loc.T("Report a problem"), Loc.T("Opens a new issue on GitHub in your browser, with the app and Windows versions filled in. Nothing is sent until you "
                + "press Submit there (a GitHub account is needed).") + "\n\n" + Loc.T("To attach the log, use Save bug report first and check the .zip."), Loc.T("Open GitHub"))) return;
        if (_demo) { Toast(Loc.T("Demo: GitHub would open."), true); return; }
        OpenUrl(IssueUrl(AppInfo.Version.Split('+')[0], Environment.OSVersion.Version.ToString(), _s.Mode));
    }

    /// <summary>The moment of the export: version, Windows, adapters, the app's routes and rules, the last check and the paths.</summary>
    async Task<string> BuildReportText()
    {
        var sb = new StringBuilder();
        var rep = _last;
        sb.AppendLine($"CS2 IP Changer {AppInfo.Version} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Windows: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({Environment.OSVersion.Version}) · {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Options: mode={_s.Mode} game={_s.GameMode} cs2Locked={_s.LockCs2} steamOnEgress={_s.SteamOnEgress} pcOffPhone={_s.KeepPcOffPhone} offline={_s.SteamOffline} method={_s.ChangeMethod}");
        sb.AppendLine($"Steam: {Steam.InstallDir() ?? "not found"} · open={Steam.IsRunning} · CS2: {_s.Cs2Path ?? "not found"} · open={Steam.Cs2Running}");
        sb.AppendLine($"Phone: {_phone?.Summary ?? "none"} · ADB: {_adb ?? "not found"} · Phone public IP: {_egressPublic ?? "not measured"}");
        sb.AppendLine();
        sb.AppendLine("== Checks");
        foreach (var c in _checks) sb.AppendLine($"{(c.Ok == true ? "OK " : c.Ok == false ? "BAD" : " - ")} {c.Title}: {c.Detail}");
        if (rep != null)
        {
            sb.AppendLine();
            sb.AppendLine("== Last protection check");
            sb.AppendLine($"mode={rep.EffectiveMode} egress={rep.Egress?.Name} ({rep.Egress?.LocalIp}) candidate={rep.Candidate?.Name} ({rep.Candidate?.LocalIp}) problem={rep.EgressProblem} distrust={rep.DistrustReason}");
            sb.AppendLine($"firewall={rep.FirewallProblem ?? "on"} killSwitch={rep.KillSwitchOk} cs2={rep.Cs2LockOk} routes={rep.RouteOk.Values.Count(v => v)}/{rep.RouteOk.Count} steamRoutes={rep.SteamRoutesOk} steamOnEgress={rep.SteamOnEgress}");
            sb.AppendLine($"wholePc={rep.FullModeOk} (routes={rep.FullRoutesOk} dns={rep.DnsOk} ipv6={rep.Ipv6Ok}) pcDefault={rep.PcDefaultName} homeNetwork={rep.HasHome} error={rep.Error}");
        }
        sb.AppendLine();
        sb.AppendLine("== Adapters");
        foreach (var a in Net.Scan()) sb.AppendLine($"[{a.Index}] {a.Kind} · {a.Name} · {a.Description} · up={a.Up} · {a.LocalIp} gw {a.Gateway}{(a.IsWifi ? " · Wi-Fi " + (a.Ssid ?? "-") : "")}");
        sb.AppendLine();
        sb.AppendLine("== The app's routes");
        var ips = _servers.Select(s => s.Ip).Append(Net.EchoIp).ToHashSet();
        foreach (var r in await Net.ListRoutes() ?? new())
            if ((r.Prefix.EndsWith("/32") && ips.Contains(r.Prefix[..^3])) || Guard.SplitPrefixes.Contains(r.Prefix) || Net.ValvePrefixes.Contains(r.Prefix))
                sb.AppendLine($"{r.Prefix} if {r.Index} via {r.NextHop}");
        sb.AppendLine();
        sb.AppendLine("== The app's Firewall rules");
        var rules = Firewall.Remaining();
        if (rules.Error != null) sb.AppendLine(rules.ErrorText);
        foreach (var n in rules) sb.AppendLine(n);
        return sb.ToString();
    }
}
