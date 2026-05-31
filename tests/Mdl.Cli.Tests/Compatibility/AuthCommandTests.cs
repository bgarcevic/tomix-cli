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
    public void Status_Json_WhenNotLoggedIn_ReportsLoggedInFalse()
    {
        using var state = TempState();

        var status = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "status", "--output-format", "json");

        Assert.Equal(0, status.ExitCode);
        var json = JsonNode.Parse(CompatibilityText.JsonPrefix(status.StdOut))!.AsObject();
        Assert.False(json["loggedIn"]!.GetValue<bool>());
    }

    [Fact]
    public void Logout_WhenNotLoggedIn_ReportsNotLoggedIn()
    {
        using var state = TempState();

        var logout = CliProcess.RunMdlWithEnvironment(state.Environment, "auth", "logout");

        Assert.Equal(0, logout.ExitCode);
        Assert.Contains("Not logged in", logout.StdOut);
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
