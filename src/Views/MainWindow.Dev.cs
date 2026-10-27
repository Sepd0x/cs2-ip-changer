using SRT.Core;

namespace SRT;

// Ganchos da build de desenvolvimento (diagnóstico, demonstração e capturas). A build normal não os
// implementa e o compilador tira as chamadas.
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

    /// <summary>Barra lateral: a versão em duas linhas (cabe nos 80 px); a completa fica na dica.</summary>
    void ShowVersion()
    {
        var parts = AppInfo.Version.Split('+');
        VersionText.Text = "v" + parts[0].Replace("-", "\n") + (parts.Length > 1 ? "\n" + parts[1] : "");
        VersionText.ToolTip = "ServerRouteTool " + AppInfo.Version;
    }
}
