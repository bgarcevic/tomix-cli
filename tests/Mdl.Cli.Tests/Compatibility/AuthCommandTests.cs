using System.Text.Json.Nodes;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class AuthCommandTests
{
    [Fact]
    public void Status_WhenNotLoggedIn_ReportsCleanly()
    {
        using var state = TempState();

        var status = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "status");

        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Not logged in", status.StdOut);
    }

    [Fact]
    public void Status_Json_WhenNotLoggedIn_ReportsAuthenticatedFalse()
    {
        using var state = TempState();

        var status = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "status", "--output-format", "json");

        Assert.Equal(0, status.ExitCode);
        var json = JsonNode.Parse(CompatibilityText.JsonPrefix(status.StdOut))!.AsObject();
        Assert.False(json["authenticated"]!.GetValue<bool>());
        Assert.False(json["expired"]!.GetValue<bool>());
        Assert.Equal("None", json["method"]!.GetValue<string>());
    }

    [Fact]
    public void Logout_WhenNotLoggedIn_ReportsNotLoggedIn()
    {
        using var state = TempState();

        var logout = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "logout");

        Assert.Equal(0, logout.ExitCode);
        Assert.Contains("Not logged in", logout.StdOut);
    }

    [Fact]
    public void Status_Json_FlatShape_HasRequiredFields()
    {
        using var state = TempState();

        var status = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "status", "--output-format", "json");

        Assert.Equal(0, status.ExitCode);
        var json = JsonNode.Parse(CompatibilityText.JsonPrefix(status.StdOut))!.AsObject();

        // Verify all the flat-shape fields exist
        Assert.True(json.ContainsKey("authenticated"));
        Assert.True(json.ContainsKey("expired"));
        Assert.True(json.ContainsKey("method"));
        Assert.True(json.ContainsKey("account"));
        Assert.True(json.ContainsKey("tenantId"));
        Assert.True(json.ContainsKey("encryptedAtRest"));
        Assert.True(json.ContainsKey("keyStoreMode"));
        Assert.True(json.ContainsKey("expiresOn"));
        Assert.True(json.ContainsKey("tokenAccount"));
        Assert.True(json.ContainsKey("tokenTenantId"));
        Assert.True(json.ContainsKey("tenantMismatch"));
        Assert.True(json.ContainsKey("usernameMismatch"));
    }

    [Fact]
    public void Help_ShowsLoginOptions()
    {
        var help = CliProcess.RunMdl("auth", "login", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("--username", help.StdOut);
        Assert.Contains("--password", help.StdOut);
        Assert.Contains("--tenant", help.StdOut);
        Assert.Contains("--identity", help.StdOut);
        Assert.Contains("--certificate", help.StdOut);
        Assert.Contains("--device-code", help.StdOut);
        Assert.Contains("--client-id", help.StdOut);
        Assert.Contains("--save", help.StdOut);
    }

    [Fact]
    public void Help_PasswordDescriptionShowsEnvVarAndStdin()
    {
        var help = CliProcess.RunMdl("auth", "login", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("AZURE_CLIENT_SECRET", help.StdOut);
        Assert.Contains("stdin", help.StdOut);
    }

    [Fact]
    public void Help_CertificateDescriptionShowsPem()
    {
        var help = CliProcess.RunMdl("auth", "login", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("PEM", help.StdOut);
    }

    [Fact]
    public void Help_ShowsStatusAndLogout()
    {
        var help = CliProcess.RunMdl("auth", "--help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("login", help.StdOut);
        Assert.Contains("status", help.StdOut);
        Assert.Contains("logout", help.StdOut);
    }

    /// <summary>
    /// Run a login attempt with a short timeout. These tests make network calls
    /// and will fail on their own, but we verify the invocation doesn't crash.
    /// </summary>
    [Fact]
    public async Task Login_FailsFast()
    {
        using var state = TempState();

        // SPN with all required args but no real credentials — fails with fast auth error
        var login = await Task.Run(() =>
            CliProcess.RunMdlWithEnvironment(
                state.Environment,
                "auth", "login", "-u", "test-client-id", "-p", "not-a-real-secret", "-t", "test-tenant"));

        Assert.Equal(1, login.ExitCode);
    }

    private static TempStateDirectory TempState()
        => new(Path.Combine(Path.GetTempPath(), $"mdl-auth-test-{Guid.NewGuid():N}"));

    private sealed class TempStateDirectory : IDisposable
    {
        private readonly string _path;

        public TempStateDirectory(string path)
        {
            _path = path;
            Environment = new Dictionary<string, string>
            {
                ["MDL_CONFIG_DIR"] = path,
                ["MDL_SESSION"] = "auth-test"
            };
        }

        public IReadOnlyDictionary<string, string> Environment { get; }

        public void Dispose()
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, recursive: true);
        }
    }
}
