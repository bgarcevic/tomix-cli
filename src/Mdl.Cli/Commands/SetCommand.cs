using System.CommandLine;
using Mdl.App.Set;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class SetCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public SetCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Object path. Slash-separated paths and DAX forms are accepted."
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var queryOption = new Option<string?>("-q")
        {
            Description = "Property expression."
        };
        var valueOption = new Option<string?>("-i")
        {
            Description = "Value for the preceding -q. Use '-' to read from stdin."
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Save even if this mutation introduces validation errors"
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
        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save to a different path (implies --save)"
        };
        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder"
        };
        var stageOption = new Option<bool>("--stage")
        {
            Description = "Stage this command's mutation"
        };
        var revertOption = new Option<bool>("--revert")
        {
            Description = "Revert a staged mutation"
        };

        var command = new Command("set", "Set a property on a model object")
        {
            pathArgument,
            modelArgument,
            queryOption,
            valueOption,
            forceOption,
            typeOption,
            saveOption,
            saveToOption,
            serializationOption,
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

            var query = parseResult.GetValue(queryOption);
            var value = ResolveInputValue(parseResult.GetValue(valueOption));
            IReadOnlyList<ModelPropertyAssignment> assignments = string.IsNullOrWhiteSpace(query)
                ? Array.Empty<ModelPropertyAssignment>()
                : [new ModelPropertyAssignment(query, value ?? "")];
            var result = await new SetModelPropertyHandler(_providers).HandleAsync(
                new SetModelPropertyRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    parseResult.GetValue(pathArgument) ?? "",
                    assignments,
                    type,
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption) ?? "",
                    parseResult.GetValue(forceOption)),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static string? ResolveInputValue(string? value)
        => value == "-" ? Console.In.ReadToEnd() : value;

    private static void Render(SetModelPropertyResult result)
    {
        Console.WriteLine($"Set: {result.Set}.{result.Property}");
        if (result.Saved is false)
            Console.WriteLine("Changes not saved. Use --save to persist.");
        else
            Console.WriteLine($"Saved: {result.Saved}");
    }
}
