using Mdl.App.Format;
using Mdl.Core.Models;

namespace Mdl.App.Tests;

public sealed class FormatModelHandlerTests
{
    [Fact]
    public async Task HandleAsync_InlineDax_UsesFormatterOptions()
    {
        var formatter = new RecordingFormatter();
        var handler = new FormatModelHandler([], formatter);

        var result = await handler.HandleAsync(
            new FormatModelRequest(
                new ModelReference(""),
                Expression: "SUM(Sales[Amount])",
                Path: null,
                Language: "dax",
                Type: null,
                Long: true,
                Semicolons: true,
                NoSpaceAfterFunction: true,
                Save: false,
                SaveTo: null),
            CancellationToken.None);

        Assert.True(result.Success);
        var inline = Assert.IsType<InlineFormatResult>(result.Data);
        Assert.Equal("formatted:dax:SUM(Sales[Amount])", inline.Formatted);

        var request = Assert.Single(formatter.Requests);
        Assert.Equal("dax", request.Language);
        Assert.True(request.Long);
        Assert.True(request.Semicolons);
        Assert.True(request.NoSpaceAfterFunction);
    }

    [Fact]
    public async Task HandleAsync_ObjectPathSave_SetsFormattedExpressionAndSaves()
    {
        var session = new StubSession(Snapshot());
        var formatter = new RecordingFormatter();
        var handler = new FormatModelHandler([new StubProvider(session)], formatter);

        var result = await handler.HandleAsync(
            new FormatModelRequest(
                new ModelReference("any"),
                Expression: null,
                Path: "Sales/Total Sales",
                Language: "",
                Type: ModelObjectKind.Measure,
                Long: false,
                Semicolons: false,
                NoSpaceAfterFunction: false,
                Save: true,
                SaveTo: "out"),
            CancellationToken.None);

        Assert.True(result.Success);
        var obj = Assert.IsType<ObjectFormatResult>(result.Data);
        Assert.Equal("Sales/Total Sales", obj.Path);
        Assert.Equal("dax", obj.Language);
        Assert.Equal("formatted", obj.Status);

        var mutation = Assert.Single(session.SetRequests);
        Assert.Equal("Sales/Total Sales", mutation.Path);
        Assert.Equal(ModelObjectKind.Measure, mutation.Type);
        Assert.Equal("formatted:dax:SUM(Sales[Amount])", mutation.Properties.Single().Value);
        Assert.Equal("out", session.SaveOutputPath);
    }

    [Fact]
    public async Task HandleAsync_WholeModelDefault_FormatsMeasuresOnly()
    {
        var formatter = new RecordingFormatter();
        var handler = new FormatModelHandler([new StubProvider(new StubSession(Snapshot()))], formatter);

        var result = await handler.HandleAsync(
            new FormatModelRequest(
                new ModelReference("any"),
                Expression: null,
                Path: null,
                Language: "",
                Type: null,
                Long: false,
                Semicolons: false,
                NoSpaceAfterFunction: false,
                Save: false,
                SaveTo: null),
            CancellationToken.None);

        Assert.True(result.Success);
        var model = Assert.IsType<ModelFormatResult>(result.Data);
        Assert.Equal(2, model.Total);
        Assert.Equal(2, formatter.Requests.Count);
        Assert.All(formatter.Requests, request => Assert.Equal("dax", request.Language));
        Assert.Equal(["Total Sales", "Order Count"], model.Results.Select(r => r.Measure));
    }

    [Fact]
    public async Task HandleAsync_PowerQueryLanguage_FormatsPartitions()
    {
        var formatter = new RecordingFormatter();
        var handler = new FormatModelHandler([new StubProvider(new StubSession(Snapshot()))], formatter);

        var result = await handler.HandleAsync(
            new FormatModelRequest(
                new ModelReference("any"),
                Expression: null,
                Path: null,
                Language: "m",
                Type: null,
                Long: true,
                Semicolons: false,
                NoSpaceAfterFunction: false,
                Save: false,
                SaveTo: null),
            CancellationToken.None);

        Assert.True(result.Success);
        var model = Assert.IsType<ModelFormatResult>(result.Data);
        var row = Assert.Single(model.Results);
        Assert.Null(row.Measure);
        Assert.Equal("Sales", row.Partition);
        Assert.Equal("powerquery", Assert.Single(formatter.Requests).Language);
    }

