using SRT.Core;

namespace SRT;

// Hooks for the development build (diagnostics, demo and screenshots). The normal build doesn't
// implement them and the compiler removes the calls.
public partial class MainWindow
{
    partial void ParseDevArgs(string[] args);
    partial void InitDev();
    partial void StartupDev();
    partial void AfterStartupDev();
    partial void LoadDemo();
    /// <summary>The demo's Status after Turn on / Turn off protection (no Firewall in the demo).</summary>
    partial void DemoProtection(bool on);
    partial void AddDefaultServers(List<(string Ip, int Port)> list);
    partial void ApplyDevSettings();
    partial void ReadDevSettings();
    partial void RegisterDevHotkeys();
    partial void OnDevHotkey(int id, ref bool handled);
    partial void UnregisterDevHotkeys();
    partial void DiagReport(GuardReport rep);
    partial void DiagPhone(PhoneInfo info);
    partial void DiagIps(string egress, string home, bool same);
    partial void DiagTick();
    partial void UpdateDiagInfo();

    /// <summary>Sidebar: the version on one line in mono at the bottom (26 Nov, 168 px wide); the full one goes in the tooltip.</summary>
    void ShowVersion()
    {
        var parts = AppInfo.Version.Split('+');
        VersionText.Text = "v" + parts[0].Replace("-", " ") + (parts.Length > 1 ? "\n" + parts[1] : "");
        VersionText.ToolTip = "CS2 IP Changer " + AppInfo.Version
;
    }
}
