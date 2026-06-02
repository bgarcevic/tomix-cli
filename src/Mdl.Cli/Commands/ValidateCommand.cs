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
            noMultilineOption,
            serverOnlyOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var errorsOnly = parseResult.GetValue(errorsOnlyOption);

            var result = await new ValidateModelHandler(_providers).HandleAsync(
                new ValidateModelRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    errorsOnly,
                    parseResult.GetValue(noWarningsOption) || errorsOnly,
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
                data => Render(data, errorsOnly, parseResult.GetValue(noMultilineOption), includeBanner: !OutputFormats.IsCsv(format)));
        });

        return command;
    }

    private static void Render(
        ValidateModelResult result,
        bool errorsOnly,
        bool noMultiline,
        bool includeBanner)
    {
        if (includeBanner)
            Console.WriteLine("Validating...");
        Console.WriteLine("Validating: (unnamed)");
        Console.WriteLine();

        if (result.Valid)
        {
            Console.WriteLine("No validation errors found.");
            return;
        }

        var allRows = new List<(string Kind, string Message, string Object, string Line)>();

        foreach (var error in result.Errors)
        {
            var message = noMultiline ? error.Message.ReplaceLineEndings(" ") : error.Message;
            allRows.Add(("Error", message, error.ObjectName, error.Expression ?? ""));
        }

        if (!errorsOnly)
        {
            foreach (var warning in result.Warnings)
            {
                var message = noMultiline ? warning.Message.ReplaceLineEndings(" ") : warning.Message;
                allRows.Add(("Warning", message, warning.ObjectName, warning.Expression ?? ""));
            }
        }

        var hasErrors = result.Errors.Count > 0;
        var hasWarnings = !errorsOnly && result.Warnings.Count > 0;

        if (hasErrors)
        {
            Console.WriteLine("Errors");
            RenderTable(allRows.Where(r => r.Kind == "Error").ToList());
        }

        if (hasWarnings)
        {
            Console.WriteLine("Warnings");
            RenderTable(allRows.Where(r => r.Kind == "Warning").ToList());
        }

        Console.WriteLine();
        Console.WriteLine($"  Errors:        {result.Errors.Count}");

        if (!errorsOnly)
        {
            Console.WriteLine($"  Warnings:      {result.Warnings.Count}");
        }

        Console.WriteLine("  Anti-patterns: 0");
    }

    private static void RenderTable(IReadOnlyList<(string Kind, string Message, string Object, string Line)> rows)
    {
        if (rows.Count == 0)
            return;

        var msgWidth = Math.Max("Message".Length, rows.Max(r => r.Message.Length)) + 1;
        var objWidth = Math.Max("Object".Length, rows.Max(r => r.Object.Length));
        var lineWidth = Math.Max("Line".Length, rows.Max(r => r.Line.Length)) + 2;
        var tableWidth = 2 + msgWidth + 1 + (objWidth + 2) + 1 + (lineWidth + 1);
        var blank = new string(' ', tableWidth);

        Console.WriteLine(blank);
        Console.WriteLine($"  {"Message".PadRight(msgWidth)}│ {"Object".PadRight(objWidth)} │ {"Line".PadRight(lineWidth)}");
        Console.WriteLine($" {new string('─', msgWidth + 1)}┼{new string('─', objWidth + 2)}┼{new string('─', lineWidth)} ");

        foreach (var row in rows)
            Console.WriteLine($"  {row.Message.PadRight(msgWidth)}│ {row.Object.PadRight(objWidth)} │ {row.Line.PadRight(lineWidth)}");

        Console.WriteLine(blank);
    }

    private static void EmitCi(string? ci, ValidateModelResult result)
    {
        if (string.IsNullOrWhiteSpace(ci) || result.Valid)
            return;

        if (ci.Equals("github", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"::error::{error.Message} [{error.ObjectName}] ({error.Code})");
        }
        else if (ci.Equals("vsts", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"##vso[task.logissue type=error;]{error.Message} [{error.ObjectName}] ({error.Code})");

            Console.Error.WriteLine("##vso[task.complete result=Failed;]Done.");
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
