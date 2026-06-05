using System.Text.RegularExpressions;
using Mdl.App.Bpa.Model;
using Mdl.Core.Bpa;
using Mdl.Core.Models;

namespace Mdl.App.Bpa;

/// <summary>
/// Evaluates Best-Practice-Analyzer rules against a model snapshot. Each rule's
/// <c>Expression</c> (a Dynamic-LINQ predicate) is evaluated generically against an
/// adapter object model (<see cref="BpaModel"/>) — there is no per-rule hardcoded logic, so any
/// rule expressed in the <c>bpa-rules.json</c> format is honored. A rule whose expression cannot
/// be evaluated against the available metadata is skipped (never a false positive).
/// </summary>
public sealed class BpaEngine
{
    private readonly BpaExpressionEvaluator _evaluator = new();

    public BpaRunResult Evaluate(ModelSnapshot snapshot, BpaEngineOptions options)
    {
        var ruleIds = options.RuleIds is { Count: > 0 }
            ? new HashSet<string>(options.RuleIds, StringComparer.OrdinalIgnoreCase)
            : null;

        var activeRules = ruleIds is null
            ? options.Rules
            : options.Rules.Where(r => ruleIds.Contains(r.Id)).ToList();

        var model = BpaModelBuilder.Build(snapshot);
        var violations = new List<BpaViolation>();

        foreach (var rule in activeRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Expression))
                continue;

            EvaluateRule(rule, model, options.PathFilter, violations);
        }

        return new BpaRunResult(violations, snapshot.Name, activeRules.Count);
    }

    private void EvaluateRule(BpaRule rule, BpaModel model, string? pathFilter, List<BpaViolation> violations)
    {
        // Evaluate the rule only over objects whose actual ObjectType is one of the rule's
        // scope tokens — e.g. a rule scoped to CalculatedColumn must not see DataColumns.
        void Eval<T>(IEnumerable<T> items) where T : BpaObject
        {
            var scoped = items.Where(o => MatchesScope(rule.Scope, o)).ToList();
            if (scoped.Count == 0)
                return;

            foreach (var hit in _evaluator.Evaluate(rule.Expression!, scoped))
            {
                if (pathFilter is not null && !MatchesPathFilter(hit.Source, pathFilter))
                    continue;
                violations.Add(ToViolation(rule, hit.Source));
            }
        }

        Eval(model.AllMeasures);
        Eval(model.AllColumns);
        Eval(model.Tables);
        Eval(model.Relationships);
        Eval(model.AllPartitions);
        Eval(model.Roles);
        Eval(model.AllCalculationItems);
        Eval(model.DataSources);
        Eval(model.Perspectives);
        Eval(model.Hierarchies);

        if (rule.Scope.Any(s => s.Equals("Model", StringComparison.OrdinalIgnoreCase)))
        {
            var hits = _evaluator.Evaluate(rule.Expression!, new[] { model });
            if (hits.Count > 0)
                violations.Add(ToViolation(rule, model.Source));
        }
    }

    private static bool MatchesScope(IReadOnlyList<string> scope, BpaObject obj)
    {
        foreach (var token in scope)
            if (ObjectInScope(obj, token))
                return true;
        return false;
    }

    /// <summary>Whether an adapter object's real type satisfies a single rule scope token.</summary>
    private static bool ObjectInScope(BpaObject obj, string token) => obj switch
    {
        BpaColumn => Eq(token, "Column") || Eq(token, obj.Source.Property("ObjectType") ?? "DataColumn"),
        BpaMeasure => Eq(token, "Measure") || (Eq(token, "KPI") && obj.Source.Property("KPI") == "Present"),
        BpaTable => Eq(token, "Table")
            || (Eq(token, "CalculatedTable") && obj.Source.Property("TableIsCalc") == "true")
            || (Eq(token, "CalculationGroup") && obj.Source.Property("TableObjectType") == "CalculationGroup"),
        BpaCalculationItem => Eq(token, "CalculationItem"),
        BpaDataSource ds => Eq(token, "DataSource")
            || (Eq(token, "StructuredDataSource") && Eq(ds.Type, "Structured"))
            || (Eq(token, "ProviderDataSource") && Eq(ds.Type, "Provider")),
        BpaPerspective => Eq(token, "Perspective"),
        BpaHierarchy => Eq(token, "Hierarchy"),
        BpaRelationship => Eq(token, "Relationship"),
        BpaPartition => Eq(token, "Partition"),
        BpaRole => Eq(token, "ModelRole"),
        _ => false
    };

    private static bool Eq(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPathFilter(ModelObject obj, string pathFilter)
    {
        if (obj.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (pathFilter.Contains('*'))
        {
            var pattern = "^" + Regex.Escape(pathFilter).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(obj.Path, pattern, RegexOptions.IgnoreCase);
        }

        var slashIndex = obj.Path.IndexOf('/');
        if (slashIndex > 0)
        {
            var tableName = obj.Path[..slashIndex];
            if (tableName.Equals(pathFilter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static BpaViolation ToViolation(BpaRule rule, ModelObject obj)
        => new(
            rule.Id,
            rule.Name,
            rule.Category,
            rule.Severity,
            ReferenceObjectType(obj),
            ReferenceObjectName(obj),
            obj.Path,
            rule.Description,
            !string.IsNullOrWhiteSpace(rule.FixExpression),
            obj.Kind);

    private static string ReferenceObjectType(ModelObject obj)
    {
        if (obj.Property("ObjectType") == "Model")
            return "Model";

        return obj.Kind switch
        {
            ModelObjectKind.Table => "Table (Import)",
            ModelObjectKind.Column => "Column",
            ModelObjectKind.Measure => "Measure",
            ModelObjectKind.Partition => "Partition",
            ModelObjectKind.Relationship => "Relationship",
            _ => obj.Kind.ToString()
        };
    }

    private static string ReferenceObjectName(ModelObject obj)
    {
        if (obj.Property("ObjectType") == "Model")
            return "Model";

        var parts = obj.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return obj.Kind switch
        {
            ModelObjectKind.Table => $"'{obj.Name}'",
            ModelObjectKind.Column when parts.Length >= 2 => $"'{parts[0]}'[{obj.Name}]",
            ModelObjectKind.Measure => $"[{obj.Name}]",
            _ => obj.Name
        };
    }
}

public sealed record BpaEngineOptions(
    IReadOnlyList<BpaRule> Rules,
    string? PathFilter = null,
    IReadOnlyList<string>? RuleIds = null);
