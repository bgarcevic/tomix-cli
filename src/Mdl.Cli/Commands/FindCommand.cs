using System.CommandLine;
using Mdl.App.Find;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class FindCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public FindCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var patternArgument = new Argument<string>("pattern")
        {
            Description = "Text or regex pattern to search for"
        };

        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var inOption = new Option<string?>("--in")
        {
            Description = "Scope: names, expressions, descriptions, displayFolders, formatStrings, annotations, all (default: all)"
        };
        var regexOption = new Option<bool>("--regex")
        {
            Description = "Treat pattern as a regular expression"
        };
        var caseSensitiveOption = new Option<bool>("--case-sensitive")
        {
            Description = "Enable case-sensitive matching"
        };
        var pathsOnlyOption = new Option<bool>("--paths-only")
        {
            Description = "Output one matching object path per line, suitable for piping"
        };
        var noMultilineOption = new Option<bool>("--no-multiline")
        {
            Description = "Collapse multi-line match context to a single line. Text output only."
        };

        var command = new Command("find", "Search for text across model objects")
        {
            patternArgument,
            modelArgument,
            inOption,
            regexOption,
            caseSensitiveOption,
            pathsOnlyOption,
            noMultilineOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var pattern = parseResult.GetValue(patternArgument) ?? "";
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = parseResult.GetValue(GlobalOptions.ErrorFormat);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var result = await new FindModelHandler(_providers).HandleAsync(
                new FindModelRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    pattern,
                    parseResult.GetValue(inOption) ?? "all",
                    parseResult.GetValue(regexOption),
                    parseResult.GetValue(caseSensitiveOption)),
                cancellationToken);

            var pathsOnly = parseResult.GetValue(pathsOnlyOption);
            var noMultiline = parseResult.GetValue(noMultilineOption);
            return CommandOutput.Render(
                result,
                formatValue,
                data => Render(data, pathsOnly, noMultiline),
                ToReferenceJson,
                errorFormat: errorFormat);
        });

        return command;
    }

    private static void Render(FindModelResult result, bool pathsOnly, bool noMultiline)
    {
        if (result.Matches.Count == 0)
        {
            if (!pathsOnly)
                Console.WriteLine("No matches found.");

            return;
        }

        if (pathsOnly)
        {
            foreach (var path in result.Matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase))
                Console.WriteLine(path);

            return;
        }

        _ = noMultiline;
        RenderMatchTable(result.Matches);
    }

    private static void RenderMatchTable(IReadOnlyList<FindMatch> matches)
    {
        var rows = matches
            .Select(match => new[]
            {
                match.Path,
                match.Type,
                match.Property,
                match.MatchedText,
                match.Line.ToString()
            })
            .ToList();

        var headers = new[] { "Path", "Type", "Property", "Match", "Line" };
        var widths = Enumerable.Range(0, headers.Length)
            .Select(i => Math.Max(headers[i].Length, rows.Max(row => row[i].Length)))
            .ToArray();

        var totalWidth = widths.Sum() + (headers.Length - 1) * 3 + 4;
        Console.WriteLine(new string(' ', totalWidth));
        Console.WriteLine(Row(headers, widths));
        Console.WriteLine(Separator(widths));
        foreach (var row in rows)
            Console.WriteLine(Row(row, widths));
        Console.WriteLine(new string(' ', totalWidth));
        Console.WriteLine($"{matches.Count} match(es)");
    }

    private static string Row(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
        => "  " + string.Join(" │ ", cells.Select((cell, index) => cell.PadRight(widths[index]))) + "  ";

    private static string Separator(IReadOnlyList<int> widths)
        => " " + string.Join("┼", widths.Select(width => new string('─', width + 2))) + " ";

    private static object ToReferenceJson(FindModelResult result)
        => new
        {
            pattern = result.Pattern,
            matchCount = result.Matches.Count,
            matches = result.Matches.Select(match => new
            {
                objectPath = match.Path,
                objectType = match.Type,
                property = match.Property,
                matchedText = match.MatchedText,
                line = match.Line,
                position = match.Position
            })
        };
}
