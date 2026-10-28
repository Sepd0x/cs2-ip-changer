using System.Text;
using System.Text.Json;
using System.Windows;
using SRT.Core;

namespace SRT;

// Log > Export diagnostics: a .zip for a bug report, without the API key and the home IP
public partial class MainWindow
{
    async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { Toast("Demo: the .zip would go to the Desktop.", true); return; }
        ShowProgress("Collecting diagnostics…");
        try
        {
            var now = await BuildReportText();
            var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(_s));
            copy.SteamApiKey = null;
            copy.LastHomeIp = null;
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            var steamDir = Steam.InstallDir();
            var console = Cs2Console.LogPath(_s.Cs2Path ?? Steam.Cs2Path(_s));
            var hide = new[] { _s.LastHomeIp, _homePublic };
            var path = await Task.Run(() => Report.Export(now, json, steamDir, console, hide));
            Log.Ok("Diagnostics exported: " + path);
            Toast($"Diagnostics saved: {path}. Your home IP and API key are left out; check the zip before you post it.", true);
            ShellLaunch.OpenAsUser(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"", null);
        }
        catch (Exception ex) { Log.Error("Export diagnostics: " + ex.Message); Toast("Couldn't export: " + ex.Message, Tone.Error); }
        finally { HideProgress(); }
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
        sb.AppendLine($"Phone: {_phone?.Summary ?? "none"} · ADB: {_adb ?? "not found"} · IP servers see: {_egressPublic ?? "not measured"}");
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
        foreach (var r in await Net.ListRoutes())
            if ((r.Prefix.EndsWith("/32") && ips.Contains(r.Prefix[..^3])) || r.Prefix is "0.0.0.0/1" or "128.0.0.0/1" || Net.ValvePrefixes.Contains(r.Prefix))
                sb.AppendLine($"{r.Prefix} if {r.Index} via {r.NextHop}");
        sb.AppendLine();
        sb.AppendLine("== The app's Firewall rules");
        foreach (var n in Firewall.Remaining()) sb.AppendLine(n);
        return sb.ToString();
    }
}
