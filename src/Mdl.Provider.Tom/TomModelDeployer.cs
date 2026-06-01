using System.Diagnostics;
using Microsoft.AnalysisServices.Tabular;
using Mdl.Core.Authentication;
using Mdl.Core.Models;
using AsAccessToken = Microsoft.AnalysisServices.AccessToken;
using TabularDatabase = Microsoft.AnalysisServices.Tabular.Database;
using TabularJsonSerializer = Microsoft.AnalysisServices.Tabular.JsonSerializer;
using TabularServer = Microsoft.AnalysisServices.Tabular.Server;

namespace Mdl.Provider.Tom;

public static class TomModelDeployer
{
    public static async Task<ModelDeployResult> DeployAsync(
        Database sourceDatabase,
        ModelDeployRequest request,
        IAccessTokenProvider? tokenProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        var server = new TabularServer();
        await ConnectAsync(server, request.Server, tokenProvider, cancellationToken).ConfigureAwait(false);

        var targetName = string.IsNullOrWhiteSpace(request.Database)
            ? sourceDatabase.Name ?? sourceDatabase.ID
            : request.Database;

        var existing = server.Databases.FindByName(targetName);
        if (existing is not null)
        {
            if (request.CreateOnly)
            {
                server.Disconnect();
                server.Dispose();
                throw new InvalidOperationException(
                    $"Model '{targetName}' already exists on '{request.Server}'. Remove --create-only to overwrite.");
            }

            existing.Name = targetName + "_mdl_tmp";
            existing.Update();
            server.Databases.Remove(existing);

            var clone = sourceDatabase.Clone();
            clone.Name = targetName;
            server.Databases.Add(clone);
            clone.Update();
            sw.Stop();
            server.Disconnect();
            server.Dispose();
            return new ModelDeployResult(request.Server, targetName, "updated", sw.ElapsedMilliseconds);
        }

        var created = sourceDatabase.Clone();
        created.Name = targetName;
        server.Databases.Add(created);
        created.Update();
        sw.Stop();
        server.Disconnect();
        server.Dispose();
        return new ModelDeployResult(request.Server, targetName, "created", sw.ElapsedMilliseconds);
    }

    public static string GenerateScript(Database sourceDatabase, ModelDeployRequest request)
    {
        var targetName = string.IsNullOrWhiteSpace(request.Database)
            ? sourceDatabase.Name ?? sourceDatabase.ID
            : request.Database;

        var clone = sourceDatabase.Clone();
        if (!string.IsNullOrWhiteSpace(targetName) && clone.Name != targetName)
            clone.Name = targetName;

        return TabularJsonSerializer.SerializeDatabase(
            clone,
            new SerializeOptions { SplitMultilineStrings = true });
    }

    private static async Task ConnectAsync(
        TabularServer server,
        string endpoint,
        IAccessTokenProvider? tokenProvider,
        CancellationToken cancellationToken)
    {
        if (!ModelReference.IsLocalInstanceEndpoint(endpoint))
        {
            if (tokenProvider is null)
                throw new AuthenticationRequiredException("Not authenticated. Run 'mdl auth login'.");

            var token = await tokenProvider.GetTokenAsync(endpoint, cancellationToken).ConfigureAwait(false);
            server.AccessToken = new AsAccessToken(token.Token, token.ExpiresOn.UtcDateTime);
            server.OnAccessTokenExpired = _ =>
            {
                var refreshed = tokenProvider.GetTokenAsync(endpoint, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                return new AsAccessToken(refreshed.Token, refreshed.ExpiresOn.UtcDateTime);
            };
        }

        server.Connect($"Data Source={ResolveEndpoint(endpoint)}");
    }

    public static string ResolveEndpoint(string server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return server;

        if (server.Contains("://", StringComparison.Ordinal) ||
            server.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase) ||
            server.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            return server;

        return $"powerbi://api.powerbi.com/v1.0/myorg/{server}";
    }
}
