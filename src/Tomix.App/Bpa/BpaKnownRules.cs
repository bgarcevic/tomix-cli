using Tomix.Core.Results;

namespace Tomix.App.Bpa;

/// <summary>
/// The rule IDs a <c>bpa rules disable</c>/<c>ignore</c> target can name: the whole bundled catalog,
/// the user's config-dir <c>bpa-rules.json</c>, and, with a model, its embedded and local external
/// rules. Guards against a typo silently disabling or ignoring nothing.
/// </summary>
public sealed class BpaKnownRules
{
    public const string AllowUnknownOption = "--allow-unknown";

    private readonly HashSet<string> _ids;

    private BpaKnownRules(HashSet<string> ids, bool complete)
    {
        _ids = ids;
        Complete = complete;
    }

    /// <summary>
    /// False when some rule source could not be read (a remote external file, a malformed
    /// annotation or user file), so an unmatched ID may still be real and is let through.
    /// </summary>
    public bool Complete { get; }

    /// <summary>The bundled catalog plus the user's config-dir rules, when present and readable.</summary>
    public static BpaKnownRules Load(string? configDirectory)
    {
        var ids = new HashSet<string>(
            BpaRuleLoader.LoadBundledCatalog().Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var complete = true;

        var userRulesPath = string.IsNullOrWhiteSpace(configDirectory)
            ? null
            : Path.Combine(configDirectory, "bpa-rules.json");
        if (userRulesPath is not null && File.Exists(userRulesPath))
        {
            try
            {
                ids.UnionWith(BpaRuleLoader.LoadFromFile(userRulesPath).Select(r => r.Id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                complete = false;
            }
        }

        return new BpaKnownRules(ids, complete);
    }

    /// <summary>Adds the model's embedded and local external rules. Remote files are never fetched.</summary>
    public async Task<BpaKnownRules> WithModelRulesAsync(
        IReadOnlyDictionary<string, string>? modelProperties,
        string? baseDirectory,
        CancellationToken cancellationToken)
    {
        var model = await BpaModelRuleLoader.LoadAsync(
            modelProperties, baseDirectory, allowExternal: false, BpaRuleHintContext.List,
            httpClient: null, cancellationToken).ConfigureAwait(false);

        var ids = new HashSet<string>(_ids, StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(model.Collections.SelectMany(c => c.Rules).Select(r => r.Id));
        return new BpaKnownRules(ids, Complete && model.Diagnostics.Count == 0);
    }

    /// <summary>
    /// A <c>TOMIX_BPA_RULE_NOT_FOUND</c> failure when <paramref name="ruleId"/> is unknown and every
    /// source was readable; otherwise null. The hint names up to three IDs containing the input.
    /// </summary>
    public TomixResult<T>? Check<T>(string ruleId)
    {
        if (!Complete || _ids.Contains(ruleId))
            return null;

        var near = _ids
            .Where(id => id.Contains(ruleId, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var suggestion = near.Count > 0
            ? $"Did you mean: {string.Join(", ", near)}? "
            : "Run 'tx bpa rules list --all' to see every rule ID. ";

        return TomixResult<T>.Fail(
            "TOMIX_BPA_RULE_NOT_FOUND",
            $"No BPA rule with ID '{ruleId}'.",
            exitCode: 2,
            hint: suggestion + $"Pass {AllowUnknownOption} to use the ID anyway.");
    }
}
