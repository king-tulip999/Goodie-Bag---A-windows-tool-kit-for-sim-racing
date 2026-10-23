using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GoodieBag.Config;

public static class ConfigStore
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GoodieBag");

    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    private static readonly NullabilityInfoContext Nullability = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LenientEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { KeepDefaultForNull } },
    };

    private static void KeepDefaultForNull(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
            return;
        foreach (var property in info.Properties)
        {
            if (property.Set is not { } set || property.PropertyType.IsValueType || property.AttributeProvider is not PropertyInfo member)
                continue;
            NullabilityState state;
            lock (Nullability)
                state = Nullability.Create(member).WriteState;
            if (state == NullabilityState.NotNull)
                property.Set = (target, value) =>
                {
                    if (value is not null)
                        set(target, value);
                };
        }
    }

    internal static JsonSerializerOptions JsonOptions => Options;

    public static bool ReadOnly { get; set; }

    public static AppConfig Load(out string? warning)
    {
        var config = LoadRaw(out warning);
        try
        {
            return Normalize(config);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReadOnly = true;
            warning = $"config.json couldn't be used ({ex.Message}). Goodie Bag started with default settings and won't save over the file this time.";
            return Normalize(Defaults());
        }
    }

    public static AppConfig Normalize(AppConfig config)
    {
        RemoveNullItems(config);
        DisplayProfile.Migrate(config);
        config.Fans.Normalize();
        if (config.Fans.PresetNamesVersion < 1
            && config.Fans.Presets.FirstOrDefault(p => p.Name == FanSettings.OldDefaultPresetName) is { } balanced
            && !config.Fans.Presets.Any(p => string.Equals(p.Name, FanSettings.DefaultPresetName, StringComparison.OrdinalIgnoreCase)))
        {
            balanced.Name = FanSettings.DefaultPresetName;
            if (config.Fans.ActivePreset == FanSettings.OldDefaultPresetName) config.Fans.ActivePreset = FanSettings.DefaultPresetName;
        }
        config.Fans.PresetNamesVersion = 1;
        config.Overlay.Normalize();
        config.WindowProfiles ??= new();
        LegacyWindowsmith.Migrate(config);
        LegacyModes.Migrate(config);
        config.WindowProfiles.Normalize();
        config.Extra?.Remove(ConfigTransfer.Header);
        if (config.Ui.DarkMode is { } dark)
        {
            if (dark) config.Ui.Theme = AppTheme.Dark;
            config.Ui.DarkMode = null;
        }
        if (config.Overlay.ColorsVersion < 1)
        {
            foreach (var layout in config.Overlay.Layouts.Where(l => l.Colors == OverlayLayout.For(config.Ui.Theme) && string.IsNullOrEmpty(l.ColorSet)))
                layout.Colors = OverlayColorPreset.App;
            config.Overlay.ColorsVersion = 1;
        }
        if (config.SimRacing.ColorVersion < 1)
        {
            config.SimRacing.Current ??= new();
            config.SimRacing.Current.Color = null;
            config.SimRacing.ColorVersion = 1;
        }
        if (config.Ui.PerformanceIntervalMs is int oldMs)
        {
            config.Performance.Rate = PerformanceSettings.FromLegacy(oldMs);
            config.Ui.PerformanceIntervalMs = null;
        }
        config.Performance.CustomMs = Math.Clamp(config.Performance.CustomMs, PerformanceSettings.MinMs, PerformanceSettings.MaxMs);
        config.Ui.KeybindsFolded.RemoveAll(path => path is null);
        config.Ui.KeybindOrder.RemoveAll(id => !Services.HotkeyCatalog.IsCategoryKey(id));
        config.Hotkeys ??= new();
        config.SimRacing.AssignCompareColors();
        (config.SimRacing.Setups ??= new()).Normalize();
        config.SimRacing.MigratePanelLayout();
        (config.SessionStats ??= new()).Normalize();
        (config.SimRacing.Hud ??= new()).Normalize(SimRacing.Hud.HudPresets.DefaultLayouts, SimRacing.Hud.HudPresets.DefaultRules);
        if (config.Ui.StartPage is null || config.Ui.StartPage.Equals("home", StringComparison.OrdinalIgnoreCase) || config.Ui.StartPage.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            config.Ui.StartPage = UiSettings.LastUsedPage;
        config.Ui.PageSections ??= new(StringComparer.OrdinalIgnoreCase);
        if (config.Ui.Window is null) config.Ui.SidebarVersion = 1;
        foreach (var key in config.Ui.PageSections.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList()) config.Ui.PageSections.Remove(key);
        config.Ui.LandingVersion = 1;
        if (config.Ui.InterfaceScaleVersion < 1) config.Ui.InterfaceScale = UiSettings.MigrateScale(config.Ui.InterfaceScale);
        config.Ui.InterfaceScaleVersion = 1;
        if (config.Ui.MonitorSectionsVersion < 2) config.Ui.MonitorHiddenSections.RemoveAll(id => id is "performance" or "sensors");
        config.Ui.MonitorSectionsVersion = 2;
        config.Ui.LandingLayout ??= new();
        config.Ui.LandingLayout.RemoveAll(slot => slot.Id == "home");
        foreach (var layout in new[] { config.Ui.SensorsLayout, config.Fans.Layout })
            PanelSlot.MigrateToOrder(layout);
        config.Ui.SensorsLayout.RemoveAll(slot => !slot.Id.StartsWith('/'));
        IgnoreCaseKeys(config);
        return config;
    }

    private static void RemoveNullItems(AppConfig config)
    {
        RemoveNulls(config, 0);
        foreach (var layout in new[] { config.Ui.SensorsLayout, config.Fans.Layout, config.SimRacing.PanelLayout })
            layout.RemoveAll(slot => slot.Id is null);
    }

    private static void RemoveNulls(object node, int depth)
    {
        if (depth > 12) return;
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string)
                || property.PropertyType.IsValueType || property.IsDefined(typeof(JsonIgnoreAttribute)))
                continue;
            object? value;
            try { value = property.GetValue(node); }
            catch (TargetInvocationException) { continue; }
            if (value is System.Collections.IList { IsFixedSize: false } list)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] is null) list.RemoveAt(i);
                foreach (var item in list)
                    if (IsConfigType(item!.GetType())) RemoveNulls(item, depth + 1);
            }
            else if (value is not null && IsConfigType(value.GetType()))
                RemoveNulls(value, depth + 1);
        }
    }

    private static bool IsConfigType(Type type) => !type.IsEnum && type.Namespace?.StartsWith("GoodieBag", StringComparison.Ordinal) == true;

    private static void IgnoreCaseKeys(AppConfig config)
    {
        foreach (var profile in config.Displays.Profiles)
            profile.Wallpapers = IgnoreCase(profile.Wallpapers);
        config.Displays.Names = IgnoreCase(config.Displays.Names);
        config.Ui.PageSections = IgnoreCase(config.Ui.PageSections);
        var iracing = config.SimRacing.IracingController ??= new();
        iracing.IgnoredValues = IgnoreCase(iracing.IgnoredValues);
        iracing.SavedLayout = IgnoreCase(iracing.SavedLayout);
        iracing.IgnoredFindings = new HashSet<string>(iracing.IgnoredFindings ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> IgnoreCase(Dictionary<string, string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is not null)
            foreach (var (key, value) in values) result[key] = value;
        return result;
    }

    private static AppConfig LoadRaw(out string? warning)
    {
        warning = null;
        MigrateFromOldName();
        if (!File.Exists(FilePath))
            return Fresh();

        try
        {
            var json = ReadWithRetry(FilePath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? Defaults();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReadOnly = true;
            warning = $"config.json couldn't be read ({ex.Message}). Goodie Bag started with default settings and won't save over the file this time; restart it to try again.";
            return Defaults();
        }
        catch (JsonException ex)
        {
            if (ReadOnly) return Defaults();
            var backup = Path.Combine(Folder, $"config.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Move(FilePath, backup);
            }
            catch
            {
                ReadOnly = true;
                warning = $"config.json couldn't be read ({ex.Message}). Goodie Bag started with default settings and won't save over the file this time; restart it to try again.";
                return Defaults();
            }
            warning = $"config.json couldn't be read ({ex.Message}). It was moved to {Path.GetFileName(backup)} and a fresh config was started.";
            return Defaults();
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(150);
            }
        }
    }

    public static string? ResetToDefaults()
    {
        bool wasReadOnly = ReadOnly;
        ReadOnly = true;
        lock (WriteGate)
        {
            if (!File.Exists(FilePath)) return null;
            var backup = Path.Combine(Folder, $"config.before-reset-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Move(FilePath, backup);
            }
            catch
            {
                ReadOnly = wasReadOnly;
                throw;
            }
            return backup;
        }
    }

    public static string? ReplaceWith(AppConfig imported)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(imported, Options);
        bool wasReadOnly = ReadOnly;
        ReadOnly = true;
        Monitor.Enter(WriteGate);
        try
        {
            Directory.CreateDirectory(Folder);
            string? backup = null;
            if (File.Exists(FilePath))
            {
                backup = Path.Combine(Folder, $"config.before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(FilePath, backup, overwrite: true);
            }
            var temp = FilePath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, FilePath, overwrite: true);
            return backup;
        }
        catch
        {
            ReadOnly = wasReadOnly;
            throw;
        }
        finally
        {
            Monitor.Exit(WriteGate);
        }
    }

    public static AppConfig Fresh()
    {
        var config = Defaults();
        config.Ui.SetupPending = true;
        return config;
    }

    internal static AppConfig Defaults()
    {
        var config = new AppConfig();
        config.Ui.InterfaceScaleVersion = 1;
        config.Ui.MonitorSectionsVersion = 2;
        config.Fans.PresetNamesVersion = 1;
        return config;
    }

    private static void MigrateFromOldName()
    {
        try
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RigControl", "config.json");
            if (!File.Exists(FilePath) && File.Exists(old))
            {
                Directory.CreateDirectory(Folder);
                File.Copy(old, FilePath);
                File.Move(old, old + ".migrated", overwrite: true);
            }
        }
        catch { }
    }

    public static T Clone<T>(T value)
    {
        if (value is AppConfig config) config.StoreSections();
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    }

    public static void Save(AppConfig config)
    {
        if (Serialize(config) is { } bytes)
            Write(bytes, Interlocked.Increment(ref _queued));
    }

    public static Task? SaveInBackground(AppConfig config)
    {
        if (Serialize(config) is not { } bytes) return null;
        long version = Interlocked.Increment(ref _queued);
        return _writing = Task.Run(() => Write(bytes, version));
    }

    private static Task? _writing;

    public static void WaitForWrites()
    {
        try { _writing?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }

    private static readonly object WriteGate = new();
    private static long _queued, _written;

    private static byte[]? Serialize(AppConfig config)
    {
        if (ReadOnly)
            return null;
        config.StoreSections();
        return JsonSerializer.SerializeToUtf8Bytes(config, Options);
    }

    private static void Write(byte[] bytes, long version)
    {
        lock (WriteGate)
        {
            if (ReadOnly || version < _written) return;
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temp, FilePath, overwrite: true);
                    _written = version;
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 3)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}

