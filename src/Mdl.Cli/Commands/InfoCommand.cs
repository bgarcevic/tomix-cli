using System.CommandLine;
using Mdl.App.Info;
using Mdl.Cli.Output;
using Mdl.Core.Models;
using Spectre.Console;

namespace Mdl.Cli.Commands;

internal sealed class InfoCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public InfoCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to the semantic model folder."
        };

        var format = OutputFormats.CreateOption();

        var command = new Command("info", "Show a summary of a semantic model.")
        {
            modelArgument,
            format
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(modelArgument) ?? "";
            var formatValue = parseResult.GetValue(format) ?? OutputFormats.Text;

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var handler = new InfoModelHandler(_providers);
            var result = await handler.HandleAsync(new InfoModelRequest(new ModelReference(path)), cancellationToken);

            return CommandOutput.Render(result, formatValue, data => Render(data.Summary));
        });

        return command;
    }

    private static void Render(ModelSummary s)
    {
        AnsiConsole.MarkupLine(Styling.Title(s.Name));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(Styling.KeyValue("Compatibility level: ", s.CompatibilityLevel.ToString()));
        AnsiConsole.MarkupLine(Styling.KeyValue("Tables:              ", s.Tables.ToString()));
        AnsiConsole.MarkupLine(Styling.KeyValue("Columns:             ", s.Columns.ToString()));
        AnsiConsole.MarkupLine(Styling.KeyValue("Measures:            ", s.Measures.ToString()));
        AnsiConsole.MarkupLine(Styling.KeyValue("Relationships:       ", s.Relationships.ToString()));
        AnsiConsole.MarkupLine(Styling.KeyValue("Roles:               ", s.Roles.ToString()));
    }
}
