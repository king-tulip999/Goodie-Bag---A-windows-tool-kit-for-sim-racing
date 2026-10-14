using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GoodieBag.Theme;

namespace GoodieBag.Services;

internal static class TrayTheme
{
    private static (ThemeSpec Theme, Renderer Renderer, Palette Colors)? _cached;

    public static void Apply(ToolStripDropDown menu)
    {
        if (_cached is not { } c || c.Theme != ThemeManager.Current)
        {
            var colors = new Palette();
            _cached = c = (ThemeManager.Current, new Renderer(colors), colors);
        }
        Style(menu, c.Renderer, c.Colors);
    }

    private static void Style(ToolStripDropDown menu, Renderer renderer, Palette colors)
    {
        menu.Renderer = renderer;
        menu.BackColor = colors.Surface;
        menu.ForeColor = colors.Text;
        menu.Padding = new Padding(4, 6, 4, 6);
        menu.Opened -= SquareCorners;
        menu.Opened += SquareCorners;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripSeparator && !string.IsNullOrEmpty(item.Text)) item.Text = ThemeManager.Current.Caps ? TextCase.UpperForGdi(item.Text) : item.Text;
            item.ForeColor = item.Enabled ? colors.Text : colors.Faint;
            item.Padding = item is ToolStripSeparator ? item.Padding : new Padding(2, 4, 2, 4);
            if (item is ToolStripMenuItem { HasDropDownItems: true } parent && parent.DropDown is ToolStripDropDownMenu drop)
            {
                drop.ShowImageMargin = false;
                drop.ShowCheckMargin = true;
                drop.ShowItemToolTips = true;
                Style(drop, renderer, colors);
            }
        }
    }

    private static void SquareCorners(object? sender, EventArgs e)
    {
        if (sender is not Control { IsHandleCreated: true } c) return;
        int square = 1;
        try { DwmSetWindowAttribute(c.Handle, 33, ref square, sizeof(int)); } catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class Palette
    {
        public readonly Color Surface = Of("Surface"), Hover = Of("Hover"), Line = Of("Line"), Text = Of("Text"),
            Muted = Of("TextMuted"), Faint = Of("TextFaint"), Accent = Of("Accent");

        private static Color Of(string key)
        {
            var c = ThemeManager.ColorOf(key, System.Windows.Media.Colors.Gray);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        private readonly Palette _c;

        public Renderer(Palette colors) : base(new Table(colors))
        {
            _c = colors;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_c.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_c.Line);
            var r = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_c.Hover);
            using var path = Rounded(r, 6);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool shortcut = e.Item is ToolStripMenuItem m && !string.IsNullOrEmpty(m.ShortcutKeyDisplayString) && e.Text == m.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? _c.Faint : shortcut ? _c.Muted : _c.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_c.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float x = r.X + r.Width / 2f, y = r.Y + r.Height / 2f;
            e.Graphics.DrawLines(pen, new[] { new PointF(x - 5, y), new PointF(x - 1.5f, y + 3.5f), new PointF(x + 5, y - 4) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? _c.Faint : _c.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(_c.Line);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class Table : ProfessionalColorTable
    {
        private readonly Palette _c;
        public Table(Palette colors) { _c = colors; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => _c.Surface;
        public override Color MenuBorder => _c.Line;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => _c.Hover;
        public override Color ImageMarginGradientBegin => _c.Surface;
        public override Color ImageMarginGradientMiddle => _c.Surface;
        public override Color ImageMarginGradientEnd => _c.Surface;
        public override Color SeparatorDark => _c.Line;
        public override Color SeparatorLight => _c.Surface;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
    }
}
