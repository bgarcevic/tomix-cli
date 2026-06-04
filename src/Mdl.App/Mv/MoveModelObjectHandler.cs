using Mdl.App.Mutations;
using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Mv;

public sealed class MoveModelObjectHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public MoveModelObjectHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<MoveModelObjectResult>> HandleAsync(
        MoveModelObjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetRename(request.Source, request.Destination, out var newName, out var error))
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MOVE_UNSUPPORTED", error);

        var options = new MutationOptions(
            request.Save, request.SaveTo, request.Stage, request.Revert, request.Serialization, request.Force);
        var stagingStore = new StagingStore();
        var connection = new CliStateStore().LoadCurrentSession();

        var begin = await MutationLifecycle.BeginAsync(
            _providers, request.Model, options, stagingStore, connection, cancellationToken);
        if (begin.Error is { } beginError)
            return MdlResult<MoveModelObjectResult>.Fail(beginError.Code, beginError.Message, beginError.ExitCode);

        if (begin.Mode == MutationMode.Revert)
        {
            stagingStore.Discard(request.Model);
            return MdlResult<MoveModelObjectResult>.Ok(new MoveModelObjectResult(
                NormalizePath(request.Source), NormalizePath(request.Destination), false, null));
        }

        var context = begin.Context!;
        var provider = _providers.FirstOrDefault(p => p.CanOpen(context.EffectiveModel));
        if (provider is null)
            return MdlResult<MoveModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {context.EffectiveModel.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        await using var session = await provider.OpenAsync(context.EffectiveModel, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<MoveModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {context.EffectiveModel.Value}");

        try
        {
            mutator.SetProperty(new ModelObjectSetRequest(
                request.Source,
                [new ModelPropertyAssignment("name", newName)],
                request.Type));

            var outcome = await MutationLifecycle.CompleteAsync(
                mutator, context, "mv", $"mv {request.Source} -> {request.Destination}", cancellationToken);

            return MdlResult<MoveModelObjectResult>.Ok(new MoveModelObjectResult(
                NormalizePath(request.Source), NormalizePath(request.Destination), outcome.Saved, outcome.Staged));
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MUTATION_UNSUPPORTED", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MUTATION_INVALID_VALUE", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MUTATION_FAILED", ex.Message);
        }
        catch (IOException ex)
        {
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MUTATION_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }

    private static bool TryGetRename(
        string source,
        string destination,
        out string newName,
        out string error)
    {
        var sourcePath = NormalizePath(source);
        var destinationPath = NormalizePath(destination);
        var sourceParent = ParentPath(sourcePath);
        var destinationParent = ParentPath(destinationPath);

        if (!string.Equals(sourceParent, destinationParent, StringComparison.OrdinalIgnoreCase))
        {
            newName = "";
            error = "Moving objects between parents is not supported yet.";
            return false;
        }

        newName = LeafName(destinationPath);
        error = "";
        return true;
    }

    private static string ParentPath(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? "" : path[..index];
    }

    private static string LeafName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    private static string NormalizePath(string path)
        => path.Trim().Trim('/').Replace("'", "", StringComparison.Ordinal);
}
