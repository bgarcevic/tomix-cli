using System.CommandLine;
using System.Xml.Linq;
using Mdl.App.Validate;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class ValidateCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public ValidateCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var ciOption = new Option<string?>("--ci")
        {
            Description = "Emit CI logging commands to stderr: vsts or github"
        };
        var trxOption = new Option<string?>("--trx")
        {
            Description = "Write results as a VSTEST .trx file to the specified path"
        };
        var errorsOnlyOption = new Option<bool>("--errors-only")
        {
            Description = "Only show errors"
        };
        var noWarningsOption = new Option<bool>("--no-warnings")
        {
            Description = "Hide warnings from the semantic analyzer"
        };
        var noAntipatternsOption = new Option<bool>("--no-antipatterns")
        {
            Description = "Hide anti-pattern suggestions"
        };
        var noMultilineOption = new Option<bool>("--no-multiline")
        {
            Description = "Collapse multi-line cell content to a single line. Text output only."
        };
        var serverOnlyOption = new Option<bool>("--server-only")
        {
            Description = "Only show errors reported by the connected server"
        };

        var command = new Command("validate", "Validate DAX expressions and relationship integrity (--ci for CI output, --trx for VSTEST)")
        {
            modelArgument,
            ciOption,
            trxOption,
            errorsOnlyOption,
            noWarningsOption,
            noAntipatternsOption,
            noMultilineOption,
            serverOnlyOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var result = await new ValidateModelHandler(_providers).HandleAsync(
                new ValidateModelRequest(
                    new ModelReference(ModelSourceResolver.Resolve(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument))),
                    parseResult.GetValue(errorsOnlyOption),
                    parseResult.GetValue(noWarningsOption) || parseResult.GetValue(errorsOnlyOption),
                    parseResult.GetValue(noAntipatternsOption) || parseResult.GetValue(errorsOnlyOption),
                    parseResult.GetValue(serverOnlyOption)),
                cancellationToken);

            if (result.Data is not null)
            {
                var trx = parseResult.GetValue(trxOption);
                if (!string.IsNullOrWhiteSpace(trx))
                    WriteTrx(trx, result.Data);

                EmitCi(parseResult.GetValue(ciOption), result.Data);
            }

            return CommandOutput.Render(
                result,
                format,
                data => Render(data, parseResult.GetValue(noMultilineOption)));
        });

        return command;
    }

    private static void Render(ValidateModelResult result, bool noMultiline)
    {
        Console.WriteLine("Validating...");
        Console.WriteLine("Validating: (unnamed)");
        Console.WriteLine();

        if (result.Valid)
        {
            Console.WriteLine("No validation errors found.");
            return;
        }

        Console.WriteLine("Errors");
        foreach (var error in result.Errors)
        {
            var message = noMultiline ? error.Message.ReplaceLineEndings(" ") : error.Message;
            Console.WriteLine($"  {message} | {error.ObjectName}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Errors:        {result.Errors.Count}");
        Console.WriteLine($"  Warnings:      {result.Warnings.Count}");
        Console.WriteLine($"  Anti-patterns: {result.Antipatterns.Count}");
    }

    private static void EmitCi(string? ci, ValidateModelResult result)
    {
        if (string.IsNullOrWhiteSpace(ci) || result.Valid)
            return;

        foreach (var error in result.Errors)
        {
            if (ci.Equals("github", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"::error::{error.Message} [{error.ObjectName}] ({error.Code})");
            else if (ci.Equals("vsts", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"##vso[task.logissue type=error;code={error.Code}]{error.Message} [{error.ObjectName}]");
        }
    }

    private static void WriteTrx(string path, ValidateModelResult result)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var doc = new XDocument(
            new XElement("TestRun",
                new XAttribute("name", "mdl validate"),
                new XElement("ResultSummary",
                    new XAttribute("outcome", result.Valid ? "Passed" : "Failed"))));
        doc.Save(fullPath);
    }
}
