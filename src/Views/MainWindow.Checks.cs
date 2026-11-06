using System.ComponentModel;
using System.Windows;
using SRT.Core;

namespace SRT;

// beta.11 (simulated users): the Checks panel shows only what needs you, with Show all; and a VPN app that is on gets its own row
public partial class MainWindow
{
    CheckItem _cVpn;
    bool _checksRefreshing;

    void HookChecksView()
    {
        foreach (var c in _checks) c.PropertyChanged += CheckChanged;
        _checks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (CheckItem c in e.NewItems) c.PropertyChanged += CheckChanged;
            RefreshChecksView();
        };
        RefreshChecksView();
    }

    void CheckChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CheckItem.Ok) or nameof(CheckItem.Waiting)) RefreshChecksView();
    }

    /// <summary>Hides the rows that are fine (unless Show all), and says in one line how many need you.</summary>
    void RefreshChecksView()
    {
        if (_checksRefreshing || ChecksSummary == null) return;
        _checksRefreshing = true;
        try
        {
            var all = _s?.ChecksShowAll == true;
            foreach (var c in _checks) c.Shown = ChecksView.Shows(c.Attention, all);
            ChecksSummary.Text = ChecksView.Summary(_checks.Count, _checks.Count(c => c.Ok == true && !c.Waiting), _checks.Count(c => c.Attention), _protOn || _s?.NoPhone == true);
            ChecksToggle.Content = ChecksView.Toggle(all);
        }
        finally { _checksRefreshing = false; }
    }

    void ChecksToggle_Click(object sender, RoutedEventArgs e)
    {
        _s.ChecksShowAll = !_s.ChecksShowAll;
        SaveSettings();
        RefreshChecksView();
    }

    /// <summary>Log > What the app changed: read now from the Firewall and the app's own list of routes. Copy gives the undo command.</summary>
    async void AppChanges_Click(object sender, RoutedEventArgs e)
    {
        List<Firewall.RuleInfo> rules;
        (List<string> Routes, int MetricIf) owned;
        if (_demo)
        {
            // The demo has no rules of its own: the ones protection makes for the demo servers
            var ips = string.Join(",", _servers.Select(s => s.Ip).Distinct());
            rules = new() { new(Firewall.RuleName, ips), new(Firewall.Cs2RuleName, "*"), new(Firewall.EchoRuleName, "198.51.100.200") };
            owned = (_servers.Select(s => s.Ip).Distinct().Select(ip => $"{ip}/32|21|192.168.42.129").ToList(), -1);
        }
        else
        {
            rules = await Task.Run(Firewall.Describe);
            try { owned = _guard.Owned(); } catch { owned = (new(), -1); }
        }
        var adapters = _last?.Adapters ?? new();
        // The rest of what stays on the PC (2nd round, Hugo: "no mention of the scheduled task or ProgramData")
        var other = new List<string> { $"App folder: {Uninstall.Root} (the phone tool and the app's unpacked files, only admins can change it)." };
        if (!_demo && Autostart.Allowed(Environment.ProcessPath ?? "") && await Autostart.IsOn(Environment.ProcessPath) == true)
            other.Add($"Start with Windows: the scheduled task \"{Autostart.TaskName}\".");
        var text = AppChanges.Text(rules, owned.Routes, owned.MetricIf, i => adapters.FirstOrDefault(a => a.Index == i)?.Name ?? (_demo && i == 21 ? "Phone (USB)" : null),
            Environment.ProcessPath ?? AppIds.ExeName, other);
        if (!await Ask("What the app changed", text, "Copy undo command", "Close")) return;
        try { Clipboard.SetText(AppChanges.RestoreCommand(Environment.ProcessPath ?? AppIds.ExeName)); Toast("Undo command copied.", true); }
        catch { Toast("Couldn't copy. Try again.", false); }
    }

    /// <summary>
    /// A VPN that carries the PC's traffic adds the row "VPN app" (amber), and it goes away when the VPN is off. The app wasn't tested with
    /// every VPN and its kill switch, so the row only asks to turn it off while you play.
    /// </summary>
    void UpdateVpnCheck(GuardReport rep)
    {
        var name = ChecksView.VpnOn(rep.Adapters, rep.PcDefaultIndex);
        if (name == null)
        {
            if (_cVpn != null) { _checks.Remove(_cVpn); _cVpn = null; RefreshChecksView(); }
            return;
        }
        if (_cVpn == null)
        {
            _cVpn = new CheckItem { Title = "VPN app", Waiting = true };
            _checks.Add(_cVpn);
            Log.Warn($"A VPN app is on ({name}). Turn it off while you play: the app wasn't tested with it.");
        }
        _cVpn.Detail = ChecksView.VpnDetail(name);
        _cVpn.Help = ChecksView.VpnHelpFor(rep.Unprotected);   // each report: protection can be turned on or off while the VPN stays
    }
}
