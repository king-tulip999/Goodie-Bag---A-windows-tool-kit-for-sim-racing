using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed record SearchEntry(string Title, string Description, string Category, string Page, string? Anchor = null, string Keywords = "", string? Keybind = null);

internal static class SettingsIndex
{
    public static readonly IReadOnlyList<SearchEntry> All = new SearchEntry[]
    {


        new("Displays", "Arrangement, resolution, refresh rate, rotation and main display.", "Displays", "displays", null, "monitor screen arrange"),
        new("Resolution and refresh rate", "Per monitor, with a keep-or-revert prompt.", "Displays", "displays", "displays.details", "hz fps resolution mode rotation orientation"),
        new("HDR", "Turn HDR on or off per monitor.", "Displays", "displays", "displays.details", "hdr high dynamic range"),
        new("Main display", "Which monitor has the taskbar and new windows.", "Displays", "displays", "displays.details", "primary"),
        new("Duplicate displays", "Show the same picture on two monitors, or give one its own desktop again.", "Displays", "displays", "displays.details", "duplicate clone mirror extend same picture"),
        new("Arrangement", "Drag monitors into place.", "Displays", "displays", "displays.arrange", "layout position triples"),
        new("Display profiles", "Saved monitor + audio setups with their own keybind (desk, sim rig…), applied in one click.", "Displays", "displays", "displays.profiles", "profile layout hotkey keybind preset"),
        new("Taskbar on one display", "A display profile can show the taskbar on the main display only.", "Displays", "displays", "displays.profiles", "taskbar multiple monitors main only hide"),
        new("Per-monitor wallpaper", "A display profile can set each monitor's wallpaper.", "Displays", "displays", "displays.profiles", "background wallpaper image"),
        new("Display profile audio", "A display profile can switch the speakers / headset, microphone and volume.", "Displays", "displays", "displays.profiles", "audio sound speaker headphones microphone volume profile"),

        new("Fan control", "Master switch: Goodie Bag drives your fans, or the BIOS / driver does.", "Fans", "fans", "fans.master", "fan enable on off"),
        new("Fan presets", "Named sets of fan settings (Default, Quiet, Racing…).", "Fans", "fans", "fans.presets", "fan profile silent quiet max cooling"),
        new("Fan curves", "Temperature-to-speed curves.", "Fans", "fans", "fans.curves", "fan curve temperature speed mix max min average sum subtract"),
        new("Fan failsafe", "Emergency temperature that sends every fan to 100%.", "Fans", "fans", "fans.failsafe", "fan emergency safety failsafe temperature"),
        new("Fans and calibration", "Each fan's mode, name, limits and calibration.", "Fans", "fans", "fans.fans", "fan rpm calibrate minimum maximum rename start stop step up down ramp limits"),
        new("Hidden fans", "Fans you've hidden from the page and overlay.", "Fans", "fans", "fans.hidden", "fan hide"),

        new("Hardware Monitor", "PC overview and every sensor with min / max.", "Hardware", "monitor", null, "sensors cpu gpu ram temperature clock power"),
        new("Network", "Download, upload, ping and packet loss.", "Hardware", "monitor", "monitor.network", "ping latency internet bandwidth"),
        new("Performance", "Live readings, load charts, FPS, 1% lows and the frame-time graph (Hardware Monitor).", "Hardware", "monitor", "monitor.performance", "fps frametime charts"),
        new("Show every tool's sections", "List the sections of every tool in the sidebar, not only the open tool's.", "Settings", "settings", "settings.sidebaropen", "sidebar sections open expand all tabs lists always"),
        new("Search settings", "Find any setting or keybind and jump to it: the search button beside Settings in the sidebar (Ctrl+F).", "Settings", "settings", "settings.sidebar", "search find settings ctrl+f lookup"),
        new("Sidebar", "Put the list of tools on the left or the right of the window. Drag tools, and the sections under the open tool, in the sidebar to reorder them (Alt+Up / Down with the keyboard).", "Settings", "settings", "settings.sidebar", "sidebar side left right navigation tabs menu rearrange reorder order move pages tools sections home landing"),
        new("Tool info", "What each tool reads, runs in the background and changes on your PC: the info icon beside each plugin.", "Settings", "settings", "settings.plugins", "info reads background changes privacy what does it do"),
        new("Arrange fans", "Drag the fan rows on Fan Control into a new order.", "Fans", "fans", "fans.presets", "move order drag rearrange fan cards"),
        new("Calibrate all fans", "Measure every fan that reports its speed, one after another.", "Fans", "fans", "fans.presets", "calibrate calibration measure rpm all fans batch"),
        new("Reset all fans", "Every fan back to its default name, limits and Auto in every preset (calibrations are kept).", "Fans", "fans", "fans.presets", "reset default defaults restore clear fan settings all"),
        new("Reset a fan to default", "The reset button on a fan's card: its name, limits and mode in every preset back to default.", "Fans", "fans", "fans.fans", "reset default defaults restore clear fan settings one single"),
        new("Update rate (Hardware Monitor)", "Low, Balanced or Fast on the Performance header: the same app-wide sensor update rate as Settings → Performance.", "Hardware", "monitor", "performance.rate", "polling rate refresh interval low medium high balanced fast update"),

        new("iRacing Overlay", "In-game widgets from iRacing's live telemetry: Relative, Standings, fuel, delta, alerts and more.", "Sim Racing Tools", "simracing", "simracing.hud.panel", "hud race hud overlay relative standings leaderboard fuel strategy out lap delta lap timing flags telemetry iracing widgets"),
        new("iRacing Overlay layouts", "Layouts of HUD widgets that switch by session, car, class or track; arrange them on screen.", "Sim Racing Tools", "simracing", "simracing.hud.panel", "hud layout race qualifying practice endurance multiclass arrange move resize snap rules auto switch"),
        new("iRacing Overlay alerts", "Fuel, pit speed, traffic, flags, incidents and best-lap alerts in groups you can fold and arrange, with thresholds and cooldowns.", "Sim Racing Tools", "simracing", "simracing.hud.panel", "alerts notifications fuel low pit limiter speeding blue flag yellow incident limit personal best groups arrange"),
        new("iRacing Overlay colors", "The overlay's look: a Goodie Bag theme or your own colors, background, corners; its keybinds.", "Sim Racing Tools", "simracing", "simracing.hud.panel", "hud theme custom colors colors look appearance background corners outline keybinds hotkeys hud settings"),
        new("Fuel formulas", "How the overlay works out fuel per lap, laps to go, fuel to the flag, stops and the pit window, with the numbers now.", "Sim Racing Tools", "simracing", "simracing.hud.panel", "fuel formula calculation per lap laps to go stops pit window estimate maths math"),
        new("FOV calculator", "Field of view for single or triple screens.", "Sim Racing Tools", "simracing", "simracing.fov", "fov triple angle curved bezel"),
        new("Compare FOV setups", "Overlay other screen setups on your rig: top-down view and numbers side by side; switch which one you're editing.", "Sim Racing Tools", "simracing", "simracing.compare", "compare triple 27 32 fov setups active edit"),
        new("Import / export iRacing screen setup", "Read your rig from iRacing, or write the calculator's FOV and triple setup back (the iRacing settings box under the calculator).", "Sim Racing Tools", "simracing", "simracing.fov.iracing", "iracing export import rendererDX11Monitor app.ini fov"),
        new("iRacing Controller", "Every iRacing setting explained and editable, a health check and backups.", "Sim Racing Tools", "simracing", "simracing.iracing", "iracing ini app.ini renderer graphics settings health backup"),
        new("iRacing Setup Manager", "Your iRacing car setups by car: search, rename, move, import, share packs for league mates and setups found in Downloads.", "Sim Racing Tools", "simracing", "simracing.setups", "setups sto setup files car garage share pack zip league downloads import export folders"),
        new("Fuel calculator", "Laps, stints, pit stops and fuel for a race, per car (under the Setup Manager).", "Sim Racing Tools", "simracing", "simracing.setups.fuel", "fuel calculator stint stops tank liters gallons race length laps"),
        new("Trading Paints", "Start the Trading Paints Downloader, with Goodie Bag or with iRacing.", "Sim Racing Tools", "simracing", "simracing.tradingpaints", "paints liveries downloader iracing launch"),
        new("Sim Racing Tools on / off", "Turn each Sim Racing tool on or off under Settings → Plugins; drag them in the sidebar to reorder.", "Settings", "settings", "settings.plugins", "panels hide show arrange order reorder layout tools sim racing"),
        new("iRacing keybinds", "iRacing's own keyboard keys, changed from Goodie Bag (iRacing Controller → Keybinds, or Settings → Keybinds).", "Sim Racing Tools", "simracing", "simracing.iracing", "iracing keys keybinds controls.cfg black box replay camera hotkeys"),
        new("Garage 61", "Start the Garage 61 Telemetry Agent, with Goodie Bag or with iRacing.", "Sim Racing Tools", "simracing", "simracing.garage61", "garage61 telemetry laps agent iracing launch data"),

        new("Start with Windows", "Start Goodie Bag when you sign in.", "Settings", "settings", "settings.startup", "startup boot login autostart background"),
        new("Start in background", "Start in the tray instead of opening the window.", "Settings", "settings", "settings.startup", "minimized tray hidden open background"),
        new("Application settings", "Startup and window behavior in one place: start with Windows, start in background, close to tray, keep on top, notifications, quit.", "Settings", "settings", "settings.app", "application startup window behavior behavior"),
        new("Window behavior", "Close to tray, keep on top, notifications, quit (Application settings).", "Settings", "settings", "settings.window", "tray minimize notifications toast close background quit"),
        new("Sensor update rate", "Fast, Balanced, Low or a custom interval: how often sensors are read for every page.", "Settings", "settings", "settings.sensorrate", "performance polling refresh interval rate hardware sensors fast balanced low custom ms cpu usage resource"),
        new("Lighter drawing while iRacing runs", "Draw the window without the graphics card and pause animations while the sim runs.", "Settings", "settings", "settings.light", "performance game racing lag slow stutter gpu software rendering animations iracing"),
        new("Background usage", "Standard or Reduced: read sensors less often while the window is in the tray or minimized.", "Settings", "settings", "settings.background", "performance resource usage background reduced standard minimized tray battery cpu"),
        new("Performance settings", "Sensor update rate and background usage for the whole app.", "Settings", "settings", "settings.performance", "performance resource usage speed polling"),
        new("Diagnostics", "Rescan hardware, refresh sensors, open logs, export a report, copy system information.", "Settings", "settings", "settings.diagnostics", "troubleshoot troubleshooting debug problem help support diagnostic"),
        new("Rescan hardware", "Detect temperature sensors, fans and fan controllers again.", "Settings", "settings", "settings.rescan", "rescan detect hardware fans sensors pwm controller missing reload"),
        new("Refresh sensors", "Read every sensor and fan now.", "Settings", "settings", "settings.diagnostics", "refresh sensors update now read fans"),
        new("Open logs", "Open the folder with error.log.", "Settings", "settings", "settings.logs", "log logs error errors error.log crash"),
        new("Export diagnostic report", "Save a troubleshooting report (no personal details) to share.", "Settings", "settings", "settings.report", "report diagnostic export support bug system info"),
        new("Copy system information", "Copy versions, CPU, GPU and sensor status to paste into a message.", "Settings", "settings", "settings.diagnostics", "copy clipboard system information specs cpu gpu windows version"),
        new("Export settings", "Save every setting to a file (backup, another PC).", "Settings", "settings", "settings.export", "export backup save settings configuration config file json transfer"),
        new("Import settings", "Load settings from an exported file (shows what's in it first; restarts).", "Settings", "settings", "settings.export", "import restore load settings configuration config file json transfer"),
        new("Settings file", "Where the settings are kept: open the folder.", "Settings", "settings", "settings.folder", "config.json folder appdata location path data configuration"),
        new("Keybinds", "Every keyboard shortcut in one list: window profiles, display profiles, fan presets, overlay, show / hide Goodie Bag, window shortcuts and iRacing's keys; arrange their order.", "Settings", "settings", "settings.keybinds", "hotkey hotkeys shortcut shortcuts keyboard keybind keybinds keys bindings ctrl+f ctrl+b window profile display profile fan preset iracing arrange order"),
        new("What is PawnIO?", "The optional driver for CPU temperatures and motherboard fans: what it does, safety, removing it.", "Settings", "settings", "settings.pawnio", "pawnio driver kernel safe anti-cheat winring0 admin administrator uninstall"),
        new("Reset Goodie Bag", "Back to defaults, as if freshly installed (a copy of your settings is kept).", "Settings", "settings", "settings.reset", "reset defaults factory start over clear wipe restore"),
        new("Updates", "About & updates: check for a new version, when to check, install it.", "Settings", "settings", "settings.updates", "update upgrade new version release download install check"),
        new("About Goodie Bag", "About & updates: what Goodie Bag is, update checks and the project page.", "Settings", "settings", "settings.about", "about version project page github info"),
        new("Open on", "The page Goodie Bag opens on when it starts.", "Settings", "settings", "settings.startpage", "start page startup page default page home first page open last page used"),
        new("Keep on top", "Keep the window above other windows.", "Settings", "settings", "settings.ontop", "always on top pin topmost float"),
        new("Theme", "Light, Dark, Custom (your own colors and name, or a preset such as Pastel), and GRR (with the GRR League plugin on); animations.", "Settings", "settings", "settings.look", "soft 2048 vanilla custom oled pastel dark mode light black color color animation appearance grr"),
        new("Custom colors", "Your own colors for the Custom theme: presets, saved sets, the theme's name.", "Settings", "settings", "settings.customcolors", "custom color color oled theme preset palette accent background name profile"),
        new("Topography background", "An animated contour-map background over the whole window instead of the track drawing.", "Settings", "settings", "settings.topo", "topography topo contour map terrain background lines animated wallpaper"),
        new("Topography line color", "The topography background's line color.", "Settings", "settings", "settings.topocolor", "topography topo contour line color colour background reset theme"),
        new("Interface size", "Scale text, cards and buttons: 80%, 90% or 100%.", "Settings", "settings", "settings.scale", "scaling zoom text size font bigger smaller dpi ui scale"),
        new("Hardware access", "Admin rights and sensor driver status.", "Settings", "settings", "settings.access", "admin driver"),
        new("Hardware Monitor sections", "Turn Hardware Monitor's sections on or off under Settings → Plugins; drag them in the sidebar to reorder.", "Settings", "settings", "settings.plugins", "hide show sort arrange order panels cards sections cpu gpu memory sensors overlay"),
        new("Plugins", "Turn optional tools on or off.", "Settings", "settings", "settings.plugins", "module feature enable plugin plugins tools on off"),
    };

