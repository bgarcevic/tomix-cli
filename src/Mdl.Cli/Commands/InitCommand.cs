using System.CommandLine;
using Mdl.App.Init;
using Mdl.Cli.Output;

namespace Mdl.Cli.Commands;

internal sealed class InitCommand : ICommandModule
{
    public Command Build()
    {
        var outputPathArgument = new Argument<string>("output-path")
        {
            Description = "Directory to create the model in (omit to use the global --model path)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var compatibilityLevelOption = new Option<int?>("--compatibility-level")
        {
            Description = "Compatibility level (default: 1702 when mode is PowerBI, 1500 otherwise)."
        };
        compatibilityLevelOption.Aliases.Add("--compat");

        var compatibilityModeOption = new Option<string?>("--compatibility-mode")
        {
            Description = "Compatibility mode: AnalysisServices, PowerBI. Default: PowerBI."
        };

        var nameOption = new Option<string?>("--name")
        {
            Description = "Model/database name (default: directory name)"
        };

        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder, pbip (default: tmdl)"
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Replace any existing file or directory at the target path"
        };

        var command = new Command("init", "Create a new empty semantic model")
        {
            outputPathArgument,
            compatibilityLevelOption,
            compatibilityModeOption,
            nameOption,
            serializationOption,
            forceOption
        };

        command.SetAction(parseResult =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var outputPath = parseResult.GetValue(outputPathArgument) ??
                             GlobalOptions.ModelValue(parseResult) ??
                             "";

            var result = new InitModelHandler().Handle(new InitModelRequest(
                outputPath,
                parseResult.GetValue(nameOption),
                parseResult.GetValue(serializationOption) ?? "",
                parseResult.GetValue(compatibilityModeOption) ?? "",
                parseResult.GetValue(compatibilityLevelOption),
                parseResult.GetValue(forceOption)));

            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(InitModelResult result)
    {
        Console.WriteLine($"Created: {result.Created}");
        Console.WriteLine($"  Name:   {result.Name}");
        Console.WriteLine($"  Format: {result.Format}");
        Console.WriteLine($"  Compat: {result.CompatibilityLevel} ({result.CompatibilityMode})");
    }
}
