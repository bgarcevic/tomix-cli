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
            var model = ModelSourceResolver.Resolve(
                GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument));
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var result = await new FindModelHandler(_providers).HandleAsync(
                new FindModelRequest(
                    new ModelReference(model),
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
                data => Render(data, pathsOnly, noMultiline));
        });

        return command;
    }

    private static void Render(FindModelResult result, bool pathsOnly, bool noMultiline)
    {
        foreach (var match in result.Matches)
        {
            if (pathsOnly)
            {
                Console.WriteLine(match.Path);
                continue;
            }

            var value = noMultiline
                ? match.Value.ReplaceLineEndings(" ").Trim()
                : match.Value;
            Console.WriteLine($"{match.Path}  {match.Type}  {match.Field}: {value}");
        }
    }
}
