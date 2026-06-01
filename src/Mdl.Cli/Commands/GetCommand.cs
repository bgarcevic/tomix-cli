using System.CommandLine;
using Mdl.App.Get;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class GetCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public GetCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Object path. Slash-separated: 'Sales', 'Sales/Amount'. DAX forms also accepted."
        };

        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var queryOption = new Option<string?>("--query")
        {
            Description = "Query a specific property (e.g., -q expression, -q formatString)"
        };
        queryOption.Aliases.Add("-q");

        var typeOption = new Option<string?>("--type")
        {
            Description = "Disambiguate when the path matches multiple table-children."
        };
        typeOption.Aliases.Add("-t");

        var command = new Command("get", "Get properties of a model object")
        {
            pathArgument,
            modelArgument,
            queryOption,
            typeOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument) ?? "";
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = parseResult.GetValue(GlobalOptions.ErrorFormat);
            var query = parseResult.GetValue(queryOption);
            var typeValue = parseResult.GetValue(typeOption);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

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

            var result = await new GetModelHandler(_providers).HandleAsync(
                new GetModelRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    path, query, type),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, errorFormat, Render, RenderCsv);
        });

        return command;
    }

    private static void Render(GetModelResult result)
    {
        Console.WriteLine($"{result.Path} ({result.Type})");
        foreach (var (key, value) in result.Properties)
            Console.WriteLine($"{key}: {value}");
    }

    private static void RenderCsv(GetModelResult result)
    {
        if (result.Properties.Count == 1)
        {
            CsvOutput.WriteValue(result.Properties.Values.First());
            return;
        }

        if (string.Equals(result.Type, "Table", StringComparison.Ordinal))
        {
            CsvOutput.Write(
                [
                    "Name",
                    "Description",
                    "Hidden",
                    "DataCategory",
                    "LineageTag",
                    "Columns",
                    "Measures",
                    "Hierarchies",
                    "Partitions",
                    "RefreshPolicy",
                    "DefaultDetailRowsExpression"
                ],
                [TableRow(result.Properties)]);
            return;
        }

        if (string.Equals(result.Type, "Measure", StringComparison.Ordinal))
        {
            CsvOutput.Write(
                [
                    "Name",
                    "Description",
                    "Expression",
                    "FormatString",
                    "Hidden",
                    "DisplayFolder",
                    "DataType",
                    "DetailRowsExpression",
                    "FormatStringExpression",
                    "KPI",
                    "LineageTag"
                ],
                [MeasureRow(result.Properties)]);
            return;
        }

        CsvOutput.Write(
            ["Name", "Description", "Hidden", "Detail", "Expression"],
            [GenericRow(result.Properties)]);
    }

    private static IReadOnlyList<object?> TableRow(IReadOnlyDictionary<string, object?> properties)
        =>
        [
            Value(properties, "name"),
            Value(properties, "description"),
            Value(properties, "isHidden"),
            Value(properties, "dataCategory"),
            Value(properties, "lineageTag"),
            Value(properties, "columns"),
            Value(properties, "measures"),
            Value(properties, "hierarchies"),
            Value(properties, "partitions"),
            Value(properties, "refreshPolicy"),
            Value(properties, "defaultDetailRowsExpression")
        ];

    private static IReadOnlyList<object?> MeasureRow(IReadOnlyDictionary<string, object?> properties)
        =>
        [
            Value(properties, "name"),
            Value(properties, "description"),
            Value(properties, "expression"),
            Value(properties, "formatString"),
            Value(properties, "isHidden"),
            Value(properties, "displayFolder"),
            Value(properties, "dataType"),
            Value(properties, "detailRowsExpression"),
            Value(properties, "formatStringExpression"),
            Value(properties, "kpi"),
            Value(properties, "lineageTag")
        ];

    private static IReadOnlyList<object?> GenericRow(IReadOnlyDictionary<string, object?> properties)
        =>
        [
            Value(properties, "name"),
            Value(properties, "description"),
            Value(properties, "isHidden"),
            Value(properties, "detail"),
            Value(properties, "expression")
        ];

    private static object? Value(IReadOnlyDictionary<string, object?> properties, string key)
        => properties.TryGetValue(key, out var value) ? value : "";
}
