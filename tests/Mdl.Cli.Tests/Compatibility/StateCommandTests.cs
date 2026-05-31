using System.Text.Json.Nodes;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class StateCommandTests
{
    [Fact]
    public void ConnectLocalModel_ThenLoadWithoutModel_UsesActiveSession()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "samples\\basic-tmdl", "--output-format", "json");
        Assert.Equal(0, connect.ExitCode);
        var connectJson = JsonObject(connect);
        Assert.True(connectJson["active"]!.GetValue<bool>());
        Assert.Equal("samples\\basic-tmdl", connectJson["connection"]!["model"]!.GetValue<string>());

        var load = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "load", "--output-format", "json");
        Assert.Equal(0, load.ExitCode);
        Assert.Equal(3, JsonObject(load)["tables"]!.GetValue<int>());
    }

    [Fact]
    public void ProfileSetConnectProfile_ThenLsWithoutModel_UsesProfileModel()
    {
        using var state = TempState();

        Assert.Equal(0, CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "profile", "set", "local", "--model", "samples\\basic-tmdl", "--desc", "Local model", "--output-format", "json").ExitCode);

        var list = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "profile", "list", "--output-format", "json");
        Assert.Equal(0, list.ExitCode);
        Assert.Equal("samples\\basic-tmdl", JsonObject(list)["profiles"]!["local"]!["model"]!.GetValue<string>());

        Assert.Equal(0, CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--profile", "local", "--output-format", "json").ExitCode);

        var ls = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "ls", "--output-format", "json");
        Assert.Equal(0, ls.ExitCode);
        Assert.Equal(3, JsonArray(ls).Count);
    }

    [Fact]
    public void SessionShowClear_RoundTripsActiveState()
    {
        using var state = TempState();

        Assert.Equal(0, CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "samples\\basic-tmdl", "--output-format", "json").ExitCode);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "session", "show", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        Assert.True(JsonObject(show)["exists"]!.GetValue<bool>());

        var clear = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "session", "clear", "--output-format", "json");
        Assert.Equal(0, clear.ExitCode);
        Assert.True(JsonObject(clear)["cleared"]!.GetValue<bool>());

        var after = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");
        Assert.Equal(0, after.ExitCode);
        Assert.False(JsonObject(after)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectGlobalServerDatabase_SetsRemoteActiveConnection()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--server", "workspace", "--database", "model", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.True(json["active"]!.GetValue<bool>());
        Assert.Equal("workspace", json["connection"]!["server"]!.GetValue<string>());
        Assert.Equal("model", json["connection"]!["database"]!.GetValue<string>());
    }

    private static JsonObject JsonObject(CliRun run)
        => JsonNode.Parse(CompatibilityText.JsonPrefix(run.StdOut))!.AsObject();

    private static JsonArray JsonArray(CliRun run)
        => JsonNode.Parse(CompatibilityText.JsonPrefix(run.StdOut))!.AsArray();

    private static TempStateDirectory TempState()
        => new(Path.Combine(Path.GetTempPath(), $"mdl-state-test-{Guid.NewGuid():N}"));

    private sealed class TempStateDirectory : IDisposable
    {
        private readonly string _path;

        public TempStateDirectory(string path)
        {
            _path = path;
            Environment = new Dictionary<string, string>
            {
                ["MDL_CONFIG_DIR"] = path,
                ["MDL_SESSION"] = "compat-test"
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
