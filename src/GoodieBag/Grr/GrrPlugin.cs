using GoodieBag.Config;
using GoodieBag.Grr.Views;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Views;

namespace GoodieBag.Grr;

internal sealed class GrrPlugin : Plugin
{
    public const string PluginId = "grr";
    public const string PageKey = "grr";

    public override string Id => PluginId;
    public override string Name => "GRR League";
    public override string Description => "Your GRR league's next race, championship standings, results and schedule from the GRR website.";
    public const string LogoIcon = "F.GrrLogo";

    public override string Icon => LogoIcon;
    public override (string Soft, string Deep) Colors => ("SageSoft", "SageDeep");
    public override int Order => 70;
    public const bool OnByDefault = false;
    public override bool EnabledByDefault => OnByDefault;

    public override string Reads =>
        "The GRR website's league data (schedule, standings, results; its public league feeds) while the GRR League page is on screen; read again when it's over 6 hours old, or when you press Refresh. The last read is kept in Goodie Bag's settings folder (grr), so it shows at once and offline.";
    public override string BackgroundWork =>
        "None: it reads only while its page is on screen.";
    public override string Changes => "Nothing on your PC; it only reads the GRR website.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PageKey, "GRR League", LogoIcon, (app, window) => new GrrPage(app, window), Sections: GrrPage.SectionsFor),
    };

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("GRR League dashboard", "Your league's next race, championship standings, results and schedule from the GRR website, and your driver profile.", "GRR League", PageKey, GrrParts.Anchor, "grr league standings championship results schedule points driver profile cup gt indycar refresh"),
    };

    public override IReadOnlyCollection<int>? IracingLeagueIds(AppServices app) =>
        app.Demo && app.Config.Grr.IracingLeagueIds.Count == 0 ? new[] { SessionStats.StatsDemo.LeagueId } : app.Config.Grr.IracingLeagueIds.ToArray();

    private GrrService? _service;

    public GrrService Service(AppServices app) => _service ??= new GrrService(app.Demo);

    protected override void OnStart(AppServices app)
    {
        Themes.Register(GrrTheme.Spec);
        var settings = app.Config.Grr;
        if (app.Demo && !app.Config.Ui.SetupPending && app.Config.Extra?.ContainsKey(GrrAccess.Section) != true)
        {
            settings.Leagues = new() { "cup", "gt", "indycar" };
            settings.DriverName = "Sam Driver";
        }
    }

    protected override void OnStop(AppServices app)
    {
        if (!app.Plugins.IsEnabled(this))
        {
            if (ThemeManager.Current.Id == GrrTheme.Id) app.SetTheme(AppTheme.Dark);
            Themes.Unregister(GrrTheme.Id);
        }
    }
}

internal static class GrrAccess
{
    public const string Section = "grr";

    extension(AppServices app)
    {
        public GrrService Grr => (app.Plugins.Find(GrrPlugin.PluginId) as GrrPlugin ?? throw new InvalidOperationException("The GRR League plugin isn't loaded.")).Service(app);
    }

    extension(AppConfig config)
    {
        public GrrSettings Grr
        {
            get
            {
                var s = config.Section<GrrSettings>(Section);
                if (!s.Normalized) s.Normalize();
                return s;
            }
        }
    }
}
