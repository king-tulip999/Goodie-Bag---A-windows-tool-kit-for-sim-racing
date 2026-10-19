using System.Windows;
using System.Windows.Controls;
using GoodieBag.Audio;
using GoodieBag.Services;

namespace GoodieBag.Views.Pages;

internal static class AudioPicker
{
    public static void Fill(AppServices app, ComboBox box, EDataFlow flow, string? selectedId, string? selectedName)
    {
        box.Items.Add(new Choice(null, "Don't change"));
        foreach (var d in Devices(app, flow))
            box.Items.Add(new Choice(d.Id, d.Name, d.Name));
        if (selectedId is not null && box.Items.Cast<Choice>().All(c => c.Id != selectedId))
            box.Items.Add(new Choice(selectedId, $"{selectedName ?? "Unknown device"} (not connected)", selectedName));
        box.SelectedItem = box.Items.Cast<Choice>().FirstOrDefault(c => c.Id == selectedId) ?? box.Items[0];
    }

    public static IEnumerable<AudioDevice> Devices(AppServices app, EDataFlow flow)
    {
        if (app.Demo)
        {
            return flow == EDataFlow.Render
                ? new[] { new AudioDevice("demo-headset", "Headset (Arctis Nova Pro)"), new AudioDevice("demo-speakers", "Speakers (Realtek Audio)"), new AudioDevice("demo-tv", "LG TV SSCR2 (NVIDIA High Definition Audio)") }
                : new[] { new AudioDevice("demo-mic", "Microphone (Arctis Nova Pro)"), new AudioDevice("demo-mic2", "Microphone (Shure MV7)") };
        }
        try { return AudioManager.List(flow); }
        catch { return Array.Empty<AudioDevice>(); }
    }

    public static StackPanel Volume(int? initial, Action<int?> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var on = new CheckBox { Content = "Set to", IsChecked = initial is not null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var slider = Ui.SliderWithValue(0, 100, initial ?? 50, "{0:0}%", v => { if (on.IsChecked == true) set((int)v); }, 180, 1);
        slider.IsEnabled = initial is not null;
        on.Click += (_, _) =>
        {
            slider.IsEnabled = on.IsChecked == true;
            set(on.IsChecked == true ? (int)((Slider)slider.Children[0]).Value : null);
        };
        panel.Children.Add(on);
        panel.Children.Add(slider);
        return panel;
    }
}
