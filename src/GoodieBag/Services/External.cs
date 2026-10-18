namespace GoodieBag.Services;

internal static class External
{
    public static Func<string, bool>? Gate { get; set; }

    public static bool Allowed(string what) => Gate?.Invoke(what) ?? true;
}
