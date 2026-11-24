using System.Text.RegularExpressions;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// Lightless profile descriptions are FFXIV macro strings (e.g. <c>&lt;color(0xFF00FE)&gt;</c>, <c>&lt;shadow(1)&gt;</c>,
/// <c>&lt;color(stackcolor)&gt;</c>, <c>&lt;br&gt;</c>), which Lightless renders as rich text. Laci shows descriptions
/// as plain text, so the markup is removed for display. Only used inside <see cref="SyncHubClientLL"/>.
/// </summary>
internal static partial class ProfileMarkupLL
{
    /// <summary>
    /// Returns <paramref name="description"/> without macro markup. Text that only looks a bit like a tag (e.g. "&lt;3")
    /// is kept, because only <c>&lt;name(...)&gt;</c>, <c>&lt;name=...&gt;</c>, <c>&lt;/name&gt;</c> and
    /// <c>&lt;br&gt;</c> are treated as markup.
    /// </summary>
    public static string? StripMarkup(string? description)
    {
        if (string.IsNullOrEmpty(description) || !description.Contains('<')) return description;

        try
        {
            return Strip(description);
        }
        catch (RegexMatchTimeoutException)
        {
            return description;
        }
    }

    private static string Strip(string description)
    {
        var lines = LineBreakRegex().Replace(description.ReplaceLineEndings("\n"), "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var stripped = MacroRegex().Replace(line, string.Empty);
            // Drop lines that only held formatting tags, but keep blank lines the author typed
            if (stripped.Trim().Length == 0 && line.Trim().Length > 0) continue;
            kept.Add(stripped);
        }

        return string.Join('\n', kept).Trim();
    }

    [GeneratedRegex(@"<br\s*/?>|</br>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex LineBreakRegex();

    // <color(0xFF00FE)>, <shadow(1)>, <color=#ff00ff>, </color>
    [GeneratedRegex(@"<[A-Za-z]+(?:\([^<>]*\)|=[^<>]*)>|</[A-Za-z]+>", RegexOptions.None, 1000)]
    private static partial Regex MacroRegex();
}
