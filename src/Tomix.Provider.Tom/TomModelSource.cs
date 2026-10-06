using Microsoft.AnalysisServices.Tabular;
using Tomix.Core.Authentication;
using Tomix.Core.Models;
using TabularServer = Microsoft.AnalysisServices.Tabular.Server;

namespace Tomix.Provider.Tom;

/// <summary>
/// Where a live session's model comes from and goes back to: how to load it, name it, save and
/// export it, and how its checkpoints restore (ADR 0003 §1). Everything else a session does is
/// the same for every source.
/// </summary>
internal abstract class TomModelSource : IAsyncDisposable
{
    protected TomModelSource(ModelReference reference, IAccessTokenProvider? tokenProvider)
    {
        Reference = reference;
        TokenProvider = tokenProvider;
    }

    public ModelReference Reference { get; }

    public IAccessTokenProvider? TokenProvider { get; }

    public abstract string SourcePath { get; }

    /// <summary>The source as messages name it.</summary>
    public virtual string DisplayName => SourcePath;

    public abstract TomCheckpointRestore Restore { get; }

    public abstract Database Load();

    public abstract string ModelName(Database database);

    public ModelSummary Summarize(Database database)
        => TomModelSummarizer.Summarize(database, ModelName(database))
            with
        { DatabaseName = string.IsNullOrWhiteSpace(database.Name) ? null : database.Name };

    public abstract Task<ModelExportResult> ExportAsync(Database database, ModelExportRequest request, CancellationToken cancellationToken);

    public abstract Task<ModelExportResult> SaveAsync(Database database, string? outputPath, string serialization, bool overwrite, CancellationToken cancellationToken);

    /// <summary>Whether a save to <paramref name="outputPath"/> writes the source itself, and so
    /// moves the session's save point.</summary>
    public abstract bool IsInPlace(string? outputPath);

    public abstract ValueTask DisposeAsync();

    /// <summary>Whether <see cref="Load"/> reads the source again, so a session can reload it.</summary>
    public virtual bool CanReload => false;

    /// <summary>
    /// What the source holds now, for telling whether it changed outside the session (#351): equal
    /// fingerprints mean equal content. <c>null</c> when the source cannot tell.
    /// </summary>
    public virtual string? Fingerprint() => null;

    /// <summary>Calls <paramref name="changed"/> when something may have changed the source; the
    /// caller compares <see cref="Fingerprint"/>s to know. <c>null</c> when the source cannot be watched.</summary>
    public virtual IDisposable? Watch(Action changed) => null;

    /// <summary>A TMDL folder, <c>.bim</c> or TMSL file, saved in place in
    /// <paramref name="sourceFormat"/>.</summary>
    public static TomModelSource File(ModelReference reference, string path, string sourceFormat, Func<string, Database> load, IAccessTokenProvider? tokenProvider)
        => new FileSource(reference, path, sourceFormat, load, tokenProvider);

