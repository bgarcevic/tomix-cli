using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Bpa;

public sealed record BpaRulesIgnoreRequest(
    ModelReference Model,
    string RuleId,
    bool Ignore,
    bool Save = false,
    string? SaveTo = null,
    string? Serialization = null);

public sealed record BpaRulesIgnoreResult(
    string RuleId,
    bool Ignored,
    bool Changed,
    IReadOnlyList<string> RuleIds,
    bool Saved,
    string ModelName);

/// <summary>
/// Adds or removes a rule from the model's global ignore list (the model-level
/// <c>BestPracticeAnalyzer_IgnoreRules</c> annotation). Writes the correctly-spelled key and drops
/// the historical misspelled one, optionally persisting the change.
/// </summary>
public sealed class BpaRulesIgnoreHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public BpaRulesIgnoreHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<BpaRulesIgnoreResult>> HandleAsync(
        BpaRulesIgnoreRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RuleId))
            return MdlResult<BpaRulesIgnoreResult>.Fail(
                "MDL_BPA_RULE_ID_REQUIRED", "A rule id is required.", exitCode: 2);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<BpaRulesIgnoreResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file.");

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutationSession)
            return MdlResult<BpaRulesIgnoreResult>.Fail(
                "MDL_BPA_IGNORE_UNSUPPORTED",
                "The model provider does not support editing the ignore list.",
                exitCode: 2);

        var snapshot = await session.GetSnapshotAsync(cancellationToken);

        var current = new HashSet<string>(BpaIgnoreStore.ReadRuleIds(snapshot.Properties), StringComparer.OrdinalIgnoreCase);
        var hadLegacyKey = BpaIgnoreStore.HasLegacyKey(snapshot.Properties);

        var setChanged = request.Ignore ? current.Add(request.RuleId) : current.Remove(request.RuleId);
        var changed = setChanged || hadLegacyKey;

        if (changed)
        {
            mutationSession.SetProperty(new ModelObjectSetRequest(
                ".",
                [
                    new ModelPropertyAssignment($"Annotation:{BpaIgnoreStore.Key}", BpaIgnoreStore.Serialize(current)),
                    // Drop the historical misspelled key (empty value removes the annotation).
                    new ModelPropertyAssignment($"Annotation:{BpaIgnoreStore.LegacyKey}", "")
                ],
                Type: null));
        }

        var saved = false;
        if (request.Save && changed)
        {
            var serialization = string.IsNullOrWhiteSpace(request.Serialization) ? "tmdl" : request.Serialization;
            await mutationSession.SaveAsync(request.SaveTo, serialization, force: false, cancellationToken);
            saved = true;
        }

        var result = new BpaRulesIgnoreResult(
            request.RuleId,
            Ignored: request.Ignore,
            Changed: changed,
            RuleIds: current.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Saved: saved,
            ModelName: snapshot.Name);

        return MdlResult<BpaRulesIgnoreResult>.Ok(result);
    }
}
