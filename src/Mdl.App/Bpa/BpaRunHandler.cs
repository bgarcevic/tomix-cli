using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;
using System.Text.Json;

namespace Mdl.App.Bpa;

public sealed class BpaRunHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public BpaRunHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<BpaRunResult>> HandleAsync(
        BpaRunRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<BpaRunResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        if (!TryParseFailOn(request.FailOn, out var failOnSeverity, out var failOnError))
            return MdlResult<BpaRunResult>.Fail(
                "MDL_BPA_INVALID_FAIL_ON",
                failOnError!,
                exitCode: 2);

        IReadOnlyList<BpaRule> rules;
        try
        {
            rules = await LoadRulesAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or HttpRequestException or JsonException)
        {
            return MdlResult<BpaRunResult>.Fail(
                "MDL_BPA_RULES_LOAD_FAILED",
                ex.Message,
                exitCode: 2);
        }

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(
            rules,
            request.PathFilter,
            request.RuleIds));
        sw.Stop();

        var runResult = result with { DurationMs = sw.ElapsedMilliseconds };

        return MdlResult<BpaRunResult>.Ok(runResult, exitCode: ShouldFail(runResult, failOnSeverity) ? 1 : 0);
    }

    private static async Task<IReadOnlyList<BpaRule>> LoadRulesAsync(
        BpaRunRequest request,
        CancellationToken cancellationToken)
    {
        var rules = new List<BpaRule>();

        if (!request.NoDefaults)
            rules.AddRange(await BpaRuleLoader.LoadRulesetAsync(request.Ruleset, cancellationToken).ConfigureAwait(false));

        if (request.RulesFiles is not null)
        {
            foreach (var file in request.RulesFiles)
            {
                if (!string.IsNullOrWhiteSpace(file))
                    rules.AddRange(await BpaRuleLoader.LoadFromSourceAsync(file, cancellationToken).ConfigureAwait(false));
            }
        }

        return rules;
    }

    private static bool TryParseFailOn(
        string? value,
        out BpaSeverity severity,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            severity = BpaSeverity.Error;
            error = null;
            return true;
        }

        if (value.Equals("warning", StringComparison.OrdinalIgnoreCase))
        {
            severity = BpaSeverity.Warning;
            error = null;
            return true;
        }

        severity = BpaSeverity.Error;
        error = $"Invalid --fail-on value '{value}'. Expected: error or warning.";
        return false;
    }

    private static bool ShouldFail(BpaRunResult result, BpaSeverity threshold)
        => threshold switch
        {
            BpaSeverity.Error => result.Violations.Any(v => v.Severity == BpaSeverity.Error),
            BpaSeverity.Warning => result.Violations.Any(v => v.Severity is BpaSeverity.Warning or BpaSeverity.Error),
            _ => result.Violations.Count > 0
        };
}
