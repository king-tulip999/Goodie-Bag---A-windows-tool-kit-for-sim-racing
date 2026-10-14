namespace GoodieBag.Config;

public sealed class CustomThemeSettings
{
    public bool Enabled { get; set; }

    public string? Name { get; set; }

    public ThemeColors Colors { get; set; } = new();

    public List<ThemeColorProfile> Profiles { get; set; } = new();

    public string? EditingProfile { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InUse => Enabled && !Colors.IsDefault;

    public ThemeColorProfile? Profile(string? name) =>
        name is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ThemeColors
{
    public string Background { get; set; } = "#000000";
    public string Edges { get; set; } = "#775869";
    public string Text { get; set; } = "#F5ECEE";
    public string Accent { get; set; } = "#DB156B";
    public string Highlight { get; set; } = "#00FFD0";
    public string Success { get; set; } = "#3DDC84";
    public string Warning { get; set; } = "#FFB224";
    public string Error { get; set; } = "#FF4545";

    public static readonly ThemeColors Default = new();

    public ThemeColors Copy() => (ThemeColors)MemberwiseClone();

    private IEnumerable<string> All => new[] { Background, Edges, Text, Accent, Highlight, Success, Warning, Error };

    public bool SameAs(ThemeColors other) => All.Zip(other.All).All(p => string.Equals(p.First?.Trim(), p.Second?.Trim(), StringComparison.OrdinalIgnoreCase));

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => SameAs(Default);
}

public sealed class ThemeColorProfile
{
    public string Name { get; set; } = "";
    public ThemeColors Colors { get; set; } = new();
}
