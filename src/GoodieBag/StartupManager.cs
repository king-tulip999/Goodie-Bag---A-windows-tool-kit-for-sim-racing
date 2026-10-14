using System.IO;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace GoodieBag;

internal sealed record StartupState(bool Enabled, string? Command)
{
    public bool PointsElsewhere => Enabled && !string.IsNullOrWhiteSpace(Command) && Environment.ProcessPath is { } exe &&
                                   !string.Equals(Path.GetFullPath(Command), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
}

internal static class StartupManager
{
    private const string TaskName = "Goodie Bag";
    private const string LegacyName = "RigControl";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool _legacyRemoved;

    public static StartupState State()
    {
        if (Schtasks($"/Query /TN \"{TaskName}\" /XML", out string xml) != 0)
            return new StartupState(false, null);
        var match = System.Text.RegularExpressions.Regex.Match(xml, "<Command>(.*?)</Command>");
        string? command = match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim('"') : null;
        return new StartupState(true, command);
    }

    public static void SetEnabled(bool enabled)
    {
        RemoveLegacyRunEntry();

        if (!enabled)
        {
            Schtasks($"/Delete /TN \"{TaskName}\" /F", out _);
            return;
        }

        string xmlPath = Path.Combine(App.ScratchFolder(Environment.ProcessPath ?? AppContext.BaseDirectory), $"GoodieBag-task-{Environment.ProcessId}.xml");
        File.WriteAllText(xmlPath, TaskXml(), Encoding.Unicode);
        try
        {
            if (Schtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", out string output) != 0)
                throw new InvalidOperationException(output.Trim());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string TaskXml()
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string exe = SecurityElement.Escape(Environment.ProcessPath ?? "");
        string dir = SecurityElement.Escape(AppContext.BaseDirectory);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Goodie Bag when you sign in.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--minimized</Arguments><WorkingDirectory>{dir}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
    }

    private static void RemoveLegacyRunEntry()
    {
        if (_legacyRemoved)
            return;
        _legacyRemoved = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            key?.DeleteValue(LegacyName, throwOnMissingValue: false);
        }
        catch { }

        try { Schtasks($"/Delete /TN \"{LegacyName}\" /F", out _); } catch { }
    }

    private static int Schtasks(string arguments, out string output)
    {
        var info = new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(); } catch { }
            output = "schtasks didn't respond.";
            return -1;
        }
        output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
