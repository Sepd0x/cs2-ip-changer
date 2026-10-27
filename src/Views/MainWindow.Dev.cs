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

    /// <summary>Sidebar: the version on two lines (fits in 80 px); the full one goes in the tooltip.</summary>
    void ShowVersion()
    {
        var parts = AppInfo.Version.Split('+');
        VersionText.Text = "v" + parts[0].Replace("-", "\n") + (parts.Length > 1 ? "\n" + parts[1] : "");
        VersionText.ToolTip = "CS2 IP Changer " + AppInfo.Version
;
    }
}
