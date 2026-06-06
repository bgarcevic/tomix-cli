using Mdl.Core.Bpa;
using Mdl.Core.Configuration;
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
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        if (!TryParseFailOn(request.FailOn, out var failOnSeverity, out var failOnError))
            return MdlResult<BpaRunResult>.Fail(
                "MDL_BPA_INVALID_FAIL_ON",
                failOnError!,
                exitCode: 2);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);

        IReadOnlyList<BpaRule> rules;
        IReadOnlyList<string> loadDiagnostics;
        try
        {
            (rules, loadDiagnostics) = await LoadRulesAsync(request, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or HttpRequestException or JsonException)
        {
            return MdlResult<BpaRunResult>.Fail(
                "MDL_BPA_RULES_LOAD_FAILED",
                ex.Message,
                exitCode: 2);
        }

        var userDisabled = new BpaUserRuleState().GetDisabled().ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(
            rules,
            request.PathFilter,
            request.RuleIds,
            userDisabled));
        sw.Stop();

        var runResult = result with
        {
            DurationMs = sw.ElapsedMilliseconds,
            RuleLoadDiagnostics = loadDiagnostics.Count > 0 ? loadDiagnostics : null
        };

        if (request.Fix && runResult.Violations.Any(v => v.CanFix))
        {
            if (session is not IModelMutationSession mutationSession)
                return MdlResult<BpaRunResult>.Fail(
                    "MDL_BPA_FIX_UNSUPPORTED",
                    "The model provider does not support applying fixes.",
                    exitCode: 2);

            var fixer = new BpaFixer();
            var fixResult = fixer.ApplyFixes(mutationSession, runResult.Violations, rules);

            runResult = runResult with
            {
                FixesApplied = fixResult.FixesApplied,
                FixesSkipped = fixResult.FixesSkipped,
                FixErrors = fixResult.Errors.Count > 0
                    ? fixResult.Errors.Select(e => $"[{e.RuleId}] {e.ObjectPath}: {e.Reason}").ToList()
                    : null
            };

            if (request.Save && fixResult.FixesApplied > 0)
            {
                var serialization = string.IsNullOrWhiteSpace(request.Serialization)
                    ? "tmdl"
                    : request.Serialization;

                await mutationSession.SaveAsync(
                    request.SaveTo,
                    serialization,
                    force: false,
                    cancellationToken);

                runResult = runResult with { Saved = true };
            }
        }

        return MdlResult<BpaRunResult>.Ok(runResult, exitCode: ShouldFail(runResult, failOnSeverity) ? 1 : 0);
    }

    /// <summary>
    /// Assembles the effective rule set from all sources with documented precedence (spec §6):
    /// machine (bundled/ruleset) &lt; user (--rules + user config dir) &lt; external (model-referenced)
    /// &lt; model-embedded. Returns the resolved rules plus best-effort load diagnostics.
    /// </summary>
    private static async Task<(IReadOnlyList<BpaRule> Rules, IReadOnlyList<string> Diagnostics)> LoadRulesAsync(
        BpaRunRequest request,
        ModelSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var collections = new List<BpaRuleCollection>();
        var diagnostics = new List<string>();

        // Machine: the bundled/standard ruleset (or a named --ruleset).
        if (!request.NoDefaults)
        {
            var label = string.IsNullOrWhiteSpace(request.Ruleset) ? BpaRuleLoader.StandardRuleset : request.Ruleset;
            collections.Add(new BpaRuleCollection(
                BpaRuleSourceKind.Machine, label,
                await BpaRuleLoader.LoadRulesetAsync(request.Ruleset, cancellationToken).ConfigureAwait(false)));
        }

        // User: explicit --rules files (a load failure here is fatal, as before).
        if (request.RulesFiles is not null)
        {
            foreach (var file in request.RulesFiles)
            {
                if (!string.IsNullOrWhiteSpace(file))
                    collections.Add(new BpaRuleCollection(
                        BpaRuleSourceKind.User, file,
                        await BpaRuleLoader.LoadFromSourceAsync(file, cancellationToken).ConfigureAwait(false)));
            }
        }

        // User: a rules file in the config directory (~/.mdl or $MDL_CONFIG_DIR), if present.
        var userRulesPath = Path.Combine(MdlPaths.ConfigDirectory, "bpa-rules.json");
        if (File.Exists(userRulesPath))
        {
            var userRules = BpaRuleLoader.LoadFromFile(userRulesPath);
            if (userRules.Count > 0)
                collections.Add(new BpaRuleCollection(BpaRuleSourceKind.User, userRulesPath, userRules));
        }

        // External + model-embedded: loaded from the model's annotations (best-effort).
        if (!request.NoModelRules)
        {
            var model = await BpaModelRuleLoader.LoadAsync(
                snapshot.Properties,
                ModelBaseDirectory(request.Model),
                request.AllowExternalRules,
                cancellationToken).ConfigureAwait(false);

            collections.AddRange(model.Collections);
            diagnostics.AddRange(model.Diagnostics);
        }

        return (BpaRuleResolver.Resolve(collections), diagnostics);
    }

    /// <summary>The directory used to resolve relative external-rule-file paths.</summary>
    private static string? ModelBaseDirectory(ModelReference model)
    {
        if (!model.IsLocalPath)
            return null;

        try
        {
            return Directory.Exists(model.Value)
                ? model.Value
                : Path.GetDirectoryName(Path.GetFullPath(model.Value));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
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
