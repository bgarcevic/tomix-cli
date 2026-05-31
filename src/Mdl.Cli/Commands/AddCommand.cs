using System.CommandLine;
using Mdl.App.Add;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class AddCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public AddCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Object path of the new object."
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Object type. Known local values: Table, Measure."
        };
        typeOption.Aliases.Add("-t");
        var valueOption = new Option<string?>("-i")
        {
            Description = "Expression or value for the new object. Use '-' to read from stdin."
        };
        var queryOption = new Option<string?>("-q")
        {
            Description = "Property name to set on the newly-created object."
        };
        var fileOption = new Option<string?>("--file")
        {
            Description = "Read expression from file"
        };
        var ifNotExistsOption = new Option<bool>("--if-not-exists")
        {
            Description = "Succeed silently if the object already exists"
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Save even if this mutation introduces validation errors"
        };
        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save to a different path (implies --save)"
        };
        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder, pbip"
        };
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

        var ignoredOptions = new Option[]
        {
            new Option<string?>("--mode"),
            new Option<string?>("--source"),
            new Option<string?>("--endpoint"),
            new Option<string?>("--connection-string"),
            new Option<string?>("--source-table"),
            new Option<string?>("--source-database"),
            new Option<string?>("--partition-expression"),
            new Option<string?>("--columns"),
            new Option<string?>("--source-type")
        };

        var command = new Command("add", "Add an object to the model")
        {
            pathArgument,
            modelArgument,
            typeOption,
            valueOption,
            queryOption,
            fileOption,
            ifNotExistsOption,
            forceOption,
            saveToOption,
            serializationOption,
            saveOption,
            stageOption,
            revertOption
        };

        foreach (var option in ignoredOptions)
            command.Options.Add(option);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var model = ModelSourceResolver.Resolve(
                GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument));
            var value = ResolveInputValue(
                parseResult.GetValue(valueOption),
                parseResult.GetValue(fileOption));
            var query = parseResult.GetValue(queryOption);
            IReadOnlyList<ModelPropertyAssignment> properties = string.IsNullOrWhiteSpace(query)
                ? Array.Empty<ModelPropertyAssignment>()
                : [new ModelPropertyAssignment(query, value ?? "")];

            var result = await new AddModelObjectHandler(_providers).HandleAsync(
                new AddModelObjectRequest(
                    new ModelReference(model),
                    parseResult.GetValue(pathArgument) ?? "",
                    parseResult.GetValue(typeOption),
                    value,
                    properties,
                    parseResult.GetValue(ifNotExistsOption),
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption) ?? "",
                    parseResult.GetValue(forceOption)),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static string? ResolveInputValue(string? value, string? file)
    {
        if (!string.IsNullOrWhiteSpace(file))
            return File.ReadAllText(file);

        return value == "-" ? Console.In.ReadToEnd() : value;
    }

    private static void Render(AddModelObjectResult result)
    {
        Console.WriteLine($"Added: {result.Added}");
        if (result.Saved is false)
            Console.WriteLine("Changes not saved. Use --save to persist.");
        else
            Console.WriteLine($"Saved: {result.Saved}");
    }
}
