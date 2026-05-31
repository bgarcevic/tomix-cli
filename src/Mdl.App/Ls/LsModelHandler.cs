using Mdl.App.Diagnostics;
using Mdl.Core.Authentication;
using Mdl.Core.Models;
using Mdl.Core.Paths;
using Mdl.Core.Results;

namespace Mdl.App.Ls;

public sealed class LsModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public LsModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<LsModelResult>> HandleAsync(
        LsModelRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));

        if (provider is null)
            return MdlResult<LsModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        try
        {
            await using var session = await provider.OpenAsync(request.Model, cancellationToken);
            var snapshot = await session.GetSnapshotAsync(cancellationToken);

            var matches = ModelObjectSelector
                .Select(snapshot, request.PathFilter, request.Type)
                .Select(o => new LsObject(
                    o.Path, o.Name, o.Kind, o.Detail, o.Expression, o.Description, o.Hidden,
                    o.SourceColumn,
                    o.Children.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count())))
                .ToList();

            return MdlResult<LsModelResult>.Ok(
                new LsModelResult(snapshot.Name, snapshot.CompatibilityLevel, matches));
        }
        catch (AuthenticationRequiredException ex)
        {
            return MdlResult<LsModelResult>.Fail("MDL_AUTH_REQUIRED", ex.Message, exitCode: 1);
        }
        catch (Exception ex) when (request.Model.IsRemote && ex is not OperationCanceledException)
        {
            return MdlResult<LsModelResult>.Fail(
                "MDL_CONNECT_FAILED",
                RemoteConnectError.Describe(request.Model.Value, ex),
                exitCode: 1);
        }
    }
}
