using Tomix.App.Bpa;
using Tomix.Core.Models;

namespace Tomix.App.Tests;

public sealed class BpaRulesListHandlerTests
{
    private const string OneRuleJson =
        "[{\"ID\":\"MODEL_RULE\",\"Name\":\"model rule\",\"Category\":\"c\",\"Severity\":2,\"Scope\":\"Table\",\"Expression\":\"true\",\"CompatibilityLevel\":1200}]";

    [Fact]
    public async Task List_WithModel_IncludesModelRuleSourcesAndDiagnostics()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tomix-bpa-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var session = new SnapshotSession(new Dictionary<string, string>
            {
                [$"Annotation:{BpaModelRuleLoader.EmbeddedKey}"] = OneRuleJson,
                [$"Annotation:{BpaModelRuleLoader.ExternalFilesKey}"] = "[\"missing-rules.json\"]"
            });
            var handler = new BpaRulesListHandler([new Provider(session)], new BpaUserRuleState(dir));

            var result = await handler.HandleAsync(
                new BpaRulesListRequest(Model: new ModelReference(dir), NoDefaults: true),
                CancellationToken.None);

            Assert.True(result.Success);
            var rule = Assert.Single(result.Data!.Rules);
            Assert.Equal("MODEL_RULE", rule.Id);
            Assert.Equal("model-embedded", rule.Source);
            var diagnostic = Assert.Single(result.Data.Diagnostics!);
            Assert.Contains("missing-rules.json", diagnostic);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task List_WithoutModel_HasNoDiagnostics()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tomix-bpa-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var handler = new BpaRulesListHandler([], new BpaUserRuleState(dir));

            var result = await handler.HandleAsync(new BpaRulesListRequest(), CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotEmpty(result.Data!.Rules);
            Assert.Null(result.Data.Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class Provider(IModelSession session) : IModelProvider
    {
        public bool CanOpen(ModelReference reference) => true;
        public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken ct) => Task.FromResult(session);
    }

    private sealed class SnapshotSession(IReadOnlyDictionary<string, string> modelProperties) : IModelSession
    {
        public string SourcePath => "";

        public Task<ModelSummary> GetSummaryAsync(CancellationToken ct)
            => Task.FromResult(new ModelSummary("M", 1601, 0, 0, 0, 0, 0));
        public Task<ModelSnapshot> GetSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new ModelSnapshot("M", 1601, [], modelProperties));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
