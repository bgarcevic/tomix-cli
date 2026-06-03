using System.CommandLine;
using Mdl.App.Format;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class FormatCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly IExpressionFormatterClient _formatter;

    public FormatCommand(
        IReadOnlyList<IModelProvider> providers,
        IExpressionFormatterClient formatter)
    {
        _providers = providers;
        _formatter = formatter;
    }

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var expressionOption = new Option<string?>("--expression")
        {
            Description = "Format an inline expression"
        };
        expressionOption.Aliases.Add("-e");
        var pathOption = new Option<string?>("--path")
        {
            Description = "Format the expression on a model object path"
        };
        pathOption.Aliases.Add("-p");
        var semicolonsOption = new Option<bool>("--semicolons")
        {
            Description = "Use semicolons as DAX list separators"
        };
        var longOption = new Option<bool>("--long")
        {
            Description = "Prefer long-line formatting"
        };
        var noSpaceAfterFunctionOption = new Option<bool>("--no-space-after-function")
        {
            Description = "Do not insert a space between a DAX function name and '('"
        };
        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save formatted model to a different path"
        };
        var langOption = new Option<string?>("--lang")
        {
            Description = "Expression language: dax or m"
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Disambiguate object type"
        };
        typeOption.Aliases.Add("-t");
        var saveOption = new Option<bool>("--save")
        {
            Description = "Persist formatted expressions to the source model"
        };
        var stageOption = new Option<bool>("--stage")
        {
            Description = "Stage this command's mutation"
        };
        var revertOption = new Option<bool>("--revert")
        {
            Description = "Revert a staged mutation"
        };

        var command = new Command("format", "Format DAX or M/Power Query expressions (-e inline, -p object path, or all)")
        {
            modelArgument,
            expressionOption,
            pathOption,
            semicolonsOption,
            longOption,
            noSpaceAfterFunctionOption,
            saveToOption,
            langOption,
            typeOption,
            saveOption,
            stageOption,
            revertOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var typeValue = parseResult.GetValue(typeOption);
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

            var expression = InputValueResolver.Resolve(parseResult.GetValue(expressionOption));
            var result = await new FormatModelHandler(_providers, _formatter).HandleAsync(
                new FormatModelRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    expression,
                    parseResult.GetValue(pathOption),
                    parseResult.GetValue(langOption) ?? "",
                    type,
                    parseResult.GetValue(longOption),
                    parseResult.GetValue(semicolonsOption),
                    parseResult.GetValue(noSpaceAfterFunctionOption),
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption)),
                cancellationToken);

            return CommandOutput.Render(
                result,
                formatValue,
                Render,
                data => (object)data);
        });

        return command;
    }

    private static void Render(FormatModelResult result)
    {
        switch (result)
        {
            case InlineFormatResult inline:
                Console.WriteLine(inline.Formatted);
                foreach (var error in inline.Errors)
                    Console.Error.WriteLine(error);
                break;

            case ObjectFormatResult obj:
                Console.WriteLine(obj.Formatted);
                break;

            case ModelFormatResult model:
                Console.WriteLine($"Formatted: {model.Formatted}");
                Console.WriteLine($"Unchanged: {model.Unchanged}");
                Console.WriteLine($"Failed: {model.Failed}");
                break;
        }
    }
}
