using Mdl.App.Diagnostics;
using Mdl.Core.Authentication;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Info;

public sealed class InfoModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public InfoModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<InfoModelResult>> HandleAsync(
        InfoModelRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));

        if (provider is null)
            return MdlResult<InfoModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        try
        {
            await using var session = await provider.OpenAsync(request.Model, cancellationToken);
            var summary = await session.GetSummaryAsync(cancellationToken);
            return MdlResult<InfoModelResult>.Ok(new InfoModelResult(summary));
        }
        catch (InvalidOperationException ex)
            when (request.Model.IsRemote && ex.Message.Contains("Database not found on endpoint"))
        {
            return MdlResult<InfoModelResult>.Fail("MDL_DATABASE_NOT_FOUND", ex.Message, exitCode: 1);
        }
        catch (AuthenticationRequiredException ex)
        {
            return MdlResult<InfoModelResult>.Fail("MDL_AUTH_REQUIRED", ex.Message, exitCode: 1);
        }
        catch (Exception ex) when (request.Model.IsRemote && ex is not OperationCanceledException)
        {
            return MdlResult<InfoModelResult>.Fail(
                "MDL_CONNECT_FAILED",
                RemoteConnectError.Describe(request.Model.Value, ex),
                exitCode: 1);
        }
    }
}
