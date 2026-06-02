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
        command.Subcommands.Add(BuildRulesCommand());
        command.Subcommands.Add(BuildRunCommand());
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
            Description = "Path(s) or URL(s) to BPA rule file(s) in JSON format",
            AllowMultipleArgumentsPerToken = true
        };

        var rulesetOption = new Option<string?>("--ruleset")
        {
            Description = $"Standard BPA ruleset to use ({string.Join(", ", BpaRuleLoader.KnownRulesets)})"
        };

        var noModelRulesOption = new Option<bool>("--no-model-rules")
        {
            Description = "Exclude BPA rules embedded in the model's annotations"
        };

        var noDefaultsOption = new Option<bool>("--no-defaults")
        {
            Description = "Exclude the selected standard BPA ruleset"
        };

        var failOnOption = new Option<string?>("--fail-on")
        {
            Description = "Failure threshold: error (default) or warning"
        };

        var vpaxOption = new Option<string?>("--vpax")
        {
            Description = "Load VertiPaq Analyzer stats from a .vpax file to enable VPA-aware rules"
        };

        var vpaRulesOption = new Option<bool>("--vpa-rules")
        {
            Description = "Include built-in VPA-aware rules"
        };

        var fixOption = new Option<bool>("--fix")
        {
            Description = "Apply fix expressions to auto-fix violations where possible"
        };

        var saveOption = new Option<bool>("--save")
        {
            Description = "Save model back to source after applying fixes"
        };

        var saveToOption = new Option<string?>("--save-to")
        {
            Description = "Save model to a different path after applying fixes"
        };

        var serializationOption = new Option<string?>("--serialization")
        {
            Description = "Model serialization: tmdl, bim, te-folder"
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

        var trxOption = new Option<string?>("--trx")
        {
            Description = "Write results as a VSTEST .trx file to the specified path"
        };

        var allowExternalRulesOption = new Option<bool>("--allow-external-rules")
        {
            Description = "Allow fetching BPA rule files from URLs embedded in model annotations"
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
            rulesetOption,
            noModelRulesOption,
            noDefaultsOption,
            vpaxOption,
            vpaRulesOption,
            failOnOption,
            fixOption,
            saveOption,
            saveToOption,
            serializationOption,
            ruleOption,
            ciOption,
            trxOption,
            allowExternalRulesOption,
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
                    parseResult.GetValue(fixOption),
                    parseResult.GetValue(rulesetOption),
                    parseResult.GetValue(failOnOption),
                    parseResult.GetValue(saveOption),
                    parseResult.GetValue(saveToOption),
                    parseResult.GetValue(serializationOption)),
                cancellationToken);

            var ci = parseResult.GetValue(ciOption);

            if (result.Data is not null)
                EmitCi(ci, result.Data.Violations);

            if (!string.IsNullOrWhiteSpace(ci))
                return result.ExitCode;

            return CommandOutput.Render(
                result,
                format,
                data => RenderRun(data, parseResult.GetValue(noMultilineOption)),
                ProjectRunJson);
        });

        return runCommand;
    }

    private Command BuildRulesCommand()
    {
        var rulesFileOption = new Option<string?>("--rules-file")
        {
            Description = "Path to a BPA rules JSON file"
        };

        var modelRulesOption = new Option<bool>("--model-rules")
        {
            Description = "Operate on rules embedded in the model annotation instead of a file"
        };

        var rulesetOption = new Option<string?>("--ruleset")
        {
            Description = $"Standard BPA ruleset to use ({string.Join(", ", BpaRuleLoader.KnownRulesets)})"
        };

        var noDefaultsOption = new Option<bool>("--no-defaults")
        {
            Description = "Suppress built-in rules from output"
        };

        var ignoredOption = new Option<bool>("--ignored")
        {
            Description = "Show only ignored rules"
        };

        var disabledOption = new Option<bool>("--disabled")
        {
            Description = "Show only disabled rules"
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "Show all rules including disabled and ignored"
        };

        var noMultilineOption = new Option<bool>("--no-multiline")
        {
            Description = "Collapse multi-line cell content in text output"
        };

        var modelArgument = new Argument<string>("model")
        {
            Description = "Path to model",
            Arity = ArgumentArity.ZeroOrOne
        };

        var rulesCommand = new Command("rules", "Manage BPA rule collections")
        {
            rulesFileOption,
            modelRulesOption
        };

        var listCommand = new Command("list", "List BPA rules from all sources with status")
        {
            modelArgument,
            rulesetOption,
            noDefaultsOption,
            ignoredOption,
            disabledOption,
            noMultilineOption,
            allOption
        };

        listCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var result = await new BpaRulesListHandler().HandleAsync(
                new BpaRulesListRequest(
                    All: parseResult.GetValue(allOption),
                    RulesFile: parseResult.GetValue(rulesFileOption),
                    Ruleset: parseResult.GetValue(rulesetOption),
                    NoDefaults: parseResult.GetValue(noDefaultsOption),
                    IgnoredOnly: parseResult.GetValue(ignoredOption),
                    DisabledOnly: parseResult.GetValue(disabledOption)),
                cancellationToken);

            return CommandOutput.Render(
                result,
                format,
                RenderRulesList,
                ProjectRulesListJson);
        });

        rulesCommand.Subcommands.Add(BuildRulesAddCommand());
        rulesCommand.Subcommands.Add(BuildRulesFlagCommand("disable", "Disable a built-in BPA rule for the current user"));
        rulesCommand.Subcommands.Add(BuildRulesFlagCommand("enable", "Re-enable a previously disabled built-in BPA rule"));
        rulesCommand.Subcommands.Add(BuildRulesIgnoreCommand("ignore", "Add a rule to the model's ignore list"));
        rulesCommand.Subcommands.Add(BuildRulesInitCommand());
        rulesCommand.Subcommands.Add(listCommand);
        rulesCommand.Subcommands.Add(BuildRulesRemoveCommand());
        rulesCommand.Subcommands.Add(BuildRulesSetCommand());
        rulesCommand.Subcommands.Add(BuildRulesIgnoreCommand("unignore", "Remove a rule from the model's ignore list"));
        return rulesCommand;
    }

    private static Command BuildRulesAddCommand()
    {
        var command = new Command("add", "Add a new BPA rule")
        {
            new Argument<string>("id") { Description = "Rule ID" },
            OptionalModelArgument(),
            new Option<string?>("--name") { Description = "Rule display name" },
            new Option<string?>("--expression") { Description = "Dynamic LINQ expression" },
            new Option<string?>("--scope") { Description = "Comma-separated scopes" },
            new Option<string?>("--category") { Description = "Rule category" },
            new Option<string?>("--severity") { Description = "Severity: 1, 2, or 3" },
            new Option<string?>("--description") { Description = "Rule description" },
            new Option<string?>("--fix-expression") { Description = "Dynamic LINQ fix expression" },
            new Option<bool>("--save") { Description = "Save model after adding rule" }
        };
        command.SetAction(_ => UnsupportedRulesAction("add"));
        return command;
    }

    private static Command BuildRulesSetCommand()
    {
        var queryOption = new Option<string?>("-q")
        {
            Description = "Property: name, expression, scope, category, severity, description, fixExpression"
        };
        var valueOption = new Option<string?>("-i")
        {
            Description = "New value"
        };

        var command = new Command("set", "Update a BPA rule's properties")
        {
            new Argument<string>("rule-id") { Description = "Rule ID to update" },
            OptionalModelArgument(),
            queryOption,
            valueOption,
            new Option<bool>("--save") { Description = "Save model after updating rule" }
        };
        command.SetAction(_ => UnsupportedRulesAction("set"));
        return command;
    }

    private static Command BuildRulesRemoveCommand()
    {
        var command = new Command("rm", "Remove a BPA rule")
        {
            new Argument<string>("rule-id") { Description = "Rule ID to remove" },
            OptionalModelArgument(),
            new Option<bool>("--save") { Description = "Save model after removing rule" }
        };
        command.SetAction(_ => UnsupportedRulesAction("rm"));
        return command;
    }

    private static Command BuildRulesFlagCommand(string name, string description)
    {
        var command = new Command(name, description)
        {
            new Argument<string>("rule-id") { Description = "Rule ID" }
        };
        command.SetAction(_ => UnsupportedRulesAction(name));
        return command;
    }

    private static Command BuildRulesIgnoreCommand(string name, string description)
    {
        var command = new Command(name, description)
        {
            new Argument<string>("rule-id") { Description = "Rule ID" },
            OptionalModelArgument(),
            new Option<bool>("--save") { Description = "Save changes to model" }
        };
        command.SetAction(_ => UnsupportedRulesAction(name));
        return command;
    }

    private static Command BuildRulesInitCommand()
    {
        var command = new Command("init", "Create an empty BPA rules file at the resolved path")
        {
            new Option<bool>("--force") { Description = "Overwrite an existing rules file" }
        };
        command.SetAction(_ => UnsupportedRulesAction("init"));
        return command;
    }

    private static Argument<string?> OptionalModelArgument()
        => new("model")
        {
            Description = "Path to model",
            Arity = ArgumentArity.ZeroOrOne
        };

    private static int UnsupportedRulesAction(string command)
    {
        Console.Error.WriteLine($"Command 'bpa rules {command}' is not implemented yet.");
        return 1;
    }

    private static object ProjectRunJson(BpaRunResult result)
        => new
        {
            rulesEvaluated = result.RulesEvaluated,
            violations = result.Violations.Count,
            ruleErrors = 0,
            ignoredRules = 0,
            fixesApplied = result.FixesApplied,
            fixesSkipped = result.FixesSkipped,
            fixErrors = result.FixErrors ?? Array.Empty<string>(),
            saved = result.Saved,
            results = result.Violations.Select(v => new
            {
                ruleId = v.RuleId,
                ruleName = v.RuleName,
                category = v.Category,
                severity = (int)v.Severity,
                severityLabel = v.Severity.ToString(),
                objectName = v.ObjectName,
                objectType = v.ObjectType,
                canFix = v.CanFix
            }),
            errors = Array.Empty<string>()
        };

    private static object ProjectRulesListJson(BpaRulesListResult result)
        => new
        {
            rules = result.Rules.Select(ProjectRuleInfo),
            summary = result.Summary
        };

    private static Dictionary<string, object?> ProjectRuleInfo(BpaRuleInfo rule)
    {
        var json = new Dictionary<string, object?>
        {
            ["source"] = rule.Source,
            ["status"] = rule.Status,
            ["id"] = rule.Id,
            ["name"] = rule.Name,
            ["category"] = rule.Category,
            ["severity"] = (int)rule.Severity,
            ["severityLabel"] = rule.Severity.ToString(),
            ["scope"] = rule.Scope
        };

        AddIfNotEmpty(json, "description", rule.Description);
        AddIfNotEmpty(json, "expression", rule.Expression);
        AddIfNotEmpty(json, "fixExpression", rule.FixExpression);

        return json;
    }

    private static void AddIfNotEmpty(Dictionary<string, object?> json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            json[name] = value;
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

        if (result.FixesApplied > 0)
        {
            Console.WriteLine($"  Fixes applied:    {result.FixesApplied}");
            if (result.FixesSkipped > 0)
                Console.WriteLine($"  Fixes skipped:    {result.FixesSkipped}");
            if (result.Saved)
                Console.WriteLine("  Model saved.");
        }
        else if (result.FixesSkipped > 0)
        {
            Console.WriteLine($"  Fixes skipped:    {result.FixesSkipped}");
        }

        if (result.FixErrors is { Count: > 0 })
        {
            Console.WriteLine();
            Console.WriteLine("  Fix errors:");
            foreach (var err in result.FixErrors)
                Console.WriteLine($"    {err}");
        }
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

        var rows = result.Rules.Select(r => (r.Id, r.Name, r.Category, Severity: r.Severity.ToString(), r.Scope)).ToList();

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
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";
}
