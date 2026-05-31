using System.CommandLine;
using Mdl.App.Ls;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class LsCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public LsCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var pathArgument = new Argument<string?>("path-filter")
        {
            Description =
                "Object-path filter. Bare names match literally ('Sales', 'Sales/Measures'); container " +
                "keywords pivot ('Tables', 'Measures', 'Sales/Partitions'); '*' is a wildcard " +
                "('Sa*', '*/Amount'); quote names with spaces (\"'Net Sales'/'Sales Amount'\").",
            Arity = ArgumentArity.ZeroOrOne
        };

        var typeOption = new Option<string?>("--type")
        {
            Description = "Filter by type: table, measure, column, hierarchy, partition, " +
                          "relationship, role, perspective, culture."
        };

        var pathsOnlyOption = new Option<bool>("--paths-only")
        {
            Description = "Output one object path per line, suitable for piping to other commands."
        };

        var noMultilineOption = new Option<bool>("--no-multiline")
        {
            Description = "Collapse multi-line cell content (e.g. measure expressions) to a single " +
                          "line and truncate. Text output only."
        };

        var command = new Command("ls", "List model objects")
        {
            modelArgument,
            pathArgument,
            typeOption,
            pathsOnlyOption,
            noMultilineOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var modelValue = parseResult.GetValue(modelArgument);
            var globalModel = GlobalOptions.ModelValue(parseResult);

            // The active model is --model when given, otherwise the session (local path or remote endpoint).
            var activeReference = string.IsNullOrWhiteSpace(globalModel)
                ? ModelSourceResolver.ResolveReference(null)
                : new ModelReference(globalModel);
            var hasContextModel = !string.IsNullOrWhiteSpace(activeReference.Value);

            // With an active model the bare positional argument is a path-filter, not a model path.
            var reference = hasContextModel ? activeReference : new ModelReference(modelValue ?? "");
            var pathFilter = hasContextModel
                ? parseResult.GetValue(pathArgument) ?? modelValue
                : parseResult.GetValue(pathArgument);
            var typeValue = parseResult.GetValue(typeOption);
            var pathsOnly = parseResult.GetValue(pathsOnlyOption);
            var noMultiline = parseResult.GetValue(noMultilineOption);
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            ModelObjectKind? type = null;
            if (!string.IsNullOrWhiteSpace(typeValue))
            {
                if (!ModelObjectKindParser.TryParse(typeValue, out var parsed))
                {
                    Console.Error.WriteLine(
                        "Invalid --type value. Expected: table, measure, column, hierarchy, " +
                        "partition, relationship, role, perspective, culture.");
                    return 2;
                }

                type = parsed;
            }

            var handler = new LsModelHandler(_providers);
            var result = await handler.HandleAsync(
                new LsModelRequest(reference, pathFilter, type),
                cancellationToken);

            return CommandOutput.Render(
                result,
                formatValue,
                data => LsRenderer.Render(data, pathsOnly, noMultiline),
                ToReferenceJson);
        });

        return command;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToReferenceJson(LsModelResult data)
        => data.Objects.Select(ToReferenceJson).ToList();

    private static IReadOnlyDictionary<string, object?> ToReferenceJson(LsObject obj)
    {
        if (obj.Kind == ModelObjectKind.Table)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = obj.Name,
                ["description"] = obj.Description ?? "",
                ["isHidden"] = obj.Hidden,
                ["dataCategory"] = "",
                ["lineageTag"] = "",
                ["columns"] = obj.ChildCounts.GetValueOrDefault(ModelObjectKind.Column),
                ["measures"] = obj.ChildCounts.GetValueOrDefault(ModelObjectKind.Measure),
                ["hierarchies"] = obj.ChildCounts.GetValueOrDefault(ModelObjectKind.Hierarchy),
                ["partitions"] = obj.ChildCounts.GetValueOrDefault(ModelObjectKind.Partition),
                ["refreshPolicy"] = null,
                ["defaultDetailRowsExpression"] = null
            };
        }

        return new Dictionary<string, object?>
        {
            ["type"] = obj.Kind.ToString(),
            ["path"] = obj.Path,
            ["name"] = obj.Name,
            ["description"] = obj.Description ?? "",
            ["isHidden"] = obj.Hidden,
            ["detail"] = obj.Detail,
            ["expression"] = obj.Expression
        };
    }
}