    private sealed class FileSource(ModelReference reference, string path, string sourceFormat, Func<string, Database> load, IAccessTokenProvider? tokenProvider)
        : TomModelSource(reference, tokenProvider)
    {
        public override string SourcePath => path;

        public override TomCheckpointRestore Restore => TomCheckpointRestore.Swap;

        public override Database Load() => load(path);

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public override bool CanReload => true;

        public override string? Fingerprint() => SourceFingerprint.Of(path);

        public override IDisposable? Watch(Action changed) => SourceFingerprint.Watch(path, changed);

        public override bool IsInPlace(string? outputPath)
            => string.IsNullOrWhiteSpace(outputPath) || SamePath(outputPath, path);

        public override string ModelName(Database database) => ModelDisplayName.Resolve(database.Name, path);

        public override Task<ModelExportResult> ExportAsync(Database database, ModelExportRequest request, CancellationToken cancellationToken)
            => TomModelExporter.ExportAsync(
                database,
                SamePath(request.OutputPath, path) ? request with { Overwrite = true } : request,
                cancellationToken);

        public override Task<ModelExportResult> SaveAsync(Database database, string? outputPath, string serialization, bool overwrite, CancellationToken cancellationToken)
        {
            var inPlace = IsInPlace(outputPath);
            var format = InPlaceSerializationGuard.Resolve(inPlace, serialization, sourceFormat);
            return TomModelExporter.ExportAsync(
                database,
                new ModelExportRequest(
                    string.IsNullOrWhiteSpace(outputPath) ? path : outputPath,
                    format,
                    Overwrite: overwrite || inPlace,
                    SupportingFiles: false),
                cancellationToken);
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>
/// A database on an XMLA server or in Power BI Desktop. The session owns the connection. The
/// <see cref="Database"/> belongs to its <see cref="TabularServer"/> and cannot be swapped, so
/// checkpoints restore with <c>CopyTo</c>, and a session cannot reload it.
/// </summary>
/// <remarks>
/// The server's model is watched by polling when it was last modified (#351). Every call that
/// reaches the server first checks that a Power BI Desktop instance is still listening, so a
/// session that outlived Desktop fails with <see cref="ModelSourceUnavailableException"/>.
/// </remarks>
internal sealed class TomServerModelSource(TabularServer server, Database database, ModelReference reference, IAccessTokenProvider? tokenProvider)
    : TomModelSource(reference, tokenProvider)
{
    /// <summary>How often the session asks the server whether its model changed.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    // ExecuteReader wraps the command in Execute/Command; a DMV query goes in a Statement there.
    private const string CatalogsQuery =
        "<Statement xmlns=\"urn:schemas-microsoft-com:xml-analysis\">SELECT [DATABASE_ID], [DATE_MODIFIED], [VERSION] FROM $SYSTEM.DBSCHEMA_CATALOGS</Statement>";

    private const string CubesQuery =
        "<Statement xmlns=\"urn:schemas-microsoft-com:xml-analysis\">SELECT [LAST_SCHEMA_UPDATE] FROM $SYSTEM.MDSCHEMA_CUBES</Statement>";

    public TabularServer Server => server;

    public override string SourcePath => "";

    public override string DisplayName => $"'{ModelName(database)}' on {Reference.Value}";

    public override TomCheckpointRestore Restore => TomCheckpointRestore.CopyTo;

    public override Database Load() => database;

    /// <summary>A save always sends the edits to the server, whatever else it writes.</summary>
    public override bool IsInPlace(string? outputPath) => true;

    public override string ModelName(Database database)
        => ModelDisplayName.Resolve(database.Name, sourcePath: null, fallback: database.ID);

    public override Task<ModelExportResult> ExportAsync(Database database, ModelExportRequest request, CancellationToken cancellationToken)
        => TomModelExporter.ExportAsync(database, request, cancellationToken);

    public override Task<ModelExportResult> SaveAsync(Database database, string? outputPath, string serialization, bool overwrite, CancellationToken cancellationToken)
        => Reach(() => TomServerModelSession.SaveAsync(database, ModelName(database), outputPath, serialization, overwrite, cancellationToken));

    /// <summary>
    /// When the server last changed the database (<c>DBSCHEMA_CATALOGS</c>) and its schema
    /// (<c>MDSCHEMA_CUBES</c>); a refresh can move either. Callers serialize it with every other
    /// use of <see cref="Server"/>.
    /// </summary>
    public override string? Fingerprint()
        => Reach(() =>
        {
            var catalog = Read(CatalogsQuery, null, rows => FingerprintOf(rows, database.ID));
            if (catalog == "missing")
                return catalog;

            var schema = Read(CubesQuery, new Dictionary<string, string> { ["Catalog"] = database.Name }, SchemaUpdateOf);
            return $"{catalog}|{schema}";
        });

    private T Read<T>(string query, System.Collections.IDictionary? properties, Func<System.Data.IDataReader, T> read)
    {
        using var rows = server.ExecuteReader(query, out var results, properties, true)
            ?? throw new InvalidOperationException(
                $"The server did not say when {DisplayName} last changed: {XmlaMessages(results)}");
        return read(rows);
    }

    public override IDisposable? Watch(Action changed)
        => new Timer(_ => changed(), null, PollInterval, PollInterval);

    /// <summary>Runs <paramref name="call"/> against the server, failing fast with
    /// <see cref="ModelSourceUnavailableException"/> when it cannot be reached.</summary>
    public T Reach<T>(Func<T> call)
    {
        if (LocalInstanceGone(Reference.Value, IsPortListening))
            throw Unavailable(null);

        try
        {
            return call();
        }
        catch (Microsoft.AnalysisServices.ConnectionException ex)
        {
            throw Unavailable(ex);
        }
    }

    public override ValueTask DisposeAsync()
    {
        if (server.Connected)
            server.Disconnect();
        server.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The fingerprint of <paramref name="databaseId"/> in a <c>DBSCHEMA_CATALOGS</c>
    /// result, or <c>missing</c> when the server no longer has it.</summary>
    internal static string FingerprintOf(System.Data.IDataReader rows, string databaseId)
    {
        while (rows.Read())
        {
            if (!string.Equals(Convert.ToString(rows["DATABASE_ID"], System.Globalization.CultureInfo.InvariantCulture), databaseId, StringComparison.OrdinalIgnoreCase))
                continue;

            var modified = rows["DATE_MODIFIED"] is DateTime at ? at.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : "";
            var version = Convert.ToString(rows["VERSION"], System.Globalization.CultureInfo.InvariantCulture);
            return $"{modified}|{version}";
        }

        return "missing";
    }

    /// <summary>The latest <c>LAST_SCHEMA_UPDATE</c> in an <c>MDSCHEMA_CUBES</c> result.</summary>
    internal static string SchemaUpdateOf(System.Data.IDataReader rows)
    {
        DateTime? latest = null;
        while (rows.Read())
        {
            if (rows["LAST_SCHEMA_UPDATE"] is DateTime at && (latest is null || at > latest))
                latest = at;
        }

        return latest?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>Whether <paramref name="endpoint"/> is a local instance (Power BI Desktop) that no
    /// longer listens: nothing is left to answer, so a call would only fail or wait.</summary>
    internal static bool LocalInstanceGone(string endpoint, Func<int, bool> isListening)
    {
        if (!ModelReference.IsLocalInstanceEndpoint(endpoint))
            return false;

        var separator = endpoint.LastIndexOf(':');
        return int.TryParse(endpoint.AsSpan(separator + 1), out var port) && !isListening(port);
    }

    private static string XmlaMessages(Microsoft.AnalysisServices.XmlaResultCollection? results)
    {
        var messages = new List<string>();
        if (results is not null)
            XmlaResultHelper.ExtractMessages(results, messages);
        return messages.Count > 0 ? string.Join("; ", messages) : "no rows.";
    }

    private ModelSourceUnavailableException Unavailable(Exception? inner)
        => new(
            DisplayName,
            ModelReference.IsLocalInstanceEndpoint(Reference.Value)
                ? $"The Power BI Desktop instance at {Reference.Value} is no longer running."
                : $"The server at {Reference.Value} cannot be reached: {inner?.Message}",
            inner);

    private static bool IsPortListening(int port)
    {
        try
        {
            return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(listener => listener.Port == port);
        }
        catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException or PlatformNotSupportedException)
        {
            // Cannot tell: let the call find out.
            return true;
        }
    }
}
