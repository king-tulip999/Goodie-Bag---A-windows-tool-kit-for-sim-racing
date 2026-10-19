using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace GoodieBag.Services;

internal static class CommandPipe
{
    public const string Name = "GoodieBag";
    private const int MaxLine = 512;

    private static CancellationTokenSource? _stop;

    public static string PowerShell(string profile) =>
        "$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', '" + Name + "', [System.IO.Pipes.PipeDirection]::Out); "
        + "$p.Connect(2000); $w = New-Object System.IO.StreamWriter($p); "
        + "$w.WriteLine('apply-window \"" + profile.Replace("'", "''").Replace("\"", "") + "\"'); $w.Flush(); $p.Dispose()";

    public static string? Parse(string line)
    {
        line = line.Trim();
        const string Verb = "apply-window";
        if (!line.StartsWith(Verb, StringComparison.OrdinalIgnoreCase) || line.Length == Verb.Length || !char.IsWhiteSpace(line[Verb.Length])) return null;
        string name = line[Verb.Length..].Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"') name = name[1..^1].Trim();
        return name.Length == 0 ? null : name;
    }

    public static void Start(Action<string> apply)
    {
        if (_stop is not null) return;
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        var thread = new Thread(() => Serve(apply, token)) { IsBackground = true, Name = "Goodie Bag command pipe" };
        thread.Start();
    }

    public static void Stop()
    {
        _stop?.Cancel();
        _stop = null;
    }

    private static NamedPipeServerStream Create()
    {
        var security = new PipeSecurity();
        if (WindowsIdentity.GetCurrent().User is { } user)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private static void Serve(Action<string> apply, CancellationToken token)
    {
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = Create();
                pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
                reading.CancelAfter(TimeSpan.FromSeconds(3));
                string line = ReadLine(pipe, reading.Token);
                if (Parse(line) is { } name) apply(name);
                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (++failures >= 5)
                {
                    App.LogError(ex);
                    return;
                }
            }
        }
    }

    private static string ReadLine(Stream pipe, CancellationToken token)
    {
        var bytes = new List<byte>();
        var buffer = new byte[128];
        while (bytes.Count < MaxLine)
        {
            int n = pipe.ReadAsync(buffer, token).AsTask().GetAwaiter().GetResult();
            if (n == 0) break;
            int end = Array.IndexOf(buffer, (byte)'\n', 0, n);
            bytes.AddRange(buffer.Take(end >= 0 ? end : n));
            if (end >= 0) break;
        }
        return Encoding.UTF8.GetString(bytes.Take(MaxLine).ToArray()).TrimStart('﻿');
    }
}
