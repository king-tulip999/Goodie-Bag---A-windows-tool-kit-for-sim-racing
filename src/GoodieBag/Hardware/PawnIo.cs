using System.IO;
using Microsoft.Win32;

namespace GoodieBag.Hardware;

internal enum PawnIoState { Installed, RemovalPending, Missing }

internal static class PawnIo
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

    public static PawnIoState Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKey);
            if (key is null)
                return PawnIoState.Missing;
            return Classify(key.GetValue("ImagePath") as string, key.GetValue("DeleteFlag") is int flag && flag != 0, File.Exists);
        }
        catch
        {
            return PawnIoState.Missing;
        }
    }

    internal static PawnIoState Classify(string? imagePath, bool deleteFlag, Func<string, bool> fileExists)
    {
        if (deleteFlag)
            return PawnIoState.RemovalPending;
        string? path = DriverPath(imagePath);
        return path is null || !fileExists(path) ? PawnIoState.Missing : PawnIoState.Installed;
    }

    internal static string? DriverPath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;
        string path = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            path = path[4..];
        else if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path[12..]);
        else if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
        return path;
    }
}
