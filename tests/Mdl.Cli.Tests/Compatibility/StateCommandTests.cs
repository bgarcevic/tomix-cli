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
        // connect validates the model before storing the session and emits its connection summary.
        var connectJson = JsonObject(connect);
        Assert.Equal("local", connectJson["kind"]!.GetValue<string>());
        Assert.Equal(3, connectJson["tables"]!.GetValue<int>());

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
    public void ConnectMissingProfile_FailsWithoutUnhandledException()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--profile", "missing");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("Profile 'missing' not found", connect.StdErr);
        Assert.DoesNotContain("Unhandled exception", connect.StdErr);
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
    public void InteractiveLocalModelNonInteractive_SetsActiveSession()
    {
        using var state = TempState();

        var interactive = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "interactive", "samples\\basic-tmdl", "--non-interactive", "--output-format", "json");

        Assert.Equal(0, interactive.ExitCode);
        var interactiveJson = JsonObject(interactive);
        Assert.True(interactiveJson["active"]!.GetValue<bool>());
        Assert.Equal("samples\\basic-tmdl", interactiveJson["connection"]!["model"]!.GetValue<string>());

        var load = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "load", "--output-format", "json");
        Assert.Equal(0, load.ExitCode);
        Assert.Equal(3, JsonObject(load)["tables"]!.GetValue<int>());
    }

    [Fact]
    public void InteractiveNoModelNonInteractive_DoesNotCreateActiveSession()
    {
        using var state = TempState();

        var interactive = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "interactive", "--non-interactive", "--output-format", "json");

        Assert.Equal(0, interactive.ExitCode);
        Assert.False(JsonObject(interactive)["active"]!.GetValue<bool>());

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "session", "show", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        Assert.False(JsonObject(show)["exists"]!.GetValue<bool>());
    }

    [Fact]
    public void InteractiveExitCommand_LeavesCleanly()
    {
        using var state = TempState();

        var interactive = CliProcess.RunMdlWithEnvironmentAndInput(
            state.Environment,
            "exit\n",
            "interactive", "samples\\basic-tmdl");

        Assert.Equal(0, interactive.ExitCode);
        Assert.Contains("mdl interactive", interactive.StdOut);
        Assert.Contains("mdl>", interactive.StdOut);
    }

    [Fact]
    public void ConnectGlobalServerDatabaseOptions_DoNotSetActiveConnection()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--server", "workspace", "--database", "model", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);
        Assert.False(JsonObject(connect)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectGlobalModelOption_DoesNotSetActiveConnection()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--model", "samples\\basic-tmdl", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);
        Assert.False(JsonObject(connect)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectWorkspaceWithoutPrimarySource_Fails()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("--workspace requires an explicit primary source", connect.StdErr);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        Assert.False(JsonObject(show)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectWorkspaceWithOnlyPrimaryServer_Fails()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "my-workspace", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("--workspace requires both <server> and <database>", connect.StdErr);
    }

    [Fact]
    public void ConnectWorkspaceWithLocalPathPrimaryWithoutWorkspaceDatabase_Fails()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "samples\\basic-tmdl", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("--workspace requires <server> <database> (two values)", connect.StdErr);
    }

    [Fact]
    public void ConnectWorkspaceWithLocalFlag_FailsBeforeDesktopDiscovery()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--local", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("--workspace is not supported with --local", connect.StdErr);
        Assert.DoesNotContain("Discovering Power BI Desktop instances", connect.StdOut);
    }

    [Fact]
    public void ConnectWorkspaceWithProfile_FailsBeforeProfileLookup()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--profile", "missing", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("--workspace cannot be combined with --profile", connect.StdErr);
        Assert.DoesNotContain("Profile 'missing' not found", connect.StdErr);
    }

    [Fact]
    public void ConnectWorkspaceMetadataOptionsWithoutWorkspace_DoNotPersistWorkspaceMetadata()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect",
            "samples\\basic-tmdl",
            "--workspace-format",
            "tmdl",
            "--workspace-auth",
            "env",
            "--output-format",
            "json");

        Assert.Equal(0, connect.ExitCode);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        var connection = JsonObject(show)["connection"]!.AsObject();
        Assert.False(connection.ContainsKey("workspace"));
        Assert.False(connection.ContainsKey("workspaceFormat"));
        Assert.False(connection.ContainsKey("workspaceAuth"));
    }

    [Fact]
    public void ConnectLocalModelName_WithoutDesktopInstance_FailsGracefully()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--local", "Sales", "--auth", "env", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("Discovering Power BI Desktop instances", connect.StdOut);
        Assert.Contains("No running Power BI Desktop instances found", connect.StdErr);
    }

    [Fact]
    public void ConnectLocalModelName_WithDiscoveredUnreachableDesktop_DoesNotPersistSession()
    {
        using var state = TempState();
        var workspace = Path.Combine(
            state.RootPath,
            "Microsoft",
            "Power BI Desktop",
            "AnalysisServicesWorkspaces",
            "AnalysisServicesWorkspace1");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "msmdsrv.port.txt"), "65531");

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--local", "Sales", "--output-format", "json");

        Assert.Equal(1, connect.ExitCode);
        Assert.Contains("Discovering Power BI Desktop instances", connect.StdOut);
        Assert.Contains("Could not connect to 'localhost:65531'", connect.StdErr);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        Assert.False(JsonObject(show)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectWorkspaceOptions_ArePersistedInActiveSession()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect",
            "my-workspace",
            "Sales",
            "--workspace",
            ".\\workspace",
            "--workspace-format",
            "tmdl",
            "--workspace-auth",
            "env",
            "--output-format",
            "json");

        Assert.Equal(0, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.Equal("my-workspace", json["connection"]!["server"]!.GetValue<string>());
        Assert.Equal("Sales", json["connection"]!["database"]!.GetValue<string>());
        Assert.Equal(".\\workspace", json["connection"]!["workspace"]!.GetValue<string>());
        Assert.Equal("tmdl", json["connection"]!["workspaceFormat"]!.GetValue<string>());
        Assert.Equal("env", json["connection"]!["workspaceAuth"]!.GetValue<string>());
    }

    [Fact]
    public void ConnectWorkspaceAuth_DefaultsToAuthOption()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect",
            "my-workspace",
            "Sales",
            "--workspace",
            ".\\workspace",
            "--auth",
            "env",
            "--output-format",
            "json");

        Assert.Equal(0, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.Equal("env", json["connection"]!["auth"]!.GetValue<string>());
        Assert.Equal("env", json["connection"]!["workspaceAuth"]!.GetValue<string>());
    }

    [Fact]
    public void ConnectLocalModelWithLocalWorkspace_ShowDisplaysActivePathMirror()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "samples\\basic-tmdl", "Sales", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.Equal("local", json["kind"]!.GetValue<string>());
        Assert.Equal(".\\workspace", json["mirror"]!["workspace"]!.GetValue<string>());
        Assert.Equal("Sales", json["mirror"]!["database"]!.GetValue<string>());

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect");

        Assert.Equal(0, show.ExitCode);
        Assert.Contains("Active: local model", show.StdOut);
        Assert.Contains("Path:", show.StdOut);
        Assert.Contains("Mirror: .\\workspace / Sales", show.StdOut);
    }

    [Fact]
    public void ConnectRemotePrimaryWithWorkspace_ShowDisplaysActiveMirror()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "my-workspace", "Sales", "--workspace", ".\\workspace", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect");

        Assert.Equal(0, show.ExitCode);
        Assert.Contains("Active: my-workspace / Sales", show.StdOut);
        Assert.Contains("Mirror: .\\workspace / Sales", show.StdOut);
    }

    [Fact]
    public void ConnectWorkspaceForce_PersistsSession()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "my-workspace", "Sales", "--workspace", ".\\workspace", "--force", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.Equal(".\\workspace", json["connection"]!["workspace"]!.GetValue<string>());

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");

        Assert.Equal(0, show.ExitCode);
        Assert.True(JsonObject(show)["active"]!.GetValue<bool>());
    }

    [Fact]
    public void ConnectLocalModelWithoutWorkspace_ShowDoesNotDisplayMirror()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "samples\\basic-tmdl", "--output-format", "json");

        Assert.Equal(0, connect.ExitCode);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect");

        Assert.Equal(0, show.ExitCode);
        Assert.Contains("Active: local model", show.StdOut);
        Assert.DoesNotContain("Mirror:", show.StdOut);
    }

    [Fact]
    public void ConnectLocalPrimaryWorkspaceRemoteEndpointFailure_DoesNotPersistSession()
    {
        using var state = TempState();

        var connect = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect",
            "samples\\basic-tmdl",
            "--workspace",
            "localhost:65531",
            "Sales",
            "--workspace-auth",
            "env",
            "--output-format",
            "json");

        Assert.Equal(1, connect.ExitCode);
        var json = JsonObject(connect);
        Assert.Equal("local", json["kind"]!.GetValue<string>());
        Assert.Equal(3, json["tables"]!.GetValue<int>());
        Assert.Contains("Could not reach workspace server", connect.StdErr);
        Assert.DoesNotContain("Could not reach workspace server: Could not connect to", connect.StdErr);

        var show = CliProcess.RunMdlWithEnvironment(
            state.Environment,
            "connect", "--output-format", "json");
        Assert.Equal(0, show.ExitCode);
        Assert.False(JsonObject(show)["active"]!.GetValue<bool>());
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

        public string RootPath => _path;

        public TempStateDirectory(string path)
        {
            _path = path;
            Environment = new Dictionary<string, string>
            {
                ["MDL_CONFIG_DIR"] = path,
                ["MDL_SESSION"] = "compat-test",
                ["LOCALAPPDATA"] = path
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
