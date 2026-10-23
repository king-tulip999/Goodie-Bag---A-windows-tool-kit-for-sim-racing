using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using GoodieBag.Controls;
using GoodieBag.Services;

namespace GoodieBag.Views;

internal enum ReleaseLineKind { Heading, Bullet, Text }

internal sealed record ReleaseLine(ReleaseLineKind Kind, string Text, int Indent = 0);

internal static class ChangelogDialog
{
    public static Task Show(MainWindow window, UpdateService updates, Version? since, bool justUpdated)
    {
        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var loading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        loading.Children.Add(new LoadingSpinner(22) { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        loading.Children.Add(new TextBlock { Text = "Loading the release notes…", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(loading);

        string page = UpdateService.ReleasesPage;
        var buttons = new StackPanel();
        var github = Ui.LabeledButton("I.ChevronRight", "Open on GitHub", () => Ui.OpenUrl(page));
        github.Margin = new Thickness(0, 0, 8, 0);
        var close = new Button { Content = "Close", Style = Ui.Res<Style>("PrimaryButton"), IsDefault = true };
        close.Click += (_, _) => window.CloseModal(false);
        buttons.Children.Add(github);
        buttons.Children.Add(close);

        string title = justUpdated ? "What's new" : "Changelog";
        string subtitle = justUpdated
            ? (since is { } from ? $"Goodie Bag {UpdateService.Tag(from)} → {UpdateService.Tag(updates.Installed)}" : $"Goodie Bag {UpdateService.Tag(updates.Installed)}")
            : "Every release up to the one you have, newest first.";
        var shown = window.ShowModal(Ui.Dialog(window, title, subtitle, body, buttons, null, 640));
        close.Focus();
        _ = Fill(body, updates, since, justUpdated, url => page = url);
        return shown;
    }

    private static async Task Fill(StackPanel body, UpdateService updates, Version? since, bool justUpdated, Action<string> pageOf)
    {
        List<ReleaseInfo> releases;
        try { releases = await updates.ReleasesUpToInstalledAsync(); }
        catch (Exception ex)
        {
            body.Children.Clear();
            body.Children.Add(Ui.DangerBanner("Couldn't load the release notes", ex.Message));
            return;
        }
        var picked = justUpdated
            ? releases.Where(r => since is { } from ? r.Version > from : r.Version == updates.Installed).ToList()
            : releases;
        if (picked.Count == 0 && justUpdated) picked = releases.Take(1).ToList();
        if (picked.Count == 1) pageOf(picked[0].PageUrl);
        body.Children.Clear();
        if (picked.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "No release notes were found.", Style = Ui.Res<Style>("Muted") });
            return;
        }
        for (int i = 0; i < picked.Count; i++) body.Children.Add(Release(picked[i], i == 0));
    }

    private static StackPanel Release(ReleaseInfo release, bool first)
    {
        var stack = new StackPanel { Margin = new Thickness(0, first ? 0 : 22, 0, 0) };
        string when = release.Published is { } at ? " · " + Dates.Date(at.ToLocalTime()) : "";
        stack.Children.Add(new TextBlock { Text = UpdateService.Tag(release.Version) + when, Style = Ui.Res<Style>("H3") });
        var lines = PlainText(release.Notes);
        if (lines.Count == 0)
            stack.Children.Add(new TextBlock { Text = "No notes for this release.", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        foreach (var line in lines) stack.Children.Add(Line(line));
        return stack;
    }

    private static FrameworkElement Line(ReleaseLine line)
    {
        if (line.Kind == ReleaseLineKind.Heading)
            return new TextBlock { Text = line.Text, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 12, 0, 2), TextWrapping = TextWrapping.Wrap };
        if (line.Kind == ReleaseLineKind.Text)
            return new TextBlock { Text = line.Text, Style = Ui.Res<Style>("Body"), Margin = new Thickness(0, 6, 0, 0) };
        var row = new Grid { Margin = new Thickness(line.Indent * 16, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = "•", Style = Ui.Res<Style>("Muted") });
        var text = new TextBlock { Text = line.Text, Style = Ui.Res<Style>("Body") };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static readonly RegexOptions Options = RegexOptions.CultureInvariant;
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$", Options);
    private static readonly Regex Bullet = new(@"^(\s*)(?:[-*+]|\d+[.)])\s+(.*)$", Options);
    private static readonly Regex Rule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", Options);
    private static readonly Regex Image = new(@"!\[([^\]]*)\]\([^)]*\)", Options);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)", Options);
    private static readonly Regex RefLink = new(@"\[([^\]]+)\]\[[^\]]*\]", Options);
    private static readonly Regex AutoLink = new(@"<(https?://[^>\s]+)>", Options);
    private static readonly Regex Tag = new(@"</?[A-Za-z][^>]*>", Options);
    private static readonly Regex Strong = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", Options);
    private static readonly Regex StarEmphasis = new(@"(?<![\*\w])\*(?=\S)(.+?)(?<=\S)\*(?![\*\w])", Options);
    private static readonly Regex UnderscoreEmphasis = new(@"(?<!\w)_(?=\S)(.+?)(?<=\S)_(?!\w)", Options);
    private static readonly Regex Strike = new(@"~~(.+?)~~", Options);
    private static readonly Regex Code = new(@"`+([^`]*)`+", Options);
    private static readonly Regex Escape = new(@"\\([\\`*_{}\[\]()#+\-.!>~|])", Options);
    private static readonly Regex Definition = new(@"^\s{0,3}\[[^\]]+\]:\s*\S+", Options);

    internal static List<ReleaseLine> PlainText(string markdown)
    {
        var result = new List<ReleaseLine>();
        var paragraph = new List<string>();
        void EndParagraph()
        {
            if (paragraph.Count > 0) result.Add(new ReleaseLine(ReleaseLineKind.Text, string.Join(" ", paragraph)));
            paragraph.Clear();
        }
        foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || Rule.IsMatch(line) || Definition.IsMatch(line)) { EndParagraph(); continue; }
            if (line.Trim().Length == 0) { EndParagraph(); continue; }
            if (Heading.Match(line) is { Success: true } h)
            {
                EndParagraph();
                if (Inline(h.Groups[1].Value) is { Length: > 0 } heading) result.Add(new ReleaseLine(ReleaseLineKind.Heading, heading));
                continue;
            }
            if (Bullet.Match(line) is { Success: true } b)
            {
                EndParagraph();
                int indent = Math.Min(3, b.Groups[1].Value.Replace("\t", "    ").Length / 2);
                if (Inline(b.Groups[2].Value) is { Length: > 0 } item) result.Add(new ReleaseLine(ReleaseLineKind.Bullet, item, indent));
                continue;
            }
            var text = Inline(line.TrimStart().TrimStart('>').Trim());
            if (text.Length == 0) continue;
            if (paragraph.Count == 0 && result.Count > 0 && result[^1].Kind == ReleaseLineKind.Bullet && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                result[^1] = result[^1] with { Text = result[^1].Text + " " + text };
                continue;
            }
            paragraph.Add(text);
        }
        EndParagraph();
        return result;
    }

    internal static string Inline(string text)
    {
        text = Image.Replace(text, "$1");
        text = Link.Replace(text, "$1");
        text = RefLink.Replace(text, "$1");
        text = AutoLink.Replace(text, "$1");
        text = Code.Replace(text, "$1");
        text = Tag.Replace(text, "");
        text = Strong.Replace(text, "$2");
        text = StarEmphasis.Replace(text, "$1");
        text = UnderscoreEmphasis.Replace(text, "$1");
        text = Strike.Replace(text, "$1");
        text = Escape.Replace(text, "$1");
        return Regex.Replace(text, @"\s{2,}", " ", Options).Trim();
    }
}
