using Mdl.App.Mutations;
using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Rm;

public sealed class RemoveModelObjectHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public RemoveModelObjectHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<RemoveModelObjectResult>> HandleAsync(
        RemoveModelObjectRequest request,
        CancellationToken cancellationToken)
    {
        // --dry-run is a preview: never persist or stage, regardless of --save/--stage.
        var options = new MutationOptions(
            request.Save && !request.DryRun,
            request.DryRun ? null : request.SaveTo,
            request.Stage && !request.DryRun,
            request.Revert,
            request.Serialization,
            request.Force);
        var stagingStore = new StagingStore();
        var connection = new CliStateStore().LoadCurrentSession();

        var begin = await MutationLifecycle.BeginAsync(
            _providers, request.Model, options, stagingStore, connection, cancellationToken);
        if (begin.Error is { } error)
            return MdlResult<RemoveModelObjectResult>.Fail(error.Code, error.Message, error.ExitCode);

        if (begin.Mode == MutationMode.Revert)
        {
            stagingStore.Discard(request.Model);
            return MdlResult<RemoveModelObjectResult>.Ok(
                new RemoveModelObjectResult(false, null, null, null, null));
        }

        var context = begin.Context!;
        var provider = _providers.FirstOrDefault(p => p.CanOpen(context.EffectiveModel));
        if (provider is null)
            return MdlResult<RemoveModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {context.EffectiveModel.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        await using var session = await provider.OpenAsync(context.EffectiveModel, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<RemoveModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {context.EffectiveModel.Value}");

        try
        {
            var mutation = mutator.RemoveObject(new ModelObjectRemoveRequest(
                request.Path,
                request.Type,
                request.IfExists));

            if (!mutation.Changed)
                return MdlResult<RemoveModelObjectResult>.Ok(
                    new RemoveModelObjectResult(false, null, null, mutation.Reason, mutation.Path));

            var outcome = await MutationLifecycle.CompleteAsync(
                mutator, context, "rm", $"rm {mutation.Path}", cancellationToken);

            return MdlResult<RemoveModelObjectResult>.Ok(
                new RemoveModelObjectResult(mutation.Path, outcome.Saved, outcome.Staged, null, null));
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<RemoveModelObjectResult>.Fail("MDL_MUTATION_UNSUPPORTED", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return MdlResult<RemoveModelObjectResult>.Fail("MDL_MUTATION_INVALID_VALUE", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<RemoveModelObjectResult>.Fail("MDL_MUTATION_FAILED", ex.Message);
        }
        catch (IOException ex)
        {
            return MdlResult<RemoveModelObjectResult>.Fail("MDL_MUTATION_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }
}
