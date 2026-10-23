using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using GoodieBag.Views;

namespace GoodieBag.Theme;

public static class TextCase
{
    public static readonly DependencyProperty VerbatimProperty = DependencyProperty.RegisterAttached(
        "Verbatim", typeof(bool), typeof(TextCase), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetVerbatim(DependencyObject d) => (bool)d.GetValue(VerbatimProperty);

    public static void SetVerbatim(DependencyObject d, bool value) => d.SetValue(VerbatimProperty, value);

    public static T Verbatim<T>(T element) where T : DependencyObject
    {
        SetVerbatim(element, true);
        return element;
    }

    public static ToolTip VerbatimTip(string text) => Verbatim(new ToolTip { Content = text });

    public static string Upper(string text) => Spaced(Capitals(text), joined: true);

    private static string Capitals(string text)
    {
        foreach (char ch in text)
            if (char.IsLower(ch)) return text.ToUpperInvariant();
        return text;
    }

    public const char Hair = ' ', Narrow = ' ';

    public static string Spaced(string text, bool joined)
    {
        int gaps = 0;
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] is Hair or Narrow) return text;
            gaps += Gaps(text[i - 1], text[i]);
        }
        if (gaps == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + gaps);
        sb.Append(text[0]);
        for (int i = 1; i < text.Length; i++)
        {
            for (int n = Gaps(text[i - 1], text[i]); n > 0; n--)
                sb.Append(joined ? Narrow : Hair);
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    private static int Gaps(char before, char at)
    {
        if (at == ' ') return before is not ' ' and not '\n' ? 2 : 0;
        if (char.IsLetter(before)) return char.IsLetter(at) || Mark(at) ? 1 : 0;
        return Mark(before) && char.IsLetter(at) ? 1 : 0;
    }

    private static bool Mark(char c) => c is not Hair and not Narrow && (char.IsPunctuation(c) || char.IsSymbol(c));

    public static string Plain(string text) => text.Contains(Hair) || text.Contains(Narrow) ? text.Replace(Hair.ToString(), "").Replace(Narrow.ToString(), "") : text;

    public static string UpperForGdi(string text) => Spaced(Capitals(text), joined: false);

    public static readonly DependencyProperty UpperProperty = DependencyProperty.RegisterAttached(
        "Upper", typeof(bool), typeof(TextCase), new PropertyMetadata(false, OnUpperChanged));

    public static bool GetUpper(DependencyObject d) => (bool)d.GetValue(UpperProperty);

    public static void SetUpper(DependencyObject d, bool value) => d.SetValue(UpperProperty, value);

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    private static void OnUpperChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Loaded -= OnLoaded;
        tb.Unloaded -= OnUnloaded;
        TextDescriptor.RemoveValueChanged(tb, OnText);
        if (e.NewValue is not true) return;
        tb.Loaded += OnLoaded;
        tb.Unloaded += OnUnloaded;
        if (tb.IsLoaded) OnLoaded(tb, null);
    }

    private static void OnLoaded(object sender, RoutedEventArgs? e)
    {
        var tb = (TextBlock)sender;
        TextDescriptor.RemoveValueChanged(tb, OnText);
        if (GetVerbatim(tb)) return;
        Apply(tb);
        TextDescriptor.AddValueChanged(tb, OnText);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => TextDescriptor.RemoveValueChanged((TextBlock)sender, OnText);

    private static void OnText(object? sender, EventArgs e)
    {
        if (sender is TextBlock tb) Apply(tb);
    }

    private static void Apply(TextBlock tb)
    {
        if (BindingOperations.IsDataBound(tb, TextBlock.TextProperty)) return;
        if (!HasInlines(tb) || tb.Inlines.Count <= 1)
        {
            string text = tb.Text;
            string upper = IsMono(tb.FontFamily) ? Capitals(Plain(text)) : Upper(text);
            if (upper != text) tb.SetCurrentValue(TextBlock.TextProperty, upper);
            return;
        }
        UpperRuns(tb.Inlines, IsMono(tb.FontFamily));
    }

    private static readonly DependencyProperty RawProperty = DependencyProperty.RegisterAttached("Raw", typeof(string), typeof(TextCase));

    public static void Set(TextBlock tb, string text)
    {
        if ((string?)tb.GetValue(RawProperty) == text) return;
        tb.SetValue(RawProperty, text);
        bool cased = GetUpper(tb) && !GetVerbatim(tb) && tb.IsLoaded && !BindingOperations.IsDataBound(tb, TextBlock.TextProperty);
        tb.Text = cased ? IsMono(tb.FontFamily) ? Capitals(Plain(text)) : Upper(text) : text;
    }

    private static readonly Dictionary<FontFamily, bool> Mono = new();

    private static bool IsMono(FontFamily family)
    {
        if (Mono.TryGetValue(family, out bool mono)) return mono;
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double Width(string s) => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 20, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
        if (Mono.Count >= 64) Mono.Clear();
        return Mono[family] = Math.Abs(Width("iiii") - Width("MMMM")) < 0.5;
    }

    internal static readonly System.Reflection.FieldInfo? ComplexContent =
        typeof(TextBlock).GetField("_complexContent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static bool HasInlines(TextBlock tb) => ComplexContent is null || ComplexContent.GetValue(tb) is not null;

    private static void UpperRuns(InlineCollection inlines, bool mono)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run run && !BindingOperations.IsDataBound(run, Run.TextProperty) && !GetVerbatim(run))
            {
                string upper = mono ? Capitals(Plain(run.Text)) : Upper(run.Text);
                if (upper != run.Text) run.Text = upper;
            }
            else if (inline is Span span) UpperRuns(span.Inlines, mono);
        }
    }
}

public sealed class CaseConverter : IValueConverter
{
    public bool Verbatim { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        string s => Verbatim ? s : ThemeManager.Case(s),
        Choice c => Verbatim ? c.Text : ThemeManager.Case(c.Text),
        _ => value,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}

public sealed class CaseTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }

    public DataTemplate? Plain { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is string or Choice ? (TextCase.GetVerbatim(container) ? Plain : Text) : null;
}
