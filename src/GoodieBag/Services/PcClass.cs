using System.Windows.Media;

namespace GoodieBag.Services;

internal static class PcClass
{
    public const int NormalFrameRate = 60;
    public const int WeakFrameRate = 30;
    public const int FewCores = 4;
    public const long LittleMemory = 6L << 30;

    public static bool SoftwareRendering => (RenderCapability.Tier >> 16) < 2;

    private static bool? _weak;

    public static bool Weak => _weak ??= Classify(RenderCapability.Tier >> 16, Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    public static bool Effects => !Weak && !SoftwareRendering;

    public static int FrameRate => Weak ? WeakFrameRate : NormalFrameRate;

    public static bool Classify(int renderTier, int cores, long memoryBytes) =>
        renderTier < 2 || cores <= FewCores || (memoryBytes > 0 && memoryBytes < LittleMemory);

    public static string Describe() =>
        $"render tier {RenderCapability.Tier >> 16}, {Environment.ProcessorCount} logical processors, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1 << 20)} MB memory: " +
        (Weak ? "lighter drawing (30 fps animations, no blur)" : "full drawing");
}
