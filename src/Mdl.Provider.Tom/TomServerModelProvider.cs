using Mdl.Core.Authentication;
using Mdl.Core.Models;
using AsAccessToken = Microsoft.AnalysisServices.AccessToken;
using TabularDatabase = Microsoft.AnalysisServices.Tabular.Database;
using TabularServer = Microsoft.AnalysisServices.Tabular.Server;

namespace Mdl.Provider.Tom;

/// <summary>
/// Opens a live model over XMLA (<c>powerbi://</c>, <c>asazure://</c>, or a local
/// <c>localhost:&lt;port&gt;</c> Power BI Desktop instance). Remote endpoints acquire a token
/// from the injected <see cref="IAccessTokenProvider"/>; local instances connect without one.
/// Read-only: summary and snapshot only.
/// </summary>
public sealed class TomServerModelProvider : IModelProvider
{
    private readonly IAccessTokenProvider? _tokenProvider;

    public TomServerModelProvider(IAccessTokenProvider? tokenProvider) => _tokenProvider = tokenProvider;

    public bool CanOpen(ModelReference reference) => reference.IsRemote;

    public async Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
    {
        var server = new TabularServer();

        if (!reference.IsLocalInstance)
        {
            if (_tokenProvider is null)
                throw new AuthenticationRequiredException("Not authenticated. Run 'mdl auth login'.");

            var token = await _tokenProvider.GetTokenAsync(reference.Value, cancellationToken).ConfigureAwait(false);
            server.AccessToken = new AsAccessToken(token.Token, token.ExpiresOn.UtcDateTime);
            server.OnAccessTokenExpired = _ =>
            {
                var refreshed = _tokenProvider.GetTokenAsync(reference.Value, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return new AsAccessToken(refreshed.Token, refreshed.ExpiresOn.UtcDateTime);
            };
        }

        server.Connect(BuildConnectionString(reference));
        return new TomServerModelSession(server, ResolveDatabase(server, reference.Database), _tokenProvider);
    }

    private static string BuildConnectionString(ModelReference reference)
    {
        var connectionString = $"Data Source={TomModelDeployer.ResolveEndpoint(reference.Value)}";
        return string.IsNullOrWhiteSpace(reference.Database)
            ? connectionString
            : $"{connectionString};Initial Catalog={reference.Database}";
    }

    private static TabularDatabase ResolveDatabase(TabularServer server, string? database)
    {
        if (!string.IsNullOrWhiteSpace(database))
            return server.Databases.FindByName(database)
                ?? throw new InvalidOperationException($"Database not found on endpoint: {database}");

        return server.Databases.Count switch
        {
            1 => server.Databases[0],
            0 => throw new InvalidOperationException("No databases are available on the endpoint."),
            _ => throw new InvalidOperationException("Multiple databases on the endpoint; specify one with --database.")
        };
    }
}

internal sealed class TomServerModelSession : IModelSession, IModelDeploySession, IModelExportSession
{
    private readonly TabularServer _server;
    private readonly TabularDatabase _database;
    private readonly IAccessTokenProvider? _tokenProvider;

    public TomServerModelSession(TabularServer server, TabularDatabase database, IAccessTokenProvider? tokenProvider)
    {
        _server = server;
        _database = database;
        _tokenProvider = tokenProvider;
    }

    public Task<ModelSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = ModelName();
        return Task.FromResult(TomModelSummarizer.Summarize(_database, name)
            with { DatabaseName = string.IsNullOrWhiteSpace(_database.Name) ? null : _database.Name });
    }

    public Task<ModelSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TomModelSummarizer.Snapshot(_database, ModelName()));
    }

    public ValueTask DisposeAsync()
    {
        if (_server.Connected)
            _server.Disconnect();

        _server.Dispose();
        return ValueTask.CompletedTask;
    }

    public Task<ModelDeployResult> DeployAsync(
        ModelDeployRequest request,
        CancellationToken cancellationToken)
        => TomModelDeployer.DeployAsync(_database, request, _tokenProvider, cancellationToken);

    public string GenerateScript(ModelDeployRequest request)
        => TomModelDeployer.GenerateScript(_database, request);

    // Serializes the live remote model to a local tmdl/bim target — the "remote -> local" half of
    // workspace mirroring. Reuses the same exporter the file sessions use.
    public Task<ModelExportResult> ExportAsync(
        ModelExportRequest request,
        CancellationToken cancellationToken)
        => TomModelExporter.ExportAsync(_database, request, cancellationToken);

    private string ModelName()
        => string.IsNullOrWhiteSpace(_database.Name) ? _database.ID : _database.Name;
}
