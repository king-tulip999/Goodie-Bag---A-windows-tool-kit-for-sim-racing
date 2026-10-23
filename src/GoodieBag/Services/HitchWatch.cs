using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;

namespace GoodieBag.Services;

internal static class HitchWatch
{
    private static readonly FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Stack<(DispatcherOperation Op, long Start)> Running = new();
    private static readonly List<Hitch> Found = new();
    private static Func<string>? _context;
    private static double _thresholdMs;

    public sealed record Hitch(double Ms, string What, string Context);

    public static bool On { get; private set; }

    public static string? Note { get; set; }

    public static void Start(Dispatcher dispatcher, double thresholdMs, Func<string>? context = null)
    {
        if (On) return;
        On = true;
        _thresholdMs = thresholdMs;
        _context = context;
        dispatcher.Hooks.OperationStarted += (_, e) => Running.Push((e.Operation, Stopwatch.GetTimestamp()));
        dispatcher.Hooks.OperationCompleted += (_, e) => Finish(e.Operation);
        dispatcher.Hooks.OperationAborted += (_, e) => Finish(e.Operation);
    }

    private static long _lastFrame;
    private static readonly List<(double Ms, string What)> SinceFrame = new();
    private static TimeSpan _lastPause;
    private static int _lastGen2;

    public static void WatchFrames(bool on)
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
        _lastFrame = 0;
        if (on) System.Windows.Media.CompositionTarget.Rendering += OnFrame;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        var pause = GC.GetTotalPauseDuration();
        int gen2 = GC.CollectionCount(2);
        if (_lastFrame != 0 && Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds is var gap && gap >= _thresholdMs * 2 && gap < 60_000)
        {
            double gc = (pause - _lastPause).TotalMilliseconds;
            string what = (gc >= 1 ? $"frame gap, {gc:0} ms of it GC{(gen2 > _lastGen2 ? " (gen 2)" : "")}" : "frame gap")
                + (Math.Min(Environment.TickCount64, Views.Motion.PageSettlesAt) - Math.Max(Environment.TickCount64 - (long)gap, Views.Motion.PageAnimatesFrom) >= 50 ? ", during the page animation" : "");
            var inside = SinceFrame.OrderByDescending(o => o.Ms).Take(3).Select(o => $"{o.What} {o.Ms:0} ms").ToList();
            if (inside.Count > 0) what += "; inside: " + string.Join(", ", inside);
            lock (Found) Found.Add(new Hitch(gap, what, Context()));
        }
        SinceFrame.Clear();
        _lastFrame = now;
        _lastPause = pause;
        _lastGen2 = gen2;
    }

    private static void Finish(DispatcherOperation op)
    {
        while (Running.Count > 0)
        {
            var (top, start) = Running.Pop();
            if (!ReferenceEquals(top, op)) continue;
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            string? note = Note;
            Note = null;
            if (ms >= 5 && _lastFrame != 0 && SinceFrame.Count < 64) SinceFrame.Add((ms, NameOf(op) + (note is null ? "" : " (" + note + ")")));
            if (ms >= _thresholdMs && ms < 60_000)
                lock (Found) Found.Add(new Hitch(ms, NameOf(op) + (note is null ? "" : " (" + note + ")"), Context()));
            return;
        }
    }

    private static string Context()
    {
        try { return _context?.Invoke() ?? ""; }
        catch (InvalidOperationException) { return ""; }
    }

    internal static string NameOf(DispatcherOperation op)
    {
        var d = MethodField?.GetValue(op) as Delegate;
        if (d is SendOrPostCallback)
        {
            object? args = ArgsField?.GetValue(op);
            if (args is Delegate inner) d = inner;
            else if (StateMachineOf(args?.GetType()) is { } machine) return machine + " @" + op.Priority;
        }
        return (d is null ? "?" : Describe(d)) + " @" + op.Priority;
    }

    private static string? StateMachineOf(Type? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.Name.StartsWith("AsyncStateMachineBox", StringComparison.Ordinal))
                return DescribeType(t.GetGenericArguments()[^1]) + " (async)";
        return null;
    }

    private static readonly FieldInfo? ArgsField = typeof(DispatcherOperation).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static string Describe(Delegate d)
    {
        return StateMachineOf(d.Target?.GetType()) ?? Describe(d.Method);
    }

    private static string DescribeType(Type type)
    {
        string full = type.FullName ?? type.Name;
        int plus = full.IndexOf('+');
        string outer = plus > 0 ? full[..plus] : full;
        outer = outer[(outer.LastIndexOf('.') + 1)..];
        return outer + "." + Readable(type.Name, "");
    }

    internal static string Describe(MethodInfo method)
    {
        string type = method.DeclaringType?.FullName ?? "?";
        int plus = type.IndexOf('+');
        string outer = plus > 0 ? type[..plus] : type;
        outer = outer[(outer.LastIndexOf('.') + 1)..];
        return outer + "." + Readable(method.Name, plus > 0 ? type[(plus + 1)..] : "");
    }

    private static string Readable(string method, string nested)
    {
        string name = method.StartsWith('<') && method.IndexOf('>') is > 1 and var end ? method[1..end] : method;
        if (name.Length == 0 && nested.StartsWith('<') && nested.IndexOf('>') is > 1 and var close) name = nested[1..close];
        return name.Length > 0 ? name : method;
    }

    public static IReadOnlyList<Hitch> Take()
    {
        lock (Found)
        {
            var list = Found.ToList();
            Found.Clear();
            return list;
        }
    }

    public static IEnumerable<string> Report(IReadOnlyList<Hitch> hitches, int top = 40)
    {
        yield return $"{hitches.Count} UI-thread operations took {_thresholdMs:0} ms or more";
        foreach (var g in hitches.GroupBy(h => h.What).OrderByDescending(g => g.Max(h => h.Ms)))
            yield return $"  {g.Count(),4} × max {g.Max(h => h.Ms),6:0} ms  {g.Key}";
        yield return "";
        foreach (var h in hitches.OrderByDescending(h => h.Ms).Take(top))
            yield return $"{h.Ms,6:0} ms  {h.What}  [{h.Context}]";
    }
}
