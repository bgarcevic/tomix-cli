using System.CommandLine;
using Mdl.App.Info;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class LoadCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public LoadCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model, Fabric path, or omit for active connection",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("load", "Load a semantic model and display summary")
        {
            modelArgument
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(modelArgument);
            var reference = ModelSourceResolver.ResolveReference(
                GlobalOptions.ModelValue(parseResult) ?? path,
                parseResult.GetValue(GlobalOptions.Database));
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var handler = new InfoModelHandler(_providers);
            var result = await handler.HandleAsync(
                new InfoModelRequest(reference),
                cancellationToken);

            return CommandOutput.Render(result, formatValue, Render, data => data.Summary);
        });

        return command;
    }

    private static void Render(InfoModelResult result)
    {
        var summary = result.Summary;
        Console.WriteLine($"Loaded: {summary.Name}");
        if (summary.DatabaseName is null)
            Console.WriteLine($"  name:          {summary.Name}");
        Console.WriteLine($"  database:      {summary.DatabaseName ?? ""}");
        Console.WriteLine($"  compatLevel:   {summary.CompatibilityLevel}");
        Console.WriteLine($"  tables:        {summary.Tables}");
        Console.WriteLine($"  measures:      {summary.Measures}");
        Console.WriteLine($"  columns:       {summary.Columns}");
        Console.WriteLine($"  relationships: {summary.Relationships}");
    }
}
