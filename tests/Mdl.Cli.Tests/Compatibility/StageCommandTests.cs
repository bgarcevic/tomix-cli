using System.Text.Json.Nodes;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class StageCommandTests
{
    [Fact]
    public void StageAcrossInvocations_ThenCommit_PromotesAllEditsToSource()
    {
        using var state = TempState();
        var model = state.CopyModel();
        var salesFile = Path.Combine(model, "tables", "Sales.tmdl");

        // Two separate CLI processes stage onto the same working copy (cross-invocation accumulation).
        Assert.Equal(0, Mdl(state, "set", "Sales/Total Sales", "-q", "description", "-i", "compat staged desc",
            "--stage", "--model", model).ExitCode);
        Assert.Equal(0, Mdl(state, "set", "Sales/Total Sales", "-q", "formatString", "-i", "COMPATFMT",
            "--stage", "--model", model).ExitCode);

        var status = Mdl(state, "stage", "status", "--model", model, "--output-format", "json");
        Assert.Equal(0, status.ExitCode);
        var statusJson = JsonObject(status);
        Assert.True(statusJson["staged"]!.GetValue<bool>());
        Assert.Equal(2, statusJson["opCount"]!.GetValue<int>());

        // Source is untouched until commit.
        Assert.DoesNotContain("compat staged desc", File.ReadAllText(salesFile));

        var commit = Mdl(state, "stage", "commit", "--model", model);
        Assert.Equal(0, commit.ExitCode);

        var source = File.ReadAllText(salesFile);
        Assert.Contains("compat staged desc", source);
        Assert.Contains("COMPATFMT", source);

        var after = Mdl(state, "stage", "status", "--model", model, "--output-format", "json");
        Assert.False(JsonObject(after)["staged"]!.GetValue<bool>());
    }

    [Fact]
    public void StageAddObject_ThenCommit_AddsToSource()
    {
        using var state = TempState();
        var model = state.CopyModel();

        var add = Mdl(state, "add", "Sales/StagedMeasure", "-t", "Measure", "-i", "1",
            "--stage", "--model", model, "--output-format", "json");
        Assert.Equal(0, add.ExitCode);
        Assert.True(JsonObject(add)["staged"]!.GetValue<bool>());

        Assert.Equal(0, Mdl(state, "stage", "commit", "--model", model).ExitCode);
        Assert.Contains("StagedMeasure", File.ReadAllText(Path.Combine(model, "tables", "Sales.tmdl")));
    }

    [Fact]
    public void StageThenRevert_DiscardsWorkingCopy_SourceUntouched()
    {
        using var state = TempState();
        var model = state.CopyModel();
        var salesFile = Path.Combine(model, "tables", "Sales.tmdl");

        Assert.Equal(0, Mdl(state, "set", "Sales/Total Sales", "-q", "description", "-i", "to be reverted",
            "--stage", "--model", model).ExitCode);

        var revert = Mdl(state, "set", "Sales/Total Sales", "--revert", "--model", model);
        Assert.Equal(0, revert.ExitCode);

        Assert.False(JsonObject(Mdl(state, "stage", "status", "--model", model, "--output-format", "json"))["staged"]!.GetValue<bool>());
        Assert.DoesNotContain("to be reverted", File.ReadAllText(salesFile));
    }

    [Fact]
    public void SaveAndStageTogether_IsRejected()
    {
        using var state = TempState();
        var model = state.CopyModel();

        var run = Mdl(state, "set", "Sales/Total Sales", "-q", "description", "-i", "x",
            "--save", "--stage", "--model", model);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--save and --stage are mutually exclusive", run.StdErr);
    }

    [Fact]
    public void StageRemoteSource_IsRejectedWithoutNetwork()
    {
        using var state = TempState();

        var run = Mdl(state, "set", "Sales/Total Sales", "-q", "description", "-i", "x",
            "--stage", "--model", "powerbi://api.powerbi.com/v1.0/myorg/ws");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Staging is not supported for remote", run.StdErr);
    }

    [Fact]
    public void CommitWithNothingStaged_Fails()
    {
        using var state = TempState();
        var model = state.CopyModel();

        var commit = Mdl(state, "stage", "commit", "--model", model);

        Assert.Equal(1, commit.ExitCode);
        Assert.Contains("Nothing staged to commit", commit.StdErr);
    }

    private static CliRun Mdl(TempStateDirectory state, params string[] args)
        => CliProcess.RunMdlWithEnvironment(state.Environment, args);

    private static JsonObject JsonObject(CliRun run)
        => JsonNode.Parse(CompatibilityText.JsonPrefix(run.StdOut))!.AsObject();

    private static TempStateDirectory TempState()
        => new(Path.Combine(Path.GetTempPath(), $"mdl-stage-test-{Guid.NewGuid():N}"));

    private sealed class TempStateDirectory : IDisposable
    {
        private readonly string _path;

        public TempStateDirectory(string path)
        {
            _path = path;
            Environment = new Dictionary<string, string>
            {
                ["MDL_CONFIG_DIR"] = Path.Combine(path, "cfg"),
                ["MDL_SESSION"] = "compat-test",
                ["LOCALAPPDATA"] = path
            };
        }

        public IReadOnlyDictionary<string, string> Environment { get; }

        /// <summary>Copies the sample TMDL model into the temp dir so mutations never dirty the repo sample.</summary>
        public string CopyModel()
        {
            var source = Path.Combine(CliProcess.RepositoryRoot, "samples", "basic-tmdl");
            var destination = Path.Combine(_path, "model");
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, destination));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = file.Replace(source, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            return destination;
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, recursive: true);
        }
    }
}
