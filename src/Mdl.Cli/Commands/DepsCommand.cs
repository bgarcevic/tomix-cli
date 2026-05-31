using System.CommandLine;
using Mdl.App.Deps;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class DepsCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public DepsCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Object path to analyze. Slash-separated: 'Sales/Revenue'. DAX forms also accepted.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var upstreamOption = new Option<bool>("--upstream")
        {
            Description = "Show only upstream dependencies (what this object uses)"
        };
        var downstreamOption = new Option<bool>("--downstream")
        {
            Description = "Show only downstream dependents (what uses this object)"
        };
        var deepOption = new Option<bool>("--deep")
        {
            Description = "Show recursive dependency tree"
        };
        var unusedOption = new Option<bool>("--unused")
        {
            Description = "Find unreferenced measures and columns"
        };
        var hiddenOption = new Option<bool>("--hidden")
        {
            Description = "With --unused: only list unused objects whose IsHidden is true"
        };
        var maxDepthOption = new Option<int>("--max-depth")
        {
            Description = "Maximum depth for --deep traversal (default: 10)",
            DefaultValueFactory = _ => 10
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Disambiguate when the path matches multiple table-children."
        };
        typeOption.Aliases.Add("-t");

        var command = new Command("deps", "Analyze object dependencies (upstream/downstream)")
        {
            pathArgument,
            modelArgument,
            upstreamOption,
            downstreamOption,
            deepOption,
            unusedOption,
            hiddenOption,
            maxDepthOption,
            typeOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
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

            var result = await new DepsModelHandler(_providers).HandleAsync(
                new DepsModelRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    parseResult.GetValue(pathArgument),
                    type,
                    parseResult.GetValue(upstreamOption),
                    parseResult.GetValue(downstreamOption),
                    parseResult.GetValue(deepOption),
                    parseResult.GetValue(unusedOption),
                    parseResult.GetValue(hiddenOption),
                    parseResult.GetValue(maxDepthOption)),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(DepsModelResult result)
    {
        Console.WriteLine("Running semantic analysis...");
        Console.WriteLine();
        Console.WriteLine($"Dependencies for {result.Path} ({result.Type})");
        Console.WriteLine();
        RenderSection("Upstream (depends on)", result.Upstream);
        Console.WriteLine();
        RenderSection("Downstream (referenced by)", result.Downstream);
    }

    private static void RenderSection(string title, IReadOnlyList<DependencyObject> dependencies)
    {
        Console.WriteLine($"  {title}: {dependencies.Count}");
        if (dependencies.Count == 0)
        {
            Console.WriteLine("    None");
            return;
        }

        foreach (var dependency in dependencies)
            Console.WriteLine($"    {dependency.Type,-18} {dependency.Reference,-30} {dependency.Path}");
    }
}
