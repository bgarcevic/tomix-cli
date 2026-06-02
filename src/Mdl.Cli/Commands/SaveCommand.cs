using System.CommandLine;
using Mdl.App.Save;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class SaveCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public SaveCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model, Fabric path, or omit for active connection",
            Arity = ArgumentArity.ZeroOrOne
        };
        var outputPathOption = new Option<string?>("--output-path")
        {
            Description = "File system path to write the model to. Omit to save the loaded model back to its source."
        };
        outputPathOption.Aliases.Add("-o");

        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl (default), bim, te-folder, pbip, database.json. Defaults to the loaded model's format."
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Skip validation and overwrite existing output. Does not override layout-safety refusals."
        };
        var skipBpaOption = new Option<bool>("--skip-bpa")
        {
            Description = "Skip BPA gate check (configured via .te-bpa.json)"
        };
        var fixBpaOption = new Option<bool>("--fix-bpa")
        {
            Description = "Auto-fix BPA violations before saving (applies FixExpressions where available)"
        };
        var bpaRulesOption = new Option<string[]>("--bpa-rules")
        {
            Description = "Path(s) to BPA rule file(s) for this save. Overrides bpa.rules in CLI config.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var skipValidationOption = new Option<bool>("--skip-validation")
        {
            Description = "Skip DAX semantic validation. Faster for pure download-and-save scenarios."
        };
        var supportingFilesOption = new Option<bool>("--supporting-files")
        {
            Description = "Wrap output in a {modelName}.SemanticModel/ folder with .platform and definition.pbism. Only for tmdl/bim on bare targets."
        };

        var command = new Command("save", "Save a model to disk in a specified format (like fab export)")
        {
            modelArgument,
            outputPathOption,
            serializationOption,
            forceOption,
            skipBpaOption,
            fixBpaOption,
            bpaRulesOption,
            skipValidationOption,
            supportingFilesOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = parseResult.GetValue(GlobalOptions.ErrorFormat);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var outputPath = parseResult.GetValue(outputPathOption);
            var serialization = parseResult.GetValue(serializationOption) ?? "";
            var force = parseResult.GetValue(forceOption);
            var supportingFiles = parseResult.GetValue(supportingFilesOption);
            var fixBpa = parseResult.GetValue(fixBpaOption);
            var bpaRules = parseResult.GetValue(bpaRulesOption);

            var reference = ModelSourceResolver.ResolveReference(
                GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                parseResult.GetValue(GlobalOptions.Database));

            var result = await new SaveModelHandler(_providers).HandleAsync(
                new SaveModelRequest(
                    reference,
                    outputPath,
                    serialization,
                    force,
                    supportingFiles,
                    fixBpa,
                    bpaRules),
                cancellationToken);

            return CommandOutput.Render(
                result,
                formatValue,
                errorFormat,
                data => Render(data, reference.Value),
                RenderCsv);
        });

        return command;
    }

    private static void Render(SaveModelResult result, string source)
    {
        Console.WriteLine($"Source: {source}");
        Console.WriteLine($"Saving ({result.Format})...");
        Console.WriteLine($"Saved: {result.Saved} ({result.Format})");
    }

    private static void RenderCsv(SaveModelResult result)
        => Console.WriteLine($"Saved: {result.Saved} ({result.Format})");
}
