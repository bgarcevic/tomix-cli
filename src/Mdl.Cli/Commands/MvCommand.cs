using System.CommandLine;
using Mdl.App.Mv;
using Mdl.App.State;
using Mdl.Cli.Output;
using Mdl.Core.Models;
using Spectre.Console;

namespace Mdl.Cli.Commands;

internal sealed class MvCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public MvCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var sourceArgument = new Argument<string>("source")
        {
            Description = "Source object path"
        };
        var destinationArgument = new Argument<string>("destination")
        {
            Description = "Destination object path"
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite destination when supported"
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Disambiguate when the path matches multiple table-children."
        };
        typeOption.Aliases.Add("-t");
        var stageOption = new Option<bool>("--stage")
        {
            Description = "Stage this command's mutation"
        };
        var revertOption = new Option<bool>("--revert")
        {
            Description = "Revert a staged mutation"
        };
        var saveOption = new Option<bool>("--save")
        {
            Description = "Persist this command's mutation to the source location"
        };
        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save to a different path (implies --save)"
        };
        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder"
        };

        var command = new Command("mv", "Move or rename a model object")
        {
            sourceArgument,
            destinationArgument,
            modelArgument,
            forceOption,
            typeOption,
            stageOption,
            revertOption,
            saveOption,
            saveToOption,
            serializationOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var typeValue = parseResult.GetValue(typeOption);
            ModelObjectKind? type = null;
            if (!string.IsNullOrWhiteSpace(typeValue))
            {
                if (!ModelObjectKindParser.TryParse(typeValue, out var parsed))
                {
                    Console.Error.WriteLine("Invalid --type value.");
                    return 2;
                }

                type = parsed;
            }

            var result = await new MoveModelObjectHandler(_providers).HandleAsync(
                new MoveModelObjectRequest(
                    new ActiveModelResolver().ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    parseResult.GetValue(sourceArgument) ?? "",
                    parseResult.GetValue(destinationArgument) ?? "",
                    type,
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption) ?? "",
                    parseResult.GetValue(forceOption),
                    parseResult.GetValue(stageOption),
                    parseResult.GetValue(revertOption)),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(MoveModelObjectResult result)
    {
        AnsiConsole.MarkupLine(Styling.Success($"Renamed: {result.Moved} -> {result.To}"));
        if (result.Saved is false)
            AnsiConsole.MarkupLine(Styling.Warning("Changes not saved. Use --save to persist."));
        else
            AnsiConsole.MarkupLine(Styling.Success($"Saved: {result.Saved}"));
    }
}
