using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Bpa;

public sealed record BpaRulesListRequest(
    ModelReference? Model = null,
    bool All = false);

public sealed record BpaRulesListResult(
    IReadOnlyList<BpaRuleInfo> Rules);

public sealed record BpaRuleInfo(
    string Id,
    string Name,
    string Category,
    string Severity,
    string Scope,
    bool Enabled);

public sealed class BpaRulesListHandler
{
    public Task<MdlResult<BpaRulesListResult>> HandleAsync(
        BpaRulesListRequest request,
        CancellationToken cancellationToken)
    {
        var rules = BpaRuleLoader.LoadDefaultRules();

        var result = new BpaRulesListResult(
            rules.Select(r => new BpaRuleInfo(
                r.Id,
                r.Name,
                r.Category,
                r.Severity.ToString(),
                string.Join(", ", r.Scope),
                Enabled: true)).ToList());

        return Task.FromResult(MdlResult<BpaRulesListResult>.Ok(result));
    }
}
