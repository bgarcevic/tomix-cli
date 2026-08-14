using Tomix.Core.Models;

namespace Tomix.Core.Tests;

/// <summary>
/// Guards the remote connect timeout and the one place it is applied.
/// </summary>
/// <remarks>
/// The wall-clock behaviour can only be confirmed against a real unreachable endpoint — AMO owns
/// what <c>Connect Timeout</c> does — so these pin the half that is checkable offline: the key is
/// present for remote endpoints, absent for a local Desktop instance, and no other source file
/// hand-builds a client connection string that could miss it.
/// </remarks>
public sealed class XmlaConnectionStringTests
{
    [Theory]
    [InlineData("powerbi://api.powerbi.com/v1.0/myorg/Sales")]
    [InlineData("asazure://westeurope.asazure.windows.net/myserver")]
    [InlineData("MyWorkspace")]
    public void RemoteEndpoint_CarriesConnectTimeout(string endpoint)
        => Assert.Contains(
            $"Connect Timeout={XmlaConnectionString.RemoteConnectTimeoutSeconds}",
            XmlaConnectionString.Build(endpoint),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("localhost:59962")]
    [InlineData("127.0.0.1:59962")]
    public void LocalDesktopInstance_OmitsConnectTimeout(string endpoint)
    {
        Assert.True(ModelReference.IsLocalInstanceEndpoint(endpoint), $"'{endpoint}' should be a local instance.");
        Assert.DoesNotContain("Connect Timeout", XmlaConnectionString.Build(endpoint), StringComparison.Ordinal);
    }

    [Fact]
    public void Database_PrecedesTheTimeout()
    {
        var connectionString = XmlaConnectionString.Build("powerbi://api.powerbi.com/v1.0/myorg/Sales", "SalesModel");

        Assert.Equal(
            "Data Source=powerbi://api.powerbi.com/v1.0/myorg/Sales;Initial Catalog=SalesModel;Connect Timeout=30",
            connectionString);
    }

    [Fact]
    public void BareWorkspace_IsNormalizedToAnEndpoint()
        => Assert.StartsWith(
            "Data Source=powerbi://api.powerbi.com/v1.0/myorg/MyWorkspace",
            XmlaConnectionString.Build("MyWorkspace"),
            StringComparison.Ordinal);

    /// <summary>
    /// Files allowed to contain a <c>Data Source=</c> literal, and why.
    /// </summary>
    private static readonly Dictionary<string, string> ConnectionStringAuthors = new(StringComparer.Ordinal)
    {
        ["Tomix.Core/Models/XmlaConnectionString.cs"] = "the builder itself",
        ["Tomix.Provider.Tom/TomObjectAdder.cs"] = "writes a partition's data-source definition into the model, not a client connection",
    };

    /// <summary>
    /// The timeout is only worth anything if every client connection goes through the builder.
    /// It first shipped applied at one call site, which left <c>deploy</c> and <c>vertipaq</c>
    /// connecting with no timeout at all — a hand-built string is how that regresses.
    /// </summary>
    [Fact]
    public void OnlyTheBuilder_ConstructsClientConnectionStrings()
    {
        var offenders = Directory
            .EnumerateFiles(RepoPaths.Combine("src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Data Source=", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoPaths.Combine("src"), path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !ConnectionStringAuthors.ContainsKey(relative))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These files build a connection string by hand and so cannot carry the remote connect "
            + $"timeout: {string.Join(", ", offenders)}. Use {nameof(XmlaConnectionString)}.{nameof(XmlaConnectionString.Build)} "
            + $"instead, or add the file to {nameof(ConnectionStringAuthors)} with the reason it is not a client connection.");
    }
}
