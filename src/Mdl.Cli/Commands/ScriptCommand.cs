using System.CommandLine;
using Mdl.App.Script;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class ScriptCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public ScriptCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model, Fabric path, or omit for active connection",
            Arity = ArgumentArity.ZeroOrOne
        };

        var scriptOption = new Option<string[]>("--script", "-S")
        {
            Description = "Path(s) to .cs or .csx script file(s). Can be repeated.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
            CustomParser = result => result.Tokens.Select(token => token.Value).ToArray()
        };

        var expressionOption = new Option<string[]>("--expression", "-e")
        {
            Description = "Inline C# expression(s) to execute. Use '-' to read from stdin.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
            CustomParser = result => result.Tokens.Select(token => token.Value).ToArray()
        };

        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save model to a different path after all scripts execute"
        };

        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder"
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Compile script and report errors without executing"
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Save even if this mutation introduces DAX validation errors"
        };

        var saveOption = new Option<bool>("--save")
        {
            Description = "Persist this command's mutation to the source location. Mutually exclusive with --revert and --stage."
        };

        var command = new Command("script", "Execute C# script(s) against a semantic model")
        {
            modelArgument,
            scriptOption,
            expressionOption,
            saveToOption,
            serializationOption,
            dryRunOption,
            forceOption,
            saveOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = parseResult.GetValue(GlobalOptions.ErrorFormat);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var scriptValues = CollectRepeatedValues("script", "-S", "--script");
            var expressionValues = CollectRepeatedValues("script", "-e", "--expression");
            var scriptFiles = scriptValues.Count > 0
                ? scriptValues
                : parseResult.GetValue(scriptOption) ?? [];
            var expressions = ResolveExpressions(expressionValues.Count > 0
                ? expressionValues
                : parseResult.GetValue(expressionOption) ?? []);
            var explicitModel = GlobalOptions.ModelValue(parseResult)
                ?? parseResult.GetValue(modelArgument)
                ?? CollectModelArgument("script");
            var result = await new ScriptHandler(_providers).HandleAsync(
                new ScriptRunRequest(
                    ModelSourceResolver.ResolveReference(
                        explicitModel,
                        parseResult.GetValue(GlobalOptions.Database)),
                    scriptFiles,
                    expressions,
                    parseResult.GetValue(dryRunOption),
                    parseResult.GetValue(forceOption),
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption)),
                cancellationToken);

            return CommandOutput.Render(
                result,
                formatValue,
                data => RenderText(data, formatValue),
                ToReferenceJson,
                renderCsv: data => RenderText(data, OutputFormats.Csv),
                errorFormat: errorFormat);
        });

        return command;
    }

    private static IReadOnlyList<string> ResolveExpressions(IReadOnlyList<string> expressions)
        => expressions.Select(expression => expression == "-" ? Console.In.ReadToEnd() : expression).ToList();

    private static IReadOnlyList<string> CollectRepeatedValues(string commandName, string shortName, string longName)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var commandIndex = Array.FindIndex(args, arg => string.Equals(arg, commandName, StringComparison.Ordinal));
        if (commandIndex < 0)
            return [];

        var values = new List<string>();
        for (var i = commandIndex + 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, shortName, StringComparison.Ordinal) ||
                string.Equals(arg, longName, StringComparison.Ordinal))
            {
                if (i + 1 < args.Length)
                    values.Add(args[++i]);
                continue;
            }

            var prefix = longName + "=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                values.Add(arg[prefix.Length..]);
        }

        return values;
    }

    private static string? CollectModelArgument(string commandName)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var commandIndex = Array.FindIndex(args, arg => string.Equals(arg, commandName, StringComparison.Ordinal));
        if (commandIndex < 0)
            return null;

        for (var i = commandIndex + 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (ValueOptions.Contains(arg))
            {
                i++;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains('=', StringComparison.Ordinal))
                continue;

            if (FlagOptions.Contains(arg) || arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            return arg;
        }

        return null;
    }

    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "-e",
        "--expression",
        "-S",
        "--script",
        "--save-to",
        "--serialization",
        "-m",
        "--model",
        "--output-format",
        "--error-format",
        "-s",
        "--server",
        "-d",
        "--database",
        "--auth",
        "--recent"
    };

    private static readonly HashSet<string> FlagOptions = new(StringComparer.Ordinal)
    {
        "--dry-run",
        "--force",
        "--save",
        "--local",
        "--debug",
        "--non-interactive"
    };

    private static void RenderText(ScriptRunResult result, string format)
    {
        Console.WriteLine($"Model: {result.ModelName}");

        if (result.DryRun)
        {
            foreach (var script in result.Scripts)
            {
                if (script.Success)
                {
                    Console.WriteLine($"Compilation OK: {script.Source}");
                    continue;
                }

                Console.WriteLine($"Compilation failed: {script.Source}");
                foreach (var error in script.Errors)
                    Console.WriteLine(error);
            }

            return;
        }

        for (var i = 0; i < result.Inputs.Count; i++)
        {
            var input = result.Inputs[i];
            Console.WriteLine(result.Inputs.Count == 1
                ? $"Script: {input.Source}"
                : $"Script {i + 1}/{result.Inputs.Count}: {input.Source}");

            if (OutputFormats.IsTextLike(format))
                Console.WriteLine($"Running {input.Source}...");

            if (i < result.Messages.Count)
                Console.WriteLine(result.Messages[i].Text);
        }

        if (!result.Success)
        {
            foreach (var error in result.CompileErrors)
                Console.WriteLine(error);

            if (!string.IsNullOrWhiteSpace(result.RuntimeError))
                Console.WriteLine(result.RuntimeError);

            return;
        }

        Console.WriteLine($"Done: {result.ScriptsExecuted} script(s) executed.");
        if (result.Saved is bool saved && saved == false)
            Console.WriteLine("Changes not saved. Use --save to persist.");
    }

    private static object ToReferenceJson(ScriptRunResult result)
    {
        if (result.DryRun)
            return new
            {
                dryRun = true,
                scripts = result.Scripts.Select(script => new
                {
                    source = script.Source,
                    success = script.Success,
                    errors = script.Errors
                })
            };

        if (!result.Success)
            return new
            {
                success = false,
                durationMs = result.DurationMs,
                failedScript = result.FailedScript,
                scriptIndex = result.ScriptIndex,
                compileErrors = result.CompileErrors,
                runtimeError = result.RuntimeError,
                messages = result.Messages
            };

        if (result.Staged is null)
        {
            return new
            {
                success = true,
                durationMs = result.DurationMs,
                scriptsExecuted = result.ScriptsExecuted,
                messages = result.Messages,
                saved = result.Saved
            };
        }

        return new
        {
            success = true,
            durationMs = result.DurationMs,
            scriptsExecuted = result.ScriptsExecuted,
            messages = result.Messages,
            saved = result.Saved,
            staged = result.Staged
        };
    }
}
