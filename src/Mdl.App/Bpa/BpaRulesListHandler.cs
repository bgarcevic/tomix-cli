using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;
using System.Text.Json;

namespace Mdl.App.Bpa;

public sealed record BpaRulesListRequest(
    ModelReference? Model = null,
    bool All = false,
    string? RulesFile = null,
    string? Ruleset = null,
    bool NoDefaults = false,
    bool IgnoredOnly = false,
    bool DisabledOnly = false);

public sealed record BpaRulesListResult(
    IReadOnlyList<BpaRuleInfo> Rules,
    BpaRulesSummary Summary);

public sealed record BpaRulesSummary(
    int Total,
    int Active,
    int Disabled,
    int Ignored);

public sealed record BpaRuleInfo(
    string Source,
    string Status,
    string Id,
    string Name,
    string Category,
    BpaSeverity Severity,
    string Scope,
    string? Description,
    string? Expression,
    string? FixExpression,
    bool Enabled);

public sealed class BpaRulesListHandler
{
    public async Task<MdlResult<BpaRulesListResult>> HandleAsync(
        BpaRulesListRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LoadedRule> rules;
        try
        {
            rules = await LoadRulesAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or HttpRequestException or JsonException)
        {
            return MdlResult<BpaRulesListResult>.Fail(
                "MDL_BPA_RULES_LOAD_FAILED",
                ex.Message,
                exitCode: 2);
        }

        var allRules = rules.Select(r => new BpaRuleInfo(
            r.Source,
            Status: "active",
            r.Rule.Id,
            r.Rule.Name,
            r.Rule.Category,
            r.Rule.Severity,
            string.Join(", ", r.Rule.Scope),
            r.Rule.Description,
            r.Rule.Expression,
            r.Rule.FixExpression,
            Enabled: true)).ToList();

        var filteredRules = request.IgnoredOnly || request.DisabledOnly
            ? []
            : allRules;

        var result = new BpaRulesListResult(
            filteredRules,
            new BpaRulesSummary(
                Total: allRules.Count,
                Active: allRules.Count,
                Disabled: 0,
                Ignored: 0));

        return MdlResult<BpaRulesListResult>.Ok(result);
    }

    private static async Task<IReadOnlyList<LoadedRule>> LoadRulesAsync(
        BpaRulesListRequest request,
        CancellationToken cancellationToken)
    {
        var rules = new List<LoadedRule>();

        if (!request.NoDefaults)
        {
            var source = string.IsNullOrWhiteSpace(request.Ruleset)
                ? BpaRuleLoader.StandardRuleset
                : request.Ruleset;
            rules.AddRange((await BpaRuleLoader.LoadRulesetAsync(request.Ruleset, cancellationToken).ConfigureAwait(false))
                .Select(rule => new LoadedRule(source, rule)));
        }

        if (!string.IsNullOrWhiteSpace(request.RulesFile))
        {
            rules.AddRange((await BpaRuleLoader.LoadFromSourceAsync(request.RulesFile, cancellationToken).ConfigureAwait(false))
                .Select(rule => new LoadedRule("custom", rule)));
        }

        return rules;
    }

    private sealed record LoadedRule(string Source, BpaRule Rule);
}
