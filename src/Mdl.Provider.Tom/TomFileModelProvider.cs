using System.Text.Json;
using Microsoft.AnalysisServices.Tabular;
using Mdl.Core.Models;
using CompatibilityMode = Microsoft.AnalysisServices.CompatibilityMode;
using TabularDatabase = Microsoft.AnalysisServices.Tabular.Database;
using TabularJsonSerializer = Microsoft.AnalysisServices.Tabular.JsonSerializer;

namespace Mdl.Provider.Tom;

public sealed class TomFileModelProvider : IModelProvider
{
    public bool CanOpen(ModelReference reference)
        => File.Exists(reference.Value) && IsSupportedExtension(reference.Value);

    public Task<IModelSession> OpenAsync(ModelReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IModelSession>(new TomFileModelSession(reference.Value));
    }

    private static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".bim", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tmsl", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class TomFileModelSession : IModelSession, IModelExportSession, IModelMutationSession
{
    private readonly string _path;
    private TabularDatabase? _database;

    public TomFileModelSession(string path) => _path = path;

    public Task<ModelSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = GetDatabase();
        return Task.FromResult(TomModelSummarizer.Summarize(database, ModelName(database))
            with { DatabaseName = string.IsNullOrWhiteSpace(database.Name) ? null : database.Name });
    }

    public Task<ModelSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = GetDatabase();
        return Task.FromResult(TomModelSummarizer.Snapshot(database, ModelName(database)));
    }

    public ValueTask DisposeAsync()
    {
        _database = null;
        return ValueTask.CompletedTask;
    }

    public Task<ModelExportResult> ExportAsync(
        ModelExportRequest request,
        CancellationToken cancellationToken)
        => TomModelExporter.ExportAsync(GetDatabase(), request, cancellationToken);

    public ModelObjectMutationResult AddObject(ModelObjectAddRequest request)
        => new TomModelMutator(GetDatabase()).AddObject(request);

    public ModelObjectMutationResult SetProperty(ModelObjectSetRequest request)
        => new TomModelMutator(GetDatabase()).SetProperty(request);

    public ModelObjectMutationResult RemoveObject(ModelObjectRemoveRequest request)
        => new TomModelMutator(GetDatabase()).RemoveObject(request);

    public ModelReplaceResult ReplaceText(ModelReplaceRequest request)
        => new TomModelMutator(GetDatabase()).ReplaceText(request);

    public Task<ModelExportResult> SaveAsync(
        string? outputPath,
        string serialization,
        bool force,
        CancellationToken cancellationToken)
        => TomModelExporter.ExportAsync(
            GetDatabase(),
            new ModelExportRequest(
                string.IsNullOrWhiteSpace(outputPath) ? _path : outputPath,
                string.IsNullOrWhiteSpace(serialization) ? InferSerialization(_path) : serialization,
                Force: true,
                SupportingFiles: false),
            cancellationToken);

    private TabularDatabase GetDatabase()
        => _database ??= TabularJsonSerializer.DeserializeDatabase(
            ExtractDatabaseJson(File.ReadAllText(_path)),
            new DeserializeOptions(),
            CompatibilityMode.PowerBI);

    private static string ExtractDatabaseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        foreach (var operation in new[] { "createOrReplace", "create", "alter" })
        {
            if (root.TryGetProperty(operation, out var command) &&
                command.TryGetProperty("database", out var database))
                return database.GetRawText();
        }

        if (root.TryGetProperty("database", out var directDatabase))
            return directDatabase.GetRawText();

        return json;
    }

    private static string ModelName(TabularDatabase database)
        => string.IsNullOrWhiteSpace(database.Name)
            ? Path.GetFileNameWithoutExtension(database.ID)
            : database.Name;

    private static string InferSerialization(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".bim", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tmsl", StringComparison.OrdinalIgnoreCase)
            ? "bim"
            : "tmdl";
    }
}
