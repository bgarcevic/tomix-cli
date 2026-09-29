using System.CommandLine;
using System.Globalization;
using Spectre.Console;
using Tomix.App.State;
using Tomix.App.Summary;
using Tomix.Cli.Output;
using Tomix.Core.Models;

namespace Tomix.Cli.Commands;

internal sealed class SummaryCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    private readonly CliStateStore _state;

    public SummaryCommand(IReadOnlyList<IModelProvider> providers, CliStateStore state)
    {
        _providers = providers;
        _state = state;
    }

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Model path, Fabric path, or omit to use the active connection",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("summary", "Show where a model lives and what it contains")
        {
            modelArgument
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!RecentConnections.TryResolveModel(
                    parseResult,
                    GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                    _state,
                    out var reference,
                    out var recentExit))
                return recentExit;
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = GlobalOptions.ErrorFormatValue(parseResult, formatValue);

            if (!CommandOutput.TryValidateFormat(parseResult, formatValue, "summary", OutputFormats.Text, OutputFormats.Json))
                return 2;

            var quiet = parseResult.GetValue(GlobalOptions.Quiet);
            var result = await CliSpinner.RunAsync(
                "Loading model...",
                () => new SummaryModelHandler(_providers).HandleAsync(
                    new SummaryModelRequest(reference),
                    cancellationToken),
                suppress: quiet || OutputFormats.IsJson(formatValue));

            return CommandOutput.Render(result, formatValue, Render, data => data, renderCsv: null, errorFormat: errorFormat);
        });

        return command;
    }

    internal static void Render(SummaryModelResult result)
    {
        AnsiConsole.MarkupLine(Styling.Title(result.Name));
        Line("source", result.Source);
        if (result.Database is not null)
            Line("database", result.Database);
        if (result.Format is not null)
            Line("format", result.Format);
        Line("compatibilityLevel", result.CompatibilityLevel);
        if (result.Culture is not null)
            Line("culture", result.Culture);
        if (result.DefaultMode is not null)
            Line("defaultMode", result.DefaultMode);

        var counts = result.Counts;
        Line("tables", counts.Tables);
        Line("columns", counts.Columns);
        Line("measures", counts.Measures);
        Line("relationships", counts.Relationships);
        Line("roles", counts.Roles);
        Line("partitions", counts.Partitions);
        Line("calculationGroups", counts.CalculationGroups);
        Line("perspectives", counts.Perspectives);
        Line("cultures", counts.Cultures);
    }

    private static void Line(string label, int value)
        => Line(label, value.ToString(CultureInfo.InvariantCulture));

    // Labels are the JSON keys, padded so the values line up. Plain Console output (as in 'get'):
    // Spectre would wrap a long source path at the console width.
    private static void Line(string label, string value)
        => Console.WriteLine($"  {label + ":",-20} {value}");
}
