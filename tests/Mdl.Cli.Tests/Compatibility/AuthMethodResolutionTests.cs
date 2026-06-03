using Mdl.Cli.Commands;
using Mdl.Core.Authentication;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class AuthMethodResolutionTests
{
    [Fact]
    public void Default_IsInteractive()
    {
        var method = AuthCommand.ResolveMethod(false, null, null, null, false);
        Assert.Equal(AuthMethod.Interactive, method);
    }

    [Fact]
    public void DeviceCode_WhenFlagSet()
    {
        var method = AuthCommand.ResolveMethod(false, null, null, null, true);
        Assert.Equal(AuthMethod.DeviceCode, method);
    }

    [Fact]
    public void ManagedIdentity_WhenIdentitySet()
    {
        var method = AuthCommand.ResolveMethod(true, null, null, null, false);
        Assert.Equal(AuthMethod.ManagedIdentity, method);
    }

    [Fact]
    public void ManagedIdentity_WithUsername()
    {
        var method = AuthCommand.ResolveMethod(true, null, "user-assigned-client-id", null, false);
        Assert.Equal(AuthMethod.ManagedIdentity, method);
    }

    [Fact]
    public void ServicePrincipalSecret_WithUsernameAndPassword()
    {
        var method = AuthCommand.ResolveMethod(false, null, "client-id", "secret", false);
        Assert.Equal(AuthMethod.ServicePrincipalSecret, method);
    }

    [Fact]
    public void ServicePrincipalCertificate_WithCertificate()
    {
        var method = AuthCommand.ResolveMethod(false, "/path/to/cert.pfx", null, null, false);
        Assert.Equal(AuthMethod.ServicePrincipalCertificate, method);
    }

    [Fact]
    public void IdentityPriorityOverCertificate()
    {
        // identity takes highest priority
        var method = AuthCommand.ResolveMethod(true, "/path/to/cert.pfx", null, null, false);
        Assert.Equal(AuthMethod.ManagedIdentity, method);
    }

    [Fact]
    public void CertificatePriorityOverSpnSecret()
    {
        var method = AuthCommand.ResolveMethod(false, "/path/to/cert.pfx", "client-id", "secret", false);
        Assert.Equal(AuthMethod.ServicePrincipalCertificate, method);
    }

    [Fact]
    public void SpnSecretPriorityOverDeviceCode()
    {
        var method = AuthCommand.ResolveMethod(false, null, "client-id", "secret", true);
        Assert.Equal(AuthMethod.ServicePrincipalSecret, method);
    }

    [Fact]
    public void DeviceCodePriorityOverInteractive()
    {
        var method = AuthCommand.ResolveMethod(false, null, null, null, true);
        Assert.Equal(AuthMethod.DeviceCode, method);
    }

    [Fact]
    public void SpnRequiresBothUsernameAndPassword()
    {
        // Without both username and password, falls through to device-code or interactive
        var method = AuthCommand.ResolveMethod(false, null, "client-id", null, false);
        Assert.Equal(AuthMethod.Interactive, method);
    }

    [Fact]
    public void SpnRequiresBothUsernameAndPassword_EmptyPassword()
    {
        var method = AuthCommand.ResolveMethod(false, null, "client-id", "", false);
        Assert.Equal(AuthMethod.Interactive, method);
    }
}