    private static ModelSnapshot Snapshot()
    {
        var totalSales = new ModelObject(
            "Total Sales", ModelObjectKind.Measure, "Sales/Total Sales",
            Detail: null, Expression: "SUM(Sales[Amount])", Description: null, Hidden: false,
            SourceColumn: null, Children: []);
        var orderCount = new ModelObject(
            "Order Count", ModelObjectKind.Measure, "Sales/Order Count",
            Detail: null, Expression: "COUNTROWS(Sales)", Description: null, Hidden: false,
            SourceColumn: null, Children: []);
        var amount = new ModelObject(
            "Amount", ModelObjectKind.Column, "Sales/Amount",
            Detail: "decimal", Expression: null, Description: null, Hidden: false,
            SourceColumn: "Amount", Children: []);
        var partition = new ModelObject(
            "Sales", ModelObjectKind.Partition, "Sales/Sales",
            Detail: "import", Expression: "let Source = 1 in Source", Description: null, Hidden: false,
            SourceColumn: null, Children: []);
        var sales = new ModelObject(
            "Sales", ModelObjectKind.Table, "Sales",
            Detail: "regular", Expression: null, Description: null, Hidden: false,
            SourceColumn: null, Children: [amount, totalSales, orderCount, partition]);

        return new ModelSnapshot("stub", 1601, [sales]);
    }

    private sealed class RecordingFormatter : IExpressionFormatterClient
    {
        public List<ExpressionFormatRequest> Requests { get; } = [];

        public bool CanFormat(string language)
            => language is "dax" or "powerquery";

        public Task<ExpressionFormatResponse> FormatAsync(
            ExpressionFormatRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ExpressionFormatResponse(
                true,
                $"formatted:{request.Language}:{request.Expression}",
                []));
        }
    }

    private sealed class StubProvider : IModelProvider
    {
        private readonly StubSession _session;

        public StubProvider(StubSession session) => _session = session;

        public bool CanOpen(ModelReference reference) => true;

        public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
            => Task.FromResult<IModelSession>(_session);
    }

    private sealed class StubSession : IModelSession, IModelMutationSession
    {
        private readonly ModelSnapshot _snapshot;

        public StubSession(ModelSnapshot snapshot) => _snapshot = snapshot;

        public List<ModelObjectSetRequest> SetRequests { get; } = [];

        public string? SaveOutputPath { get; private set; }

        public Task<ModelSummary> GetSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModelSummary("stub", 1601, 1, 1, 2, 0, 0));

        public Task<ModelSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
            => Task.FromResult(_snapshot);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ModelObjectMutationResult AddObject(ModelObjectAddRequest request)
            => throw new NotSupportedException();

        public ModelObjectMutationResult SetProperty(ModelObjectSetRequest request)
        {
            SetRequests.Add(request);
            return new ModelObjectMutationResult(
                request.Path,
                Changed: true,
                Property: request.Properties.Single().Property,
                Value: request.Properties.Single().Value);
        }

        public ModelObjectMutationResult RemoveObject(ModelObjectRemoveRequest request)
            => throw new NotSupportedException();

        public ModelReplaceResult ReplaceText(ModelReplaceRequest request)
            => throw new NotSupportedException();

        public Task<ModelExportResult> SaveAsync(
            string? outputPath,
            string serialization,
            bool force,
            CancellationToken cancellationToken)
        {
            SaveOutputPath = outputPath;
            return Task.FromResult(new ModelExportResult(outputPath ?? "source", "tmdl"));
        }
    }
}
