using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace SRT.Core;

/// <summary>
/// Start with Windows (Settings, off by default): a scheduled task that opens the app in the notification area when you sign in,
/// with its admin rights and without a UAC prompt each time. Only for the copy installed in C:\Program Files\CS2 IP Changer:
/// a task that starts an exe from a folder other programs can change would let them run their own as admin.
/// </summary>
public static class Autostart
{
    public const string TaskName = "CS2 IP Changer";

    /// <summary>Only the installed copy may start with Windows.</summary>
    public static bool Allowed(string exe, string target = null) =>
        exe != null && Installer.SameDir(Path.GetDirectoryName(exe), target ?? Installer.Target);

    /// <summary>
    /// The task: when this Windows user signs in (15 s later, so the network is up), the exe with --tray, with the highest rights the
    /// user has. On battery too, never stopped after a time limit, and a second start while one runs is ignored. It can't be started
    /// on demand ("schtasks /Run"): otherwise any program of yours could start the app as admin without the UAC prompt.
    /// </summary>
    public static string TaskXml(string exe, string userSid)
    {
        string X(string s) => SecurityElement.Escape(s);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Opens CS2 IP Changer in the notification area when you sign in. Made by the app: Settings &gt; Start with Windows turns it off.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{X(userSid)}</UserId>
                  <Delay>PT15S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{X(userSid)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowStartOnDemand>false</AllowStartOnDemand>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{X(exe)}"</Command>
                  <Arguments>{AppIds.Args.Tray}</Arguments>
                  <WorkingDirectory>{X(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>The exe a task XML starts (quotes taken off), or null.</summary>
    public static string CommandIn(string xml)
    {
        var m = Regex.Match(xml ?? "", @"<Command>\s*([^<]*?)\s*</Command>");
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim('"') : null;
    }

    /// <summary>
    /// The task XML starts <paramref name="exe"/> for this Windows user: the task's name is the same for every user of the PC, so
    /// another user turning it on replaces yours. Windows writes the user as the SID or as PC\name, so either counts.
    /// </summary>
    public static bool IsOnFor(string xml, string exe, string sid, string account)
    {
        if (!string.Equals(CommandIn(xml), exe, StringComparison.OrdinalIgnoreCase)) return false;
        var users = Regex.Matches(xml ?? "", @"<UserId>\s*([^<]*?)\s*</UserId>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        var shortName = account?[(account.LastIndexOf('\\') + 1)..];
        return users.All(u => string.Equals(u, sid, StringComparison.OrdinalIgnoreCase) || string.Equals(u, account, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u, shortName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The task is there and starts this exe for this Windows user. Null when Windows couldn't be asked.</summary>
    public static async Task<bool?> IsOn(string exe)
    {
        var r = await Proc.Run("schtasks", $"/Query /TN \"{TaskName}\" /XML", 10000);
        if (r.Code < 0) return null;
        using var me = WindowsIdentity.GetCurrent();
        return r.Code == 0 && IsOnFor(r.Output, exe, me.User?.Value, me.Name);
    }

    /// <summary>Makes (or remakes) the task for this exe and checks it is there. Null = done, else what went wrong.</summary>
    public static async Task<string> TurnOn(string exe)
    {
        if (!Allowed(exe)) return "Only the copy installed in " + Installer.Target + " can start with Windows.";
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid == null) return "Couldn't read your Windows user.";
        // The XML goes through an admin-only folder: schtasks reads it from a file, and nobody else may change it in between.
        var root = AppIds.ProgramDataDir;
        var dir = Path.Combine(root, "task");
        var file = Path.Combine(dir, "task.xml");
        try
        {
            Adb.SafeDir(root);
            Adb.SafeDir(dir);
            File.WriteAllText(file, TaskXml(exe, sid), Encoding.Unicode);
            var r = await Proc.Run("schtasks", $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F", 15000);
            if (r.Code != 0) return "Windows didn't make the task: " + Short(r.Output);
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { File.Delete(file); } catch { } }
        return await IsOn(exe) == true ? null : "The task was made but Windows doesn't show it starting this app.";
    }

    /// <summary>Deletes the task if it's there. Null = gone (or never was), else what went wrong.</summary>
    public static async Task<string> TurnOff()
    {
        var q = await Proc.Run("schtasks", $"/Query /TN \"{TaskName}\"", 10000);
        if (q.Code == 1) return null;   // not there
        var r = await Proc.Run("schtasks", $"/Delete /TN \"{TaskName}\" /F", 10000);
        return r.Code == 0 ? null : "Windows didn't delete the task \"" + TaskName + "\": " + Short(r.Output);
    }

    static string Short(string s) => (s ?? "").Trim().Split('\n')[0].Trim();
}
