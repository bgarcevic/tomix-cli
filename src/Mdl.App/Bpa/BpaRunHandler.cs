using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;

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

        var rules = LoadRules(request);

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

        return MdlResult<BpaRunResult>.Ok(runResult, exitCode: runResult.Violations.Count > 0 ? 1 : 0);
    }

    private static IReadOnlyList<BpaRule> LoadRules(BpaRunRequest request)
    {
        var rules = new List<BpaRule>();

        if (!request.NoDefaults)
            rules.AddRange(BpaRuleLoader.LoadDefaultRules());

        if (request.RulesFiles is not null)
        {
            foreach (var file in request.RulesFiles)
            {
                if (File.Exists(file))
                    rules.AddRange(BpaRuleLoader.LoadFromFile(file));
            }
        }

        return rules;
    }
}
