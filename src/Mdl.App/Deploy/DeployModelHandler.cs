using Mdl.App.Bpa;
using Mdl.App.State;
using Mdl.Core.Authentication;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Deploy;

public sealed class DeployModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public DeployModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

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
            var bpaResult = await RunBpaGate(request, cancellationToken);
            if (bpaResult is not null)
                return bpaResult;
        }

        if (session is not IModelDeploySession deployer)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_DEPLOY_UNSUPPORTED",
                $"Provider cannot deploy model: {request.Model.Value}",
                exitCode: 1);

        var (server, database) = ResolveTarget(request);

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
            return MdlResult<DeployModelResult>.Fail("MDL_DEPLOY_CONNECT_FAILED", $"Failed to connect to '{server}': {ex.InnerException?.Message ?? ex.Message}", exitCode: 1);
        }
    }

    private async Task<MdlResult<DeployModelResult>?> RunBpaGate(
        DeployModelRequest request,
        CancellationToken cancellationToken)
    {
        var bpaHandler = new BpaRunHandler(_providers);
        var bpaResult = await bpaHandler.HandleAsync(
            new BpaRunRequest(
                request.Model,
                request.BpaRules,
                NoDefaults: false,
                PathFilter: null,
                RuleIds: null,
                Fix: request.FixBpa),
            cancellationToken);

        if (!bpaResult.Success)
            return MdlResult<DeployModelResult>.Fail(
                bpaResult.Diagnostics[0].Code,
                bpaResult.Diagnostics[0].Message,
                bpaResult.ExitCode);

        if (bpaResult.Data is not null && bpaResult.Data.Violations.Count > 0 && !request.FixBpa)
            return MdlResult<DeployModelResult>.Fail(
                "MDL_BPA_VIOLATIONS",
                $"BPA check found {bpaResult.Data.Violations.Count} violation(s). Use --skip-bpa to bypass or --fix-bpa to auto-fix.",
                exitCode: 1);

        return null;
    }

    private static (string? server, string? database) ResolveTarget(DeployModelRequest request)
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

        var session = new CliStateStore().LoadCurrentSession();
        if (session is not null)
            return (session.Server, session.Database ?? request.Database);

        return (null, request.Database);
    }
}
