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
            Description = "Object path of the new object. Slash-separated: 'Sales/Revenue', 'Products', 'Admin'. DAX forms also accepted: \"'Sales'[Revenue]\". Pair with -t. Relationships use 'Sales[Key]->Product[Key]'."
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Object type. Known values: Table, CalcTable, CalcGroup, Measure, CalcColumn, DataColumn, Hierarchy, Level, Calendar, CalcItem, KPI, Partition, MPartition, EntityPartition, PolicyRangePartition, Expression, Function, Perspective, Culture, ProviderDataSource, StructuredDataSource, Role, TablePermission, Member"
        };
        typeOption.Aliases.Add("-t");
        var valueOption = new Option<string[]?>("-i")
        {
            Description = "Expression or value for the new object. Use '-' to read from stdin. When paired with a preceding -q, applies as that property's value -- so you can set extra properties on the new object in one command, e.g. -i \"SUM(...)\" -q description -i \"my measure\" -q formatString -i \"$#,0\".",
            Arity = ArgumentArity.ZeroOrMore
        };
        var queryOption = new Option<string[]?>("-q")
        {
            Description = "Property name to set on the newly-created object. Pair each -q with a following -i value. Repeatable. Names accept dotted paths, bracket indexers, and DisplayName matching.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var fileOption = new Option<string?>("--file")
        {
            Description = "Read expression from file"
        };
        var ifNotExistsOption = new Option<bool>("--if-not-exists")
        {
            Description = "Succeed silently if the object already exists (exit 0)"
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
            Description = "Persist this command's mutation to the source location. Mutually exclusive with --revert and --stage."
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

            var file = parseResult.GetValue(fileOption);
            var parsed = ParseInterleavedQi(parseResult);
            var value = InputValueResolver.Resolve(parsed.PrimaryValue, file);
            IReadOnlyList<ModelPropertyAssignment> properties = parsed.Properties;

            var result = await new AddModelObjectHandler(_providers).HandleAsync(
                new AddModelObjectRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    parseResult.GetValue(pathArgument) ?? "",
                    parseResult.GetValue(typeOption),
                    value,
                    properties,
                    parseResult.GetValue(ifNotExistsOption),
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

    private static void Render(AddModelObjectResult result)
    {
        Console.WriteLine($"Added: {result.Added}");
        if (result.Saved is false)
            Console.WriteLine("Changes not saved. Use --save to persist.");
        else
            Console.WriteLine($"Saved: {result.Saved}");
    }

    internal static (string? PrimaryValue, IReadOnlyList<ModelPropertyAssignment> Properties) ParseInterleavedQi(
        ParseResult parseResult)
    {
        string? primaryValue = null;
        var properties = new List<ModelPropertyAssignment>();
        string? pendingQuery = null;

        foreach (var (option, value) in OrderedOptionTokens.ReadOptions(parseResult))
        {
            if (option == "-q")
            {
                pendingQuery = value;
                continue;
            }

            if (option == "-i")
            {
                if (value is null)
                    continue;

                // A value applies to the most recent -q (as that property) or, failing that, as the primary value.
                if (pendingQuery is not null)
                {
                    properties.Add(new ModelPropertyAssignment(pendingQuery, value));
                    pendingQuery = null;
                }
                else
                {
                    primaryValue ??= value;
                }

                continue;
            }

            // Any other option abandons a dangling -q that never received its -i value.
            pendingQuery = null;
        }

        return (primaryValue, properties);
    }
}