internal sealed class LenientEnumConverter : JsonConverterFactory
{
    private static readonly JsonStringEnumConverter Names = new();

    private static readonly Dictionary<Type, object> Fallbacks = new()
    {
        [typeof(BackgroundUsage)] = BackgroundUsage.Reduced,
    };

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Lenient<>).MakeGenericType(typeToConvert), Names.CreateConverter(typeToConvert, options))!;

    private sealed class Lenient<T> : JsonConverter<T> where T : struct, Enum
    {
        private readonly JsonConverter<T> _inner;
        private readonly T _fallback = Fallbacks.TryGetValue(typeof(T), out var value) ? (T)value : default;
        private readonly bool _flags = typeof(T).IsDefined(typeof(FlagsAttribute), inherit: false);

        public Lenient(JsonConverter inner) => _inner = (JsonConverter<T>)inner;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Number))
            {
                reader.Skip();
                return _fallback;
            }
            try
            {
                var value = _inner.Read(ref reader, typeToConvert, options);
                return _flags || Enum.IsDefined(value) ? value : _fallback;
            }
            catch (JsonException)
            {
                return _fallback;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => _inner.Write(writer, value, options);

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try { return _inner.ReadAsPropertyName(ref reader, typeToConvert, options); }
            catch (JsonException) { return (T)Enum.ToObject(typeof(T), int.MinValue); }
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            _inner.WriteAsPropertyName(writer, value, options);
    }
}
