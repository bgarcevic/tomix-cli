using System.CommandLine;
using Mdl.App.Rm;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class RmCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public RmCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Object path to remove."
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Force removal even if object has dependents"
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Show what would be removed without saving"
        };
        var ifExistsOption = new Option<bool>("--if-exists")
        {
            Description = "Succeed silently if the object does not exist"
        };
        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save to a different path (implies --save)"
        };
        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder"
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Disambiguate when the path matches multiple table-children."
        };
        typeOption.Aliases.Add("-t");
        var saveOption = new Option<bool>("--save")
        {
            Description = "Persist this command's mutation to the source location"
        };
        var stageOption = new Option<bool>("--stage")
        {
            Description = "Stage this command's mutation"
        };
        var revertOption = new Option<bool>("--revert")
        {
            Description = "Revert a staged mutation"
        };

        var command = new Command("rm", "Remove an object from the model")
        {
            pathArgument,
            modelArgument,
            forceOption,
            dryRunOption,
            ifExistsOption,
            saveToOption,
            serializationOption,
            typeOption,
            saveOption,
            stageOption,
            revertOption
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

            var model = ModelSourceResolver.Resolve(
                GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument));
            var result = await new RemoveModelObjectHandler(_providers).HandleAsync(
                new RemoveModelObjectRequest(
                    new ModelReference(model),
                    parseResult.GetValue(pathArgument) ?? "",
                    type,
                    parseResult.GetValue(ifExistsOption),
                    parseResult.GetValue(dryRunOption),
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption) ?? "",
                    parseResult.GetValue(forceOption)),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(RemoveModelObjectResult result)
    {
        if (result.Removed is false)
            return;

        Console.WriteLine($"Removed: {result.Removed}");
        if (result.Saved is false)
            Console.WriteLine("Changes not saved. Use --save to persist.");
        else if (result.Saved is not null)
            Console.WriteLine($"Saved: {result.Saved}");
    }
}
