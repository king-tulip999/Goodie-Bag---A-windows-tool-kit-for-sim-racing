using System.Drawing;
using System.Windows.Forms;
using GoodieBag.Theme;

namespace GoodieBag.Services;

internal sealed class TrayService : IDisposable
{
    private readonly AppServices _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private Icon _image;
    private Font? _bold;

    private Font Bold => _bold ??= new Font(_menu.Font, FontStyle.Bold);

    public event Action<string?>? OpenRequested;
    public event Action? ExitRequested;

    public TrayService(AppServices app)
    {
        _app = app;
        _image = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "Goodie Bag (running in the background)",
            ContextMenuStrip = _menu,
        };
        Show();
        var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        again.Tick += (_, _) => { again.Stop(); if (!_disposed) { Safe(() => _icon.Visible = false); Show(); } };
        if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase)) again.Start();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke(null);
        };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke(null);
        _menu.Opening += (_, e) =>
        {
            Rebuild();
            e.Cancel = false;
        };
        _menu.ShowImageMargin = false;
        _menu.ShowCheckMargin = true;
        _menu.ShowItemToolTips = true;
        ThemeManager.Changed += RefreshIcon;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    private bool _disposed;

    private void Show() => Safe(() => _icon.Visible = true);

    private static void Safe(Action change)
    {
        try { change(); }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(RefreshIcon));

    private void RefreshIcon()
    {
        if (_disposed) return;
        var old = _image;
        _image = LoadIcon();
        Safe(() => _icon.Icon = _image);
        old.Dispose();
    }

    public void ShowBalloon(string title, string text, NoticeKind kind)
    {
        var icon = kind switch
        {
            NoticeKind.Warning => ToolTipIcon.Warning,
            NoticeKind.Error => ToolTipIcon.Error,
            _ => ToolTipIcon.Info,
        };
        Safe(() => _icon.ShowBalloonTip(3000, title, text, icon));
    }

    private void Rebuild()
    {
        var old = _menu.Items.Cast<ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old)
            item.Dispose();

        var open = new ToolStripMenuItem("Open Goodie Bag") { Font = Bold };
        open.Click += (_, _) => OpenRequested?.Invoke(null);
        _menu.Items.Add(open);
        if (_app.Updates.Available && _app.Updates.Latest is { } update)
            _menu.Items.Add(new ToolStripMenuItem("Install update", null, (_, _) => _ = _app.Updates.InstallAsync()) { Font = Bold, Enabled = _app.Updates.State != UpdateState.Downloading });
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var (key, title) in _app.NavPages().Where(p => p.Key != "settings"))
        {
            var k = key;
            _menu.Items.Add(new ToolStripMenuItem(title, null, (_, _) => OpenRequested?.Invoke(k)));
        }
        _menu.Items.Add(new ToolStripSeparator());

        if (_app.Plugins.IsActive(Plugins.PluginIds.Displays))
        {
            var profiles = _app.Config.Displays.Profiles;
            var activeProfile = _app.DisplayProfiles.Active;
            var menu = new ToolStripMenuItem("Display profile");
            foreach (var profile in profiles)
            {
                var target = profile;
                menu.DropDownItems.Add(new ToolStripMenuItem(profile.Name, null, (_, _) => _ = _app.DisplayProfiles.ApplyAsync(target)) { Checked = profile == activeProfile, ShortcutKeyDisplayString = GoodieBag.Hotkeys.Hotkey.Display(profile.Hotkey) });
            }
            if (profiles.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem("Create a profile…", null, (_, _) => OpenRequested?.Invoke("displays")) { ToolTipText = "Opens Displays, where profiles are made" });
            _menu.Items.Add(menu);
            _menu.Items.Add(new ToolStripMenuItem("Identify displays", null, (_, _) => Views.IdentifyDisplays.Show(_app)) { ToolTipText = "Shows each display's number on it for a few seconds" });
        }
        if (_menu.Items[^1] is not ToolStripSeparator) _menu.Items.Add(new ToolStripSeparator());

        int before = _menu.Items.Count;
        foreach (var plugin in _app.Plugins.Active)
            plugin.AddTrayItems(_menu.Items, _app);
        if (_menu.Items.Count > before)
            _menu.Items.Add(new ToolStripSeparator());

        var themes = new ToolStripMenuItem("Theme");
        foreach (var theme in Themes.All)
        {
            var id = theme.Id;
            if (id != Config.AppTheme.Custom)
            {
                themes.DropDownItems.Add(new ToolStripMenuItem(theme.Name, null, (_, _) => _app.SetTheme(id)) { Checked = ThemeManager.Current.Id == id });
                continue;
            }
            var oled = _app.Config.Ui.OledCustom;
            bool on = ThemeManager.Current.Id == id;
            var item = new ToolStripMenuItem(Themes.For(Config.AppTheme.Custom).Name) { Checked = on };
            item.DropDownItems.Add(new ToolStripMenuItem("Default", null, (_, _) => _app.UseOledColors(null)) { Checked = on && !oled.InUse });
            if (!oled.Colors.IsDefault)
                item.DropDownItems.Add(new ToolStripMenuItem(string.IsNullOrWhiteSpace(oled.Name) ? "Your custom colors" : $"Your custom colors ({oled.Name})", null, (_, _) => _app.UseOledColors("")) { Checked = on && oled.InUse });
            if (oled.Profiles.Count > 0) item.DropDownItems.Add(new ToolStripSeparator());
            foreach (var profile in oled.Profiles.ToList())
            {
                string name = profile.Name;
                item.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => _app.UseOledColors(name)));
            }
            themes.DropDownItems.Add(item);
        }
        _menu.Items.Add(themes);
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenRequested?.Invoke("settings")));
        var exit = new ToolStripMenuItem("Quit Goodie Bag") { ToolTipText = "Closes Goodie Bag. With fan control on, it asks whether a small helper keeps your fan curves running." };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        _menu.Items.Add(exit);
        TrayTheme.Apply(_menu);
    }

    internal static Icon? FanIcon(Config.AppTheme theme) => TileIcon("I.Fan", theme);

    internal static Icon? TileIcon(string glyphKey, Config.AppTheme theme)
    {
        try
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            var spec = Themes.For(theme);
            var p = ThemeManager.Resolve(spec);
            var tile = p["LogoBg"];
            var ink = p["LogoFg"];
            var glyph = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource(glyphKey);
            var path = System.Windows.Media.PathGeometry.CreateFromGeometry(glyph);
            if (glyphKey == "I.Bag" && size < 20 && path.Figures.Count > 2)
                path.Figures.RemoveAt(path.Figures.Count - 1);
            var bounds = path.Bounds;

            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double r = size * (spec.Radius >= 1 ? 0.24 : spec.Radius >= 0.5 ? 0.22 : 0.10);
                var face = new System.Windows.Rect(0, 0, size, size);
                if (spec.Glow)
                {
                    dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(p["LogoEdge"]), null, face, r, r);
                    double ring = Math.Max(1.5, size * 0.09);
                    face.Inflate(-ring, -ring);
                    dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(tile), null, face, Math.Max(0, r - ring), Math.Max(0, r - ring));
                }
                else
                    dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(tile), null, face, r, r);

                double stroke = Math.Max(1.6, size * 0.1);
                double scale = (size * 0.74 - stroke) / Math.Max(bounds.Width, bounds.Height);
                var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(ink), stroke / scale)
                {
                    StartLineCap = System.Windows.Media.PenLineCap.Round, EndLineCap = System.Windows.Media.PenLineCap.Round,
                    LineJoin = System.Windows.Media.PenLineJoin.Round,
                };
                dc.PushTransform(new System.Windows.Media.TranslateTransform(size / 2.0, size / 2.0));
                dc.PushTransform(new System.Windows.Media.ScaleTransform(scale, scale));
                dc.PushTransform(new System.Windows.Media.TranslateTransform(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2)));
                dc.DrawGeometry(null, pen, path);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var png = new System.IO.MemoryStream();
            encoder.Save(png);
            png.Position = 0;
            using var gdi = new Bitmap(png);
            IntPtr handle = gdi.GetHicon();
            try { return (Icon)Icon.FromHandle(handle).Clone(); }
            finally { DestroyIcon(handle); }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon LoadIcon() => TileIcon("I.Bag", ThemeManager.Current.Id) ?? (Icon)SystemIcons.Application.Clone();

    public void Dispose()
    {
        ThemeManager.Changed -= RefreshIcon;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _disposed = true;
        Safe(() => _icon.Visible = false);
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
        _bold?.Dispose();
    }
}
