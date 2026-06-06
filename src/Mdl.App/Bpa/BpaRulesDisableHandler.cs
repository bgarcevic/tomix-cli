using Mdl.Core.Results;

namespace Mdl.App.Bpa;

public sealed record BpaRulesDisableRequest(string RuleId, bool Disable);

public sealed record BpaRulesDisableResult(
    string RuleId,
    bool Disabled,
    bool Changed,
    IReadOnlyList<string> DisabledRuleIds);

/// <summary>Enables/disables a BPA rule at the user level (see <see cref="BpaUserRuleState"/>).</summary>
public sealed class BpaRulesDisableHandler
{
    private readonly BpaUserRuleState _state;

    public BpaRulesDisableHandler(BpaUserRuleState? state = null)
        => _state = state ?? new BpaUserRuleState();

    public MdlResult<BpaRulesDisableResult> Handle(BpaRulesDisableRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RuleId))
            return MdlResult<BpaRulesDisableResult>.Fail(
                "MDL_BPA_RULE_ID_REQUIRED", "A rule id is required.", exitCode: 2);

        var changed = request.Disable ? _state.Disable(request.RuleId) : _state.Enable(request.RuleId);

        var result = new BpaRulesDisableResult(
            request.RuleId,
            Disabled: request.Disable,
            Changed: changed,
            DisabledRuleIds: _state.GetDisabled().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());

        return MdlResult<BpaRulesDisableResult>.Ok(result);
    }
}
