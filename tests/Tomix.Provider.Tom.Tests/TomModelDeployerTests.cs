using Microsoft.AnalysisServices.Tabular;
using Tomix.Core.Models;
using Tomix.Provider.Tom;

namespace Tomix.Provider.Tom.Tests;

public sealed class TomModelDeployerTests
{
    /// <summary>
    /// DeployAsync owns its server via try/finally. A failed connect must surface the
    /// original connection error, not an ObjectDisposedException from the cleanup path.
    /// </summary>
    [Fact]
    public async Task DeployAsync_ConnectFailure_SurfacesConnectionError()
    {
        var db = new Database { Name = "M", Model = new Model { Name = "Model" } };
        var request = new ModelDeployRequest("localhost:1", "M", DeployFull: true, CreateOnly: false, Force: false);

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => TomModelDeployer.DeployAsync(db, request, tokenProvider: null, CancellationToken.None));

        Assert.IsNotType<ObjectDisposedException>(ex);
    }

    [Fact]
    public async Task DeployAsync_RemoteWithoutToken_RequiresAuthentication()
    {
        var db = new Database { Name = "M", Model = new Model { Name = "Model" } };
        var request = new ModelDeployRequest("powerbi://api.powerbi.com/v1.0/myorg/W", "M", DeployFull: true, CreateOnly: false, Force: false);

        await Assert.ThrowsAsync<Tomix.Core.Authentication.AuthenticationRequiredException>(
            () => TomModelDeployer.DeployAsync(db, request, tokenProvider: null, CancellationToken.None));
    }
}
