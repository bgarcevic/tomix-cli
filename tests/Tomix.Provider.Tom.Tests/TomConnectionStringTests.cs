using Tomix.Core.Models;
using Tomix.Provider.Tom;

namespace Tomix.Provider.Tom.Tests;

/// <summary>
/// Guards the remote connect timeout.
/// </summary>
/// <remarks>
/// The wall-clock behaviour can only be confirmed against a real unreachable endpoint — AMO owns
/// what <c>Connect Timeout</c> does — so this pins the half that is checkable offline: the key is
/// present for remote references and absent for a local Desktop instance. Before this, the
/// provider emitted no timeout of any kind, and every command that opens a remote model could
/// hang forever with no Ctrl-C escape.
/// </remarks>
public sealed class TomConnectionStringTests
{
    [Theory]
    [InlineData("powerbi://api.powerbi.com/v1.0/myorg/Sales")]
    [InlineData("asazure://westeurope.asazure.windows.net/myserver")]
    [InlineData("MyWorkspace")]
    public void RemoteReference_CarriesConnectTimeout(string endpoint)
    {
        var connectionString = TomServerModelProvider.BuildConnectionString(
            new ModelReference(endpoint, Database: null));

        Assert.Contains("Connect Timeout=30", connectionString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost:59962")]
    [InlineData("localhost:12345")]
    public void LocalDesktopInstance_OmitsConnectTimeout(string endpoint)
    {
        var reference = new ModelReference(endpoint, Database: null);
        Assert.True(reference.IsLocalInstance, $"'{endpoint}' should classify as a local instance.");

        var connectionString = TomServerModelProvider.BuildConnectionString(reference);

        Assert.DoesNotContain("Connect Timeout", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_StillPrecedesTheTimeout()
    {
        var connectionString = TomServerModelProvider.BuildConnectionString(
            new ModelReference("powerbi://api.powerbi.com/v1.0/myorg/Sales", "SalesModel"));

        Assert.Contains("Initial Catalog=SalesModel", connectionString, StringComparison.Ordinal);
        Assert.Contains("Connect Timeout=30", connectionString, StringComparison.Ordinal);
    }
}
