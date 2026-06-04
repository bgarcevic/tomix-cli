using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Stage;

/// <summary>Inspects and clears the session's staged working copies (the <c>mdl stage</c> surface).</summary>
public sealed class StageHandler
{
    private readonly StagingStore _staging;

    public StageHandler()
        : this(new StagingStore())
    {
    }

    public StageHandler(StagingStore staging) => _staging = staging;

    public MdlResult<StageStatusResult> Status(ModelReference source)
    {
        var info = _staging.TryLoad(source);
        if (info is null)
            return MdlResult<StageStatusResult>.Ok(new StageStatusResult(
                Staged: false, source.Value, WorkingCopy: null, Serialization: null,
                Workspace: false, OpCount: 0, Ops: [], CreatedUtc: null, UpdatedUtc: null));

        var manifest = info.Manifest;
        return MdlResult<StageStatusResult>.Ok(new StageStatusResult(
            Staged: true,
            manifest.Source,
            manifest.WorkingCopy,
            manifest.Serialization,
            Workspace: manifest.Workspace is not null,
            manifest.Ops.Count,
            manifest.Ops,
            manifest.CreatedUtc,
            manifest.UpdatedUtc));
    }

    public MdlResult<StageListResult> List()
    {
        var entries = _staging.List()
            .Select(info => new StageListEntry(
                info.Manifest.Source,
                info.Manifest.WorkingCopy,
                info.Manifest.Ops.Count,
                info.Manifest.UpdatedUtc,
                info.IsCurrentSession))
            .ToList();
        return MdlResult<StageListResult>.Ok(new StageListResult(entries));
    }

    public MdlResult<StageDiscardResult> Discard(ModelReference source, bool all)
    {
        var discarded = all ? _staging.DiscardAll() : (_staging.Discard(source) ? 1 : 0);
        return MdlResult<StageDiscardResult>.Ok(new StageDiscardResult(discarded));
    }

    /// <summary>
    /// Promotes the staged working copy onto its source. The local side is always written; the remote
    /// side (workspace mirror) is synced separately. The manifest carries the local target and
    /// (when in workspace mode) the remote endpoint captured at stage time.
    /// </summary>
    public async Task<MdlResult<StageCommitResult>> CommitAsync(
        ModelReference source,
        IReadOnlyList<IModelProvider> providers,
        bool force,
        CancellationToken cancellationToken)
    {
        var info = _staging.TryLoad(source);
        if (info is null)
            return MdlResult<StageCommitResult>.Fail(
                "MDL_STAGE_NOTHING_TO_COMMIT", $"Nothing staged to commit for {source.Value}.", 1);

        var manifest = info.Manifest;

        if (!force && _staging.HasSourceDrifted(manifest, source))
            return MdlResult<StageCommitResult>.Fail(
                "MDL_STAGE_SOURCE_DRIFT",
                $"Source '{manifest.Source}' changed since staging began. Re-stage, or commit with --force to overwrite.",
                1);

        var workingReference = new ModelReference(manifest.WorkingCopy);
        var provider = providers.FirstOrDefault(p => p.CanOpen(workingReference));
        if (provider is null)
            return MdlResult<StageCommitResult>.Fail(
                "MDL_NO_PROVIDER", $"No provider can open working copy: {manifest.WorkingCopy}", 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        string localSaved;
        try
        {
            await using var session = await provider.OpenAsync(workingReference, cancellationToken);
            if (session is not IModelExportSession exporter)
                return MdlResult<StageCommitResult>.Fail(
                    "MDL_STAGE_COMMIT_LOCAL_FAILED", $"Working copy cannot be exported: {manifest.WorkingCopy}", 2);

            var export = await exporter.ExportAsync(
                new ModelExportRequest(manifest.Source, manifest.Serialization, Force: true, SupportingFiles: false),
                cancellationToken);
            localSaved = export.SavedPath;
        }
        catch (IOException ex)
        {
            return MdlResult<StageCommitResult>.Fail(
                "MDL_STAGE_COMMIT_LOCAL_FAILED", $"Failed to write local side: {ex.Message}. Staging kept.", 2);
        }

        _staging.Discard(source);
        return MdlResult<StageCommitResult>.Ok(new StageCommitResult(
            manifest.Source, localSaved, RemoteDeployed: false,
            Server: null, Database: null, DeployDurationMs: null, manifest.Ops.Count));
    }
}
