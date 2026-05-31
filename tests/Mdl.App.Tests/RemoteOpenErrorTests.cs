using Mdl.App.Info;
using Mdl.App.Ls;
using Mdl.Core.Authentication;
using Mdl.Core.Models;

namespace Mdl.App.Tests;

public sealed class RemoteOpenErrorTests
{
    private static readonly ModelReference RemoteRef =
        ModelReference.Remote("powerbi://api.powerbi.com/v1.0/myorg/Workspace", "Sales");

    [Fact]
    public async Task Info_MapsAuthenticationRequired_ToDiagnostic()
    {
        var handler = new InfoModelHandler([new ThrowingProvider(new AuthenticationRequiredException("login please"))]);

        var result = await handler.HandleAsync(new InfoModelRequest(RemoteRef), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MDL_AUTH_REQUIRED", result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task Info_MapsRemoteFailure_ToConnectFailed()
    {
        var handler = new InfoModelHandler([new ThrowingProvider(new InvalidOperationException("server down"))]);

        var result = await handler.HandleAsync(new InfoModelRequest(RemoteRef), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MDL_CONNECT_FAILED", result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task Ls_MapsAuthenticationRequired_ToDiagnostic()
    {
        var handler = new LsModelHandler([new ThrowingProvider(new AuthenticationRequiredException("login please"))]);

        var result = await handler.HandleAsync(new LsModelRequest(RemoteRef, PathFilter: null, Type: null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MDL_AUTH_REQUIRED", result.Diagnostics[0].Code);
    }

    private sealed class ThrowingProvider : IModelProvider
    {
        private readonly Exception _error;

        public ThrowingProvider(Exception error) => _error = error;

        public bool CanOpen(ModelReference reference) => reference.IsRemote;

        public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
            => Task.FromException<IModelSession>(_error);
    }
}
