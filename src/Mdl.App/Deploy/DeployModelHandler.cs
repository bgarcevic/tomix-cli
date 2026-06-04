using Mdl.App.Bpa;
using Mdl.App.State;
using Mdl.Core.Authentication;
using Mdl.Core.Bpa;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Deploy;

public sealed class DeployModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly Func<CliConnectionState?> _resolveSession;

    public DeployModelHandler(IEnumerable<IModelProvider> providers)
        : this(providers, () => new CliStateStore().LoadCurrentSession()) { }

    public DeployModelHandler(IEnumerable<IModelProvider> providers, Func<CliConnectionState?> resolveSession)
    {
        _providers = providers.ToList();
        _resolveSession = resolveSession;
    }

    public async Task<MdlResult<DeployModelResult>> HandleAsync(
        DeployModelRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Model.Value.Length == 0)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_NO_MODEL",
                "No model specified. Use --model <path>, --server <url> --database <name>, --local, or set an active connection with 'mdl connect'.",
                exitCode: 1);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);

        if (!request.SkipBpa)
        {
            var bpaResult = await RunBpaGate(session, request, cancellationToken);
            if (bpaResult is not null)
                return bpaResult;
        }

        if (session is not IModelDeploySession deployer)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_DEPLOY_UNSUPPORTED",
                $"Provider cannot deploy model: {request.Model.Value}",
                exitCode: 1);

        var (server, database) = ResolveTarget(request, _resolveSession);

        if (string.IsNullOrWhiteSpace(server))
            return MdlResult<DeployModelResult>.Fail(
                "MDL_DEPLOY_NO_TARGET",
                "No target workspace specified. Use -s/--server or set an active connection with 'mdl connect'.",
                exitCode: 1);

        var deployRequest = new ModelDeployRequest(
            server,
            database,
            request.DeployFull,
            request.CreateOnly,
            request.Force);

        if (!string.IsNullOrWhiteSpace(request.XmlaOutput))
        {
            var script = deployer.GenerateScript(deployRequest);
            var scriptPath = request.XmlaOutput;

            if (scriptPath == "-")
                return MdlResult<DeployModelResult>.Ok(new DeployModelResult(
                    server, database ?? request.Model.Value, "script", null, "-", script));

            var fullPath = Path.GetFullPath(scriptPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, script, cancellationToken).ConfigureAwait(false);
            return MdlResult<DeployModelResult>.Ok(new DeployModelResult(
                server, database ?? request.Model.Value, "script", null, fullPath, null));
        }

        try
        {
            var result = await deployer.DeployAsync(deployRequest, cancellationToken);
            return MdlResult<DeployModelResult>.Ok(new DeployModelResult(
                result.Server, result.Database, result.Status, result.DurationMs, null, null));
        }
        catch (AuthenticationRequiredException ex)
        {
            return MdlResult<DeployModelResult>.Fail("MDL_AUTH_REQUIRED", ex.Message, exitCode: 1);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<DeployModelResult>.Fail("MDL_DEPLOY_FAILED", ex.Message, exitCode: 1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MdlResult<DeployModelResult>.Fail("MDL_DEPLOY_FAILED", $"Deploy to '{server}' failed: {ex.InnerException?.Message ?? ex.Message}", exitCode: 1);
        }
    }

    private async Task<MdlResult<DeployModelResult>?> RunBpaGate(
        IModelSession session,
        DeployModelRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BpaRule> rules;
        try
        {
            rules = await BpaRuleLoader.LoadRulesetAsync(null, cancellationToken).ConfigureAwait(false);
            if (request.BpaRules is not null)
            {
                foreach (var file in request.BpaRules)
                {
                    if (!string.IsNullOrWhiteSpace(file))
                        rules = [.. rules, .. await BpaRuleLoader.LoadFromSourceAsync(file, cancellationToken).ConfigureAwait(false)];
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or HttpRequestException)
        {
            return MdlResult<DeployModelResult>.Fail(
                "MDL_BPA_RULES_LOAD_FAILED",
                ex.Message,
                exitCode: 2);
        }

        var snapshot = await session.GetSnapshotAsync(cancellationToken);
        var engine = new BpaEngine();
        var result = engine.Evaluate(snapshot, new BpaEngineOptions(rules, null, null));

        if (request.FixBpa && result.Violations.Any(v => v.CanFix))
        {
            if (session is IModelMutationSession mutationSession)
            {
                var fixer = new BpaFixer();
                fixer.ApplyFixes(mutationSession, result.Violations, rules);
            }
        }

        var remaining = result.Violations.Where(v => !v.CanFix || !request.FixBpa).ToList();
        if (remaining.Count > 0 && !request.FixBpa)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_BPA_VIOLATIONS",
                $"BPA check found {remaining.Count} violation(s). Use --skip-bpa to bypass or --fix-bpa to auto-fix.",
                exitCode: 1);

        return null;
    }

    private static (string? server, string? database) ResolveTarget(
        DeployModelRequest request, Func<CliConnectionState?> resolveSession)
    {
        if (!string.IsNullOrWhiteSpace(request.Server))
            return (request.Server, request.Database);

        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            var store = new CliStateStore();
            var profiles = store.LoadProfiles();
            if (profiles.TryGetValue(request.Profile, out var profile))
                return (profile.Server, profile.Database ?? request.Database);
        }

        var session = resolveSession();
        if (session is not null)
            return (session.Server, session.Database ?? request.Database);

        return (null, request.Database);
    }
}
