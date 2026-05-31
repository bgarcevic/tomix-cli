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

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(GetModelResult result)
    {
        Console.WriteLine($"{result.Path} ({result.Type})");
        foreach (var (key, value) in result.Properties)
            Console.WriteLine($"{key}: {value}");
    }
}
