using System.CommandLine;
using Mdl.App.Diff;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class DiffCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public DiffCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var leftArgument = new Argument<string>("left")
        {
            Description = "Path to first model (TMDL folder, .bim file)"
        };

        var rightArgument = new Argument<string>("right")
        {
            Description = "Path to second model (TMDL folder, .bim file)"
        };

        var command = new Command(
            "diff",
            "Compare two semantic models and show structural differences. Exit codes: 0 = identical, 1 = differences found, 2 = error")
        {
            leftArgument,
            rightArgument
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = parseResult.GetValue(GlobalOptions.ErrorFormat);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var left = parseResult.GetValue(leftArgument) ?? "";
            var right = parseResult.GetValue(rightArgument) ?? "";
            var result = await new DiffModelHandler(_providers).HandleAsync(
                new DiffModelRequest(
                    new ModelReference(left),
                    new ModelReference(right)),
                cancellationToken);

            return CommandOutput.Render(
                result,
                formatValue,
                errorFormat,
                data => Render(data, left, right, includeComparingLine: !OutputFormats.IsCsv(formatValue)));
        });

        return command;
    }

    private static void Render(
        DiffModelResult result,
        string left,
        string right,
        bool includeComparingLine)
    {
        if (includeComparingLine)
            Console.WriteLine("Comparing models...");

        if (!result.HasChanges)
        {
            Console.WriteLine("Models are identical");
            return;
        }

        Console.WriteLine($"Left:  {left}");
        Console.WriteLine($"Right: {right}");
        Console.WriteLine(
            $"{result.Summary.Added} added, {result.Summary.Removed} removed, {result.Summary.Modified} modified");
        Console.WriteLine();

        foreach (var change in result.Changes)
            RenderChange(change);
    }

    private static void RenderChange(DiffChange change)
    {
        switch (change.Action)
        {
            case "added":
                Console.WriteLine($"  + {change.ObjectType} {change.Path}");
                break;
            case "removed":
                Console.WriteLine($"  - {change.ObjectType} {change.Path}");
                break;
            case "modified":
                Console.WriteLine($"  ~ {change.ObjectType} {change.Path}");
                Console.WriteLine($"    - {change.OldValue}");
                Console.WriteLine($"    + {change.NewValue}");
                break;
        }
    }
}
