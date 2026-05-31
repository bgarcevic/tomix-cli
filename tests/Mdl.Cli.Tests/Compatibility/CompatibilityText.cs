using System.Text.RegularExpressions;

namespace Mdl.Cli.Tests.Compatibility;

internal static partial class CompatibilityText
{
    public static string WithoutPreviewFooter(string text)
    {
        var lines = text.Split('\n')
            .Where(line => !line.StartsWith("This is an early preview release", StringComparison.Ordinal))
            .ToArray();

        return string.Join('\n', lines).Trim();
    }

    public static IReadOnlyList<string> RootCommandNames(string helpText)
    {
        return Section(helpText, "Commands:")
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToArray();
    }

    public static IReadOnlySet<string> LongOptions(string helpText)
    {
        return LongOption().Matches(helpText)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static IReadOnlySet<string> CommandSpecificLongOptions(string helpText)
    {
        var lines = helpText.Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == "Options:");
        if (start < 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var optionLines = lines[(start + 1)..]
            .TakeWhile(line =>
                line.Trim().Length == 0 ||
                (char.IsWhiteSpace(line[0]) && !line.TrimStart().StartsWith("-?", StringComparison.Ordinal)));

        return LongOption().Matches(string.Join('\n', optionLines))
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static string JsonPrefix(string text)
    {
        var trimmed = WithoutPreviewFooter(text).TrimStart();
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c is '{' or '[')
                depth++;
            else if (c is '}' or ']')
                depth--;

            if (depth == 0 && i > 0)
                return trimmed[..(i + 1)];
        }

        return trimmed;
    }

    private static IEnumerable<string> Section(string text, string header)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == header);
        if (start < 0)
            return [];

        return lines[(start + 1)..].TakeWhile(line =>
            line.Trim().Length == 0 ||
            char.IsWhiteSpace(line[0]));
    }

    [GeneratedRegex("--[A-Za-z0-9-]+")]
    private static partial Regex LongOption();
}
