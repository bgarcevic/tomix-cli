using System.CommandLine;
using Mdl.App.Bpa;
using Mdl.Cli.Output;
using Mdl.Core.Bpa;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class BpaCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public BpaCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var command = new Command("bpa", "Best Practice Analyzer: run rules and manage rule collections");
        command.Subcommands.Add(BuildRunCommand());
        command.Subcommands.Add(BuildRulesCommand());
        return command;
    }

    private Command BuildRunCommand()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model (if not using --model)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var rulesOption = new Option<string[]>("--rules", "-r")
        {
            Description = "Path(s) to BPA rule file(s) in JSON format",
            AllowMultipleArgumentsPerToken = true
        };

        var noModelRulesOption = new Option<bool>("--no-model-rules")
        {
            Description = "Exclude BPA rules embedded in the model's annotations"
        };

        var noDefaultsOption = new Option<bool>("--no-defaults")
        {
            Description = "Exclude built-in default BPA rules"
        };

        var failOnOption = new Option<string?>("--fail-on")
        {
            Description = "Failure threshold: error (default) or warning"
        };

        var fixOption = new Option<bool>("--fix")
        {
            Description = "Apply fix expressions to auto-fix violations where possible"
        };

        var ruleOption = new Option<string[]>("--rule")
        {
            Description = "Run only specific rule(s) by ID",
            AllowMultipleArgumentsPerToken = true
        };

        var ciOption = new Option<string?>("--ci")
        {
            Description = "Emit CI logging commands to stderr: vsts or github"
        };

        var pathOption = new Option<string?>("--path")
        {
            Description = "Limit analysis to matched objects (literal names, wildcards, or paths)"
        };

        var noMultilineOption = new Option<bool>("--no-multiline")
        {
            Description = "Collapse multi-line cell content in the violations table to a single line"
        };

        var runCommand = new Command("run", "Run BPA rules against a model (--fix to auto-fix)")
        {
            modelArgument,
            rulesOption,
            noModelRulesOption,
            noDefaultsOption,
            failOnOption,
            fixOption,
            ruleOption,
            ciOption,
            pathOption,
            noMultilineOption
        };

        runCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var ruleFiles = parseResult.GetValue(rulesOption);
            var ruleIds = parseResult.GetValue(ruleOption);

            var result = await new BpaRunHandler(_providers).HandleAsync(
                new BpaRunRequest(
                    ModelSourceResolver.ResolveReference(
                        GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                        parseResult.GetValue(GlobalOptions.Database)),
                    ruleFiles,
                    parseResult.GetValue(noDefaultsOption),
                    parseResult.GetValue(pathOption),
                    ruleIds,
                    parseResult.GetValue(fixOption)),
                cancellationToken);

            var ci = parseResult.GetValue(ciOption);

            if (result.Data is not null)
                EmitCi(ci, result.Data.Violations);

            if (!string.IsNullOrWhiteSpace(ci))
                return result.ExitCode;

            return CommandOutput.Render(
                result,
                format,
                data => RenderRun(data, parseResult.GetValue(noMultilineOption)));
        });

        return runCommand;
    }

    private Command BuildRulesCommand()
    {
        var rulesFileOption = new Option<string?>("--rules-file")
        {
            Description = "Path to a BPA rules JSON file"
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "Include disabled/ignored rules"
        };

        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model",
            Arity = ArgumentArity.ZeroOrOne
        };

        var rulesCommand = new Command("rules", "Manage BPA rule collections (model annotations or local files)")
        {
            rulesFileOption,
            allOption,
            modelArgument
        };

        var listCommand = new Command("list", "List BPA rules from all sources with status")
        {
            modelArgument,
            allOption
        };

        listCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var result = await new BpaRulesListHandler().HandleAsync(
                new BpaRulesListRequest(
                    All: parseResult.GetValue(allOption)),
                cancellationToken);

            return CommandOutput.Render(
                result,
                format,
                RenderRulesList);
        });

        rulesCommand.Subcommands.Add(listCommand);
        return rulesCommand;
    }

    private static void RenderRun(BpaRunResult result, bool noMultiline)
    {
        Console.WriteLine($"Running BPA analysis on '{result.ModelName}'...");
        Console.WriteLine();

        if (result.Violations.Count == 0)
        {
            Console.WriteLine("No BPA violations found.");
            return;
        }

        var rows = result.Violations.Select(v =>
        {
            var desc = CollapseDescription(v.Description);
            return (RuleName: v.RuleName, Severity: v.Severity, SeverityText: v.Severity.ToString(), ObjectType: v.ObjectType, ObjectName: v.ObjectName, desc);
        }).ToList();

        var nameWidth = Math.Max("Rule".Length, rows.Max(r => Math.Min(r.RuleName.Length, 50)));
        var sevWidth = Math.Max("Severity".Length, rows.Max(r => r.SeverityText.Length));
        var typeWidth = Math.Max("Type".Length, rows.Max(r => Math.Min(r.ObjectType.Length, 20)));
        var objWidth = Math.Max("Object".Length, rows.Max(r => Math.Min(r.ObjectName.Length, 40)));
        var descWidth = Math.Max("Description".Length, Math.Min(rows.Max(r => r.desc.Length), 60));

        Console.WriteLine($"  {"Rule".PadRight(nameWidth)}   {"Severity".PadRight(sevWidth)}   {"Type".PadRight(typeWidth)}   {"Object".PadRight(objWidth)}   {"Description".PadRight(descWidth)}");
        Console.WriteLine($"  {new string('-', nameWidth)}   {new string('-', sevWidth)}   {new string('-', typeWidth)}   {new string('-', objWidth)}   {new string('-', descWidth)}");

        foreach (var row in rows)
        {
            var name = Truncate(row.RuleName, nameWidth);
            var sev = SeverityColored(Truncate(row.SeverityText, sevWidth), row.Severity);
            var type = Truncate(row.ObjectType, typeWidth);
            var obj = Truncate(row.ObjectName, objWidth);
            var desc = Truncate(row.desc, descWidth);
            Console.WriteLine($"  {name.PadRight(nameWidth)}   {sev.PadRight(sevWidth + AnsiOverhead(row.Severity))}   {type.PadRight(typeWidth)}   {obj.PadRight(objWidth)}   {desc}");
        }

        Console.WriteLine();
        var errorCount = result.Violations.Count(v => v.Severity == BpaSeverity.Error);
        var warningCount = result.Violations.Count(v => v.Severity == BpaSeverity.Warning);
        var infoCount = result.Violations.Count(v => v.Severity == BpaSeverity.Info);
        Console.WriteLine($"  Rules evaluated:  {result.RulesEvaluated}");
        Console.WriteLine($"  Violations:       {result.Violations.Count}  ({SeverityColored($"{errorCount} errors", BpaSeverity.Error)}, {SeverityColored($"{warningCount} warnings", BpaSeverity.Warning)}, {infoCount} info)");

        var byCategory = result.Violations.GroupBy(v => v.Category)
            .OrderByDescending(g => g.Count());
        foreach (var group in byCategory)
            Console.WriteLine($"    {group.Key}: {group.Count()}");

        if (result.DurationMs > 0)
            Console.WriteLine($"  Duration:         {result.DurationMs}ms");
    }

    private static string CollapseDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        var firstLine = description.Split('\n', 2)[0].TrimEnd('\r');

        var refIdx = firstLine.IndexOf("Reference:", StringComparison.OrdinalIgnoreCase);
        if (refIdx > 0)
            firstLine = firstLine[..refIdx].TrimEnd();

        return firstLine;
    }

    private static string SeverityColored(string text, BpaSeverity severity)
    {
        if (!Console.IsOutputRedirected)
        {
            return severity switch
            {
                BpaSeverity.Error => $"\e[1;31m{text}\e[0m",
                BpaSeverity.Warning => $"\e[1;33m{text}\e[0m",
                _ => $"\e[36m{text}\e[0m"
            };
        }

        return text;
    }

    private static int AnsiOverhead(BpaSeverity severity) => Console.IsOutputRedirected ? 0 : 9;

    private static void RenderRulesList(BpaRulesListResult result)
    {
        if (result.Rules.Count == 0)
        {
            Console.WriteLine("No BPA rules available.");
            return;
        }

        var rows = result.Rules.Select(r => (r.Id, r.Name, r.Category, r.Severity, r.Scope)).ToList();

        var idWidth = Math.Max("ID".Length, rows.Max(r => Math.Min(r.Id.Length, 45)));
        var nameWidth = Math.Max("Name".Length, rows.Max(r => Math.Min(r.Name.Length, 55)));
        var catWidth = Math.Max("Category".Length, rows.Max(r => Math.Min(r.Category.Length, 20)));
        var sevWidth = Math.Max("Severity".Length, rows.Max(r => r.Severity.Length));

        Console.WriteLine($"  {"ID".PadRight(idWidth)}   {"Name".PadRight(nameWidth)}   {"Category".PadRight(catWidth)}   {"Severity".PadRight(sevWidth)}");
        Console.WriteLine($"  {new string('-', idWidth)}   {new string('-', nameWidth)}   {new string('-', catWidth)}   {new string('-', sevWidth)}");

        foreach (var row in rows)
        {
            Console.WriteLine($"  {Truncate(row.Id, idWidth).PadRight(idWidth)}   {Truncate(row.Name, nameWidth).PadRight(nameWidth)}   {Truncate(row.Category, catWidth).PadRight(catWidth)}   {row.Severity}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total rules: {result.Rules.Count}");
    }

    private static void EmitCi(string? ci, IReadOnlyList<BpaViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(ci) || violations.Count == 0)
            return;

        foreach (var v in violations)
        {
            var msg = $"{v.RuleName}: {v.ObjectType} '{v.ObjectName}'";
            if (!string.IsNullOrWhiteSpace(v.Description))
            {
                var shortDesc = CollapseDescription(v.Description);
                msg += $" - {shortDesc}";
            }

            if (ci.Equals("github", StringComparison.OrdinalIgnoreCase))
            {
                var level = v.Severity == BpaSeverity.Error ? "error" : "warning";
                Console.Error.WriteLine($"::{level}::{msg} [{v.RuleId}]");
            }
            else if (ci.Equals("vsts", StringComparison.OrdinalIgnoreCase))
            {
                var type = v.Severity == BpaSeverity.Error ? "error" : "warning";
                Console.Error.WriteLine($"##vso[task.logissue type={type};]{msg} [{v.RuleId}]");
            }
        }

        var errors = violations.Count(v => v.Severity == BpaSeverity.Error);
        if (errors > 0 && ci.Equals("vsts", StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine("##vso[task.complete result=Failed;]Done.");
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