    public static IEnumerable<SearchEntry> With(IEnumerable<Plugins.Plugin> active) =>
        All.Concat(active.SelectMany(p =>
        {
            try { return p.SearchEntries.ToList(); }
            catch (Exception ex) { App.LogError(ex); return new List<SearchEntry>(); }
        }));

    public static IEnumerable<SearchEntry> Keybinds(IEnumerable<Services.HotkeyEntry> entries) =>
        entries.Select(e =>
        {
            string key = GoodieBag.Hotkeys.Hotkey.Display(e.Get());
            string where = string.Join(" › ", e.Path);
            return new SearchEntry(e.Label, (key.Length > 0 ? key : "Not set") + " · " + where, "Keybinds", "settings", "settings.keybinds",
                "keybind hotkey shortcut key " + where + " " + key + " " + e.Note, e.Label);
        });

    public static List<SearchEntry> Find(string query, Func<SearchEntry, bool> available, int max = 8) => Find(All, query, available, max);

    public static List<SearchEntry> Find(IEnumerable<SearchEntry> entries, string query, Func<SearchEntry, bool> available, int max = 8)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return new();
        var results = new List<(SearchEntry Entry, int Score)>();
        foreach (var e in entries)
        {
            if (!available(e))
                continue;
            string title = e.Title.ToLowerInvariant(), keys = e.Keywords.ToLowerInvariant(),
                desc = e.Description.ToLowerInvariant(), cat = e.Category.ToLowerInvariant();
            int score = 0;
            foreach (var w in words)
            {
                int s = title.StartsWith(w) ? 100 : title.Contains(w) ? 60 : keys.Contains(w) ? 35 : cat.Contains(w) ? 15 : desc.Contains(w) ? 10 : 0;
                if (s == 0) { score = 0; break; }
                score += s;
            }
            if (score > 0)
                results.Add((e, score - title.Length / 10));
        }
        return results.OrderByDescending(r => r.Score).Take(max).Select(r => r.Entry).ToList();
    }
}

internal sealed class HighlightAdorner : Adorner
{
    private readonly Pen _pen = new(Ui.Res<Brush>("Accent"), 2);
    private readonly Brush _fill;

    private HighlightAdorner(UIElement target) : base(target)
    {
        IsHitTestVisible = false;
        var soft = Ui.Res<Brush>("AccentSoft").Clone();
        soft.Opacity = 0.35;
        _fill = soft;
    }

    protected override void OnRender(DrawingContext dc) =>
        dc.DrawGeometry(_fill, _pen, Tile.Shape(new Rect(new Point(-6, -4), new Size(AdornedElement.RenderSize.Width + 12, AdornedElement.RenderSize.Height + 8)), ThemeManager.Round(10)));

    public static void Flash(FrameworkElement target)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
            return;
        var adorner = new HighlightAdorner(target);
        layer.Add(adorner);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(1400) };
        fade.Completed += (_, _) => layer.Remove(adorner);
        adorner.BeginAnimation(OpacityProperty, fade);
    }
}
