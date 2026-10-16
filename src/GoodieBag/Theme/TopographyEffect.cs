using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace GoodieBag.Theme;

internal sealed class TopographyEffect : ShaderEffect
{
    private static PixelShader? _shader;

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(TopographyEffect), 0);

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Point), typeof(TopographyEffect),
        new UIPropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(double), typeof(TopographyEffect),
        new UIPropertyMetadata(312.0, PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty ZProperty = DependencyProperty.Register(nameof(Z), typeof(double), typeof(TopographyEffect),
        new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(nameof(Line), typeof(Point4D), typeof(TopographyEffect),
        new UIPropertyMetadata(new Point4D(1, 1, 1, 1), PixelShaderConstantCallback(3)));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Point4D), typeof(TopographyEffect),
        new UIPropertyMetadata(new Point4D(0, 0, 0, 1), PixelShaderConstantCallback(4)));

    public static readonly DependencyProperty HazeProperty = DependencyProperty.Register(nameof(Haze), typeof(double), typeof(TopographyEffect),
        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(5)));

    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(nameof(Strength), typeof(double), typeof(TopographyEffect),
        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(6)));

    public TopographyEffect()
    {
        PixelShader = Shader();
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(SizeProperty);
        UpdateShaderValue(UnitProperty);
        UpdateShaderValue(ZProperty);
        UpdateShaderValue(LineProperty);
        UpdateShaderValue(BackgroundProperty);
        UpdateShaderValue(HazeProperty);
        UpdateShaderValue(StrengthProperty);
    }

    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public double Unit { get => (double)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public double Z { get => (double)GetValue(ZProperty); set => SetValue(ZProperty, value); }

    public Point4D Line { get => (Point4D)GetValue(LineProperty); set => SetValue(LineProperty, value); }

    public Point4D Background { get => (Point4D)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public double Haze { get => (double)GetValue(HazeProperty); set => SetValue(HazeProperty, value); }

    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }

    public static Point4D Vector(Color c) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0, 1);

    public static bool Available
    {
        get
        {
            if (!RenderCapability.IsPixelShaderVersionSupported(3, 0) || (RenderCapability.Tier >> 16) < 2) return false;
            try { return Shader() is not null; }
            catch (Exception ex) { App.LogError(ex); return false; }
        }
    }

    private static PixelShader Shader()
    {
        if (_shader is not null) return _shader;
        using var stream = typeof(TopographyEffect).Assembly.GetManifestResourceStream("Topography.ps")
            ?? throw new InvalidOperationException("Topography.ps is not embedded");
        var shader = new PixelShader();
        shader.SetStreamSource(stream);
        shader.Freeze();
        return _shader = shader;
    }
}
