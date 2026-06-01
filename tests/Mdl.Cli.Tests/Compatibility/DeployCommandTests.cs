using System.Text.Json.Nodes;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class DeployCommandTests
{
    [Fact]
    public void DeployHelp_ShowsOptionsMatchingReference()
    {
        var mdl = CliProcess.RunMdl("deploy", "--help");
        Assert.Equal(0, mdl.ExitCode);

        var refOutput = CliProcess.RunReference("deploy", "--help");
        var refOptions = CompatibilityText.CommandSpecificLongOptions(
            CompatibilityText.WithoutPreviewFooter(refOutput.StdOut));

        var mdlOptions = CompatibilityText.CommandSpecificLongOptions(mdl.StdOut);

        foreach (var expected in new[] { "--deploy-full", "--create-only", "--xmla", "--skip-bpa", "--fix-bpa", "--force", "--ci" })
            Assert.Contains(expected, mdlOptions);

        Assert.Contains("--profile", mdlOptions);
        Assert.Contains("-p", mdl.StdOut);
    }

    [Fact]
    public void Deploy_NoArgs_ExitsWithError()
    {
        using var state = TempState();
        var result = CliProcess.RunMdlWithEnvironment(state.Environment, "deploy");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No model specified", result.StdErr);
    }

    [Fact]
    public void Deploy_XmlaDash_OutputsTmslScript()
    {
        var result = CliProcess.RunMdl(
            "deploy", "samples\\basic-tmdl",
            "--server", "my-workspace",
            "--database", "my-model",
            "--skip-bpa",
            "--xmla", "-");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("my-model", result.StdOut);
        Assert.Contains("tables", result.StdOut);
    }

    [Fact]
    public void Deploy_XmlaDash_JsonFormat_IncludesStatus()
    {
        var result = CliProcess.RunMdl(
            "deploy", "samples\\basic-tmdl",
            "--server", "my-workspace",
            "--database", "my-model",
            "--skip-bpa",
            "--xmla", "-",
            "--output-format", "json");

        Assert.Equal(0, result.ExitCode);
        var json = JsonNode.Parse(CompatibilityText.JsonPrefix(result.StdOut))!.AsObject();
        Assert.Equal("script", json["status"]!.GetValue<string>());
        Assert.Equal("my-workspace", json["server"]!.GetValue<string>());
        Assert.Equal("my-model", json["database"]!.GetValue<string>());
    }

    [Fact]
    public void Deploy_XmlaFile_WritesScript()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"mdl-deploy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        try
        {
            var scriptPath = Path.Combine(outputDir, "deploy.json");
            var result = CliProcess.RunMdl(
                "deploy", "samples\\basic-tmdl",
                "--server", "my-workspace",
                "--database", "my-model",
                "--skip-bpa",
                "--xmla", scriptPath);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(scriptPath));
            var content = File.ReadAllText(scriptPath);
            Assert.Contains("my-model", content);
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void Deploy_NoServer_ExitsWithError()
    {
        var result = CliProcess.RunMdl(
            "deploy", "samples\\basic-tmdl",
            "--skip-bpa");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No target workspace specified", result.StdErr);
    }

    private static JsonObject JsonObject(CliRun run)
        => JsonNode.Parse(CompatibilityText.JsonPrefix(run.StdOut))!.AsObject();

    private static TempStateDirectory TempState()
        => new(Path.Combine(Path.GetTempPath(), $"mdl-deploy-test-{Guid.NewGuid():N}"));

    private sealed class TempStateDirectory : IDisposable
    {
        private readonly string _path;

        public TempStateDirectory(string path)
        {
            _path = path;
            Environment = new Dictionary<string, string>
            {
                ["MDL_CONFIG_DIR"] = path,
                ["MDL_SESSION"] = "deploy-test"
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
