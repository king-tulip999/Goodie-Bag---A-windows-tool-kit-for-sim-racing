using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GoodieBag.Processes;

internal static class ProcessLauncher
{
    public static string? Launch(string target, string? arguments = null, bool asAdmin = false)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (target.Length == 0)
            return "Nothing to launch.";
        bool isUrl = target.Contains("://", StringComparison.Ordinal);
        if (!isUrl && !File.Exists(target))
            return $"{Path.GetFileName(target)} wasn't found at {target}.";

        string? dir = isUrl ? null : Path.GetDirectoryName(target);
        if (!GoodieBag.Services.External.Allowed("start " + target)) return "Blocked during an automated run.";
        try
        {
            if (!asAdmin && Environment.IsPrivilegedProcess)
            {
                int error = 0;
                bool hasArguments = !string.IsNullOrWhiteSpace(arguments);
                bool exe = !isUrl && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe && TryLaunchAsDesktopUser(target, arguments, dir, out error))
                    return null;
                if (!exe && hasArguments)
                {
                    bool script = !isUrl && (target.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
                    string line = script ? $"/c \"\"{target}\" {arguments}\"" : $"/c start \"\" \"{target}\" {arguments}";
                    if (TryLaunchAsDesktopUser(Path.Combine(Environment.SystemDirectory, "cmd.exe"), line, dir, out error, script ? 0 : CreateNoWindow))
                        return null;
                }
                if (!hasArguments)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false })?.Dispose();
                    return null;
                }
                if (error is not (NoShellToken or ErrorElevationRequired))
                    return $"{Path.GetFileName(target)} couldn't be started with normal rights ({new Win32Exception(error).Message}). Tick Admin to start it elevated.";
            }
            Process.Start(new ProcessStartInfo(target, arguments ?? "") { UseShellExecute = true, WorkingDirectory = dir ?? "" })?.Dispose();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Canceled at the Windows permission prompt.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static async Task<int> CloseAsync(string nameOrPath, bool force = false, int graceMs = 4000)
    {
        string name = AppWatcher.Normalize(nameOrPath);
        var all = Process.GetProcessesByName(name);
        var processes = all.Where(p => SessionOf(p) == CurrentSession).ToArray();
        foreach (var other in all.Except(processes)) other.Dispose();
        if (processes.Length == 0)
            return 0;
        try
        {
            foreach (var p in processes)
            {
                try { p.CloseMainWindow(); } catch { }
            }
            if (force)
            {
                using (var grace = new CancellationTokenSource(graceMs))
                    await Task.WhenAll(processes.Select(p => ExitedAsync(p, grace.Token)));
                foreach (var p in processes)
                {
                    try
                    {
                        if (!p.HasExited)
                            p.Kill(entireProcessTree: true);
                    }
                    catch { }
                }
            }
            return processes.Length;
        }
        finally
        {
            foreach (var p in processes)
                p.Dispose();
        }
    }

    private static int SessionOf(Process p)
    {
        try { return p.SessionId; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return -2; }
    }

    private static readonly int CurrentSession = Process.GetCurrentProcess().SessionId;

    private static async Task ExitedAsync(Process process, CancellationToken grace)
    {
        try
        {
            await process.WaitForExitAsync(grace);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            try { await Task.Delay(Timeout.Infinite, grace); } catch (OperationCanceledException) { }
        }
    }

    private static bool TryLaunchAsDesktopUser(string exe, string? arguments, string? dir, out int error, uint creationFlags = 0)
    {
        error = 0;
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            error = NoShellToken;
            return false;
        }
        GetWindowThreadProcessId(shell, out uint pid);

        IntPtr process = IntPtr.Zero, token = IntPtr.Zero, primary = IntPtr.Zero;
        try
        {
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero || !OpenProcessToken(process, TOKEN_DUPLICATE, out token))
            {
                error = NoShellToken;
                return false;
            }
            const uint access = TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;
            if (!DuplicateTokenEx(token, access, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary))
            {
                error = NoShellToken;
                return false;
            }

            var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            char[] commandLine = ($"\"{exe}\"" + (string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments) + "\0").ToCharArray();
            if (!CreateProcessWithTokenW(primary, 0, null, commandLine, creationFlags, IntPtr.Zero, dir, ref startup, out var info))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            return true;
        }
        finally
        {
            if (primary != IntPtr.Zero) CloseHandle(primary);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ErrorElevationRequired = 740, NoShellToken = -1;
    private const uint CreateNoWindow = 0x08000000;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x1, TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8, TOKEN_ADJUST_DEFAULT = 0x80, TOKEN_ADJUST_SESSIONID = 0x100;
    private const int SecurityImpersonation = 2, TokenPrimary = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? applicationName, [In, Out] char[] commandLine,
        uint creationFlags, IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);
}
