using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using GoodieBag.Config;

namespace GoodieBag.Services;

internal sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, string? ExeUrl, long ExeSize, string? ChecksumUrl, DateTime? Published = null);

internal enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed, NotConfigured }

internal sealed class UpdateService
{
    public const string Repository = "king-tulip999/Goodie-Bag---A-windows-tool-kit-for-sim-racing";

    public static string ProjectPage => $"https://github.com/{Repository}";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

    private readonly AppServices _app;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private static HttpClient? _http;
    private static HttpClient Http => _http ??= CreateClient();

    public UpdateService(AppServices app)
    {
        _app = app;
        _timer.Tick += (_, _) => { if (Due) _ = CheckAsync(manual: false); };
    }

    public UpdateState State { get; private set; } = Repository.Length == 0 ? UpdateState.NotConfigured : UpdateState.Idle;
    public ReleaseInfo? Latest { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }

    public event Action? Changed;

    public static Version Current { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public bool Available => Latest is { } latest
        && (State is UpdateState.Available or UpdateState.Downloading || State == UpdateState.Checking && latest.Version > Current);

    public static bool CanSelfInstall => Environment.ProcessPath is not null && !File.Exists(Path.Combine(AppContext.BaseDirectory, "GoodieBag.dll"));

    private UpdateSettings S => _app.Config.Updates;

    private bool Due => S.Check switch
    {
        UpdateCheck.Daily => S.LastCheckUtc is not { } d1 || DateTime.UtcNow - d1 >= TimeSpan.FromDays(1),
        UpdateCheck.Weekly => S.LastCheckUtc is not { } d7 || DateTime.UtcNow - d7 >= TimeSpan.FromDays(7),
        _ => false,
    };

    public void Start()
    {
        if (Environment.GetCommandLineArgs().Contains("--updated", StringComparer.OrdinalIgnoreCase))
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                _app.Notify("Updated", "Goodie Bag installed the new version and restarted.", NoticeKind.Success)));
        if (Environment.GetCommandLineArgs().Contains("--update-failed", StringComparer.OrdinalIgnoreCase))
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                _app.Notify("Update didn't install", "The file couldn't be replaced (in use, or no permission), so this version kept running. You can download it from the release page instead.",
                    NoticeKind.Warning, important: true)));
        if (_app.Demo || Repository.Length == 0) return;
        ApplySchedule();
        if (S.Check == UpdateCheck.OnStart || Due)
        {
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            delay.Tick += (_, _) => { delay.Stop(); _ = CheckAsync(manual: false); };
            delay.Start();
        }
    }

    public void ApplySchedule()
    {
        if (S.Check is UpdateCheck.Daily or UpdateCheck.Weekly && Repository.Length > 0 && !_app.Demo) _timer.Start();
        else _timer.Stop();
    }

    public async Task CheckAsync(bool manual)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        if (Repository.Length == 0)
        {
            Set(UpdateState.NotConfigured);
            return;
        }
        Set(UpdateState.Checking);
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=30");
            Latest = Newest(json);
            S.LastCheckUtc = DateTime.UtcNow;
            _app.Save();
            if (Latest is not null && Latest.Version > Current)
            {
                Set(UpdateState.Available);
                if (manual || S.NotifiedVersion != Latest.Tag)
                {
                    S.NotifiedVersion = Latest.Tag;
                    _app.Notify("Update available", "Install it from the title bar or Settings → About & updates.", NoticeKind.Info, important: true);
                }
            }
            else
            {
                Set(UpdateState.UpToDate);
                if (manual) _app.Notify("You're up to date", "You have the latest version of Goodie Bag.", NoticeKind.Success);
            }
        }
        catch (Exception ex)
        {
            Error = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? "The release page couldn't be found." : ex.Message;
            bool stillAvailable = !manual && Latest is { } known && known.Version > Current;
            Set(stillAvailable ? UpdateState.Available : UpdateState.Failed);
            if (manual) _app.Notify("Couldn't check for updates", Error, NoticeKind.Warning);
        }
    }

    public async Task InstallAsync()
    {
        if (Latest is not { } release || State is UpdateState.Downloading or UpdateState.Checking) return;
        if (_app.Demo || release.ExeUrl is null || release.ChecksumUrl is null || !CanSelfInstall)
        {
            OpenReleasePage();
            return;
        }
        var current = Environment.ProcessPath!;
        var download = Path.Combine(App.ScratchFolder(current), $"GoodieBag-{Show(release.Version)}.download");
        try
        {
            Set(UpdateState.Downloading);
            Progress = 0;
            using (var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? release.ExeSize;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(download);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                using var stall = new CancellationTokenSource(StallTimeout);
                while ((read = await ReadOrStall(source, buffer, stall)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0 && (int)(100.0 * done / total) != (int)(100 * Progress))
                    {
                        Progress = (double)done / total;
                        Changed?.Invoke();
                    }
                }
            }
            var expected = (await Http.GetStringAsync(release.ChecksumUrl)).Trim().Split(' ', '\t')[0];
            await using (var check = File.OpenRead(download))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(check));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The download didn't match the release's checksum, so it wasn't installed.");
            }

            App.RelaunchAfterExit("--updated", SwapSteps(current, download), current);
        }
        catch (Exception ex)
        {
            try { File.Delete(download); } catch { }
            Error = ex.Message;
            Set(UpdateState.Available);
            _app.Notify("Update didn't install", ex.Message + " You can download it from the release page instead.", NoticeKind.Warning, important: true);
        }
    }

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> ReadOrStall(Stream source, byte[] buffer, CancellationTokenSource stall)
    {
        try { return await source.ReadAsync(buffer, stall.Token); }
        catch (OperationCanceledException) when (stall.IsCancellationRequested) { throw new IOException("The download stopped (no data for 30 seconds)."); }
    }

    internal static string[] SwapSteps(string current, string download)
    {
        string exe = App.BatchPath(current), old = App.BatchPath(current + ".old"), downloaded = App.BatchPath(download);
        string failed = $"(start \"\" {exe} --update-failed & del {downloaded} >nul 2>&1 & del \"%~f0\" & exit /b 1)";
        return new[]
        {
            "set /a tries=0",
            ":swap",
            $"move /y {exe} {old} >nul 2>&1 && goto copy",
            "set /a tries+=1",
            $"if %tries% lss 10 ({App.WaitASecond} & goto swap) else {failed}",
            ":copy",
            $"copy /y {downloaded} {exe} >nul || (move /y {old} {exe} >nul & {failed[1..^1]})",
            $"del {old} >nul 2>&1",
            $"del {downloaded} >nul 2>&1",
        };
    }

    public void OpenReleasePage()
    {
        if (Repository.Length > 0) Views.Ui.OpenUrl(Latest?.PageUrl ?? ReleasesPage);
    }

    public static string Show(Version v) => v.ToString(v.Revision > 0 ? 4 : 3);

    internal static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    internal static ReleaseInfo? Newest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Parse(doc.RootElement);
        return doc.RootElement.EnumerateArray()
            .Where(r => r.ValueKind == JsonValueKind.Object && !(r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True))
            .Select(Parse)
            .OfType<ReleaseInfo>()
            .OrderByDescending(r => r.Version)
            .ThenByDescending(r => r.Published)
            .FirstOrDefault();
    }

    private static ReleaseInfo? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagName) || tagName.ValueKind != JsonValueKind.String) return null;
        var tag = tagName.GetString() ?? "";
        if (!TryVersion(tag, out var version)) return null;
        string? exe = null, checksum = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            static string? Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var list = assets.EnumerateArray()
                .Select(a => (Name: Str(a, "name") ?? "", Url: Str(a, "browser_download_url"), Size: a.ValueKind == JsonValueKind.Object && a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0))
                .ToList();
            var pick = list.FirstOrDefault(a => a.Name.Equals("GoodieBag.exe", StringComparison.OrdinalIgnoreCase));
            if (pick.Url is not null)
            {
                exe = pick.Url;
                size = pick.Size;
                checksum = list.FirstOrDefault(a => a.Name.Equals(pick.Name + ".sha256", StringComparison.OrdinalIgnoreCase)).Url;
            }
        }
        DateTime? published = root.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var when) ? when : null;
        return new ReleaseInfo(version, tag, root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "",
            root.TryGetProperty("html_url", out var page) && page.ValueKind == JsonValueKind.String ? page.GetString() ?? ReleasesPage : ReleasesPage, exe, size, checksum, published);
    }

    internal static bool TryVersion(string tag, out Version version)
    {
        var digits = new string(tag.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (Version.TryParse(digits.Contains('.') ? digits : digits + ".0", out var v))
        {
            version = Normalize(v);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version v) =>
        v.Revision > 0 ? new(v.Major, v.Minor, Math.Max(0, v.Build), v.Revision) : new(v.Major, v.Minor, Math.Max(0, v.Build));

    internal void ShowForScreenshots(ReleaseInfo? release)
    {
        Latest = release;
        Set(release is null ? UpdateState.UpToDate : UpdateState.Available);
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GoodieBag/" + Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
