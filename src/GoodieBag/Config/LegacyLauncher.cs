namespace GoodieBag.Config;

internal static class LegacyLauncher
{
    public const string PageKey = "launcher";

    public static void Migrate(AppConfig config)
    {
        var entries = config.Launcher?.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Path)).ToList();
        config.Launcher = null;
        config.Ui.NavOrder.RemoveAll(k => k == PageKey);
        config.Plugins.Remove(PageKey);
        if (config.Ui.StartPage == PageKey) config.Ui.StartPage = "modes";
        if (entries is null || entries.Count == 0)
            return;

        var cmp = StringComparer.OrdinalIgnoreCase;
        var uses = entries.Where(e => e.ModeName is not null).GroupBy(e => e.ModeName!, cmp).ToDictionary(g => g.Key, g => g.Count(), cmp);
        foreach (var entry in entries)
        {
            var source = config.Modes.FirstOrDefault(m => cmp.Equals(m.Name, entry.ModeName));
            Mode target;
            if (source is not null && source.Program is null && uses[source.Name] == 1)
            {
                target = source;
            }
            else
            {
                target = source is null ? new Mode() : ConfigStore.Clone(source);
                target.Name = UniqueName(config, string.IsNullOrWhiteSpace(entry.Name) ? System.IO.Path.GetFileNameWithoutExtension(entry.Path) : entry.Name.Trim());
                target.Hotkey = null;
                target.Program = null;
                target.Triggers = new();
                foreach (var rule in target.Windows) rule.Id = Guid.NewGuid().ToString("N");
                int at = source is null ? -1 : config.Modes.IndexOf(source);
                if (at < 0) config.Modes.Add(target);
                else config.Modes.Insert(at + 1, target);
            }
            target.Program = new ModeProgram
            {
                Path = entry.Path, Arguments = entry.Arguments, AsAdmin = entry.AsAdmin, IconPath = entry.IconPath, ProcessName = entry.ProcessName,
            };
            foreach (var helper in entry.AlsoLaunch.Where(h => !string.IsNullOrWhiteSpace(h.Path)))
                if (!target.Launch.Any(l => cmp.Equals(l.Path, helper.Path)))
                    target.Launch.Add(helper);
            target.CloseLaunchedOnExit |= entry.CloseHelpersOnExit;
        }
    }

    private static string UniqueName(AppConfig config, string name)
    {
        string candidate = name;
        for (int n = 2; config.Modes.Any(m => string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }
}
