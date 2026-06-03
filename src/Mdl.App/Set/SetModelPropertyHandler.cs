using Mdl.App.Mutations;
using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Set;

public sealed class SetModelPropertyHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public SetModelPropertyHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<SetModelPropertyResult>> HandleAsync(
        SetModelPropertyRequest request,
        CancellationToken cancellationToken)
    {
        var options = new MutationOptions(
            request.Save, request.SaveTo, request.Stage, request.Revert, request.Serialization, request.Force);
        var stagingStore = new StagingStore();
        var connection = new CliStateStore().LoadCurrentSession();

        var begin = await MutationLifecycle.BeginAsync(
            _providers, request.Model, options, stagingStore, connection, cancellationToken);
        if (begin.Error is { } error)
            return MdlResult<SetModelPropertyResult>.Fail(error.Code, error.Message, error.ExitCode);

        if (begin.Mode == MutationMode.Revert)
        {
            stagingStore.Discard(request.Model);
            return MdlResult<SetModelPropertyResult>.Ok(new SetModelPropertyResult(
                request.Path, Property: "", Value: "", Saved: false, ValidationErrors: 0));
        }

        if (request.Properties.Count == 0)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_SET_PROPERTY_REQUIRED",
                "At least one -q/-i property assignment is required.",
                exitCode: 2);

        var context = begin.Context!;
        var provider = _providers.FirstOrDefault(p => p.CanOpen(context.EffectiveModel));
        if (provider is null)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {context.EffectiveModel.Value}",
                exitCode: 2);

        await using var session = await provider.OpenAsync(context.EffectiveModel, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {context.EffectiveModel.Value}");

        try
        {
            var mutation = mutator.SetProperty(new ModelObjectSetRequest(
                request.Path,
                request.Properties,
                request.Type));

            var property = mutation.Property ?? request.Properties[^1].Property;
            var outcome = await MutationLifecycle.CompleteAsync(
                mutator, context, "set", $"set {mutation.Path}.{property}", cancellationToken);

            return MdlResult<SetModelPropertyResult>.Ok(new SetModelPropertyResult(
                mutation.Path,
                property,
                mutation.Value ?? request.Properties[^1].Value,
                outcome.Saved,
                ValidationErrors: 0,
                // set never exposed a staged field; surface it only when actually staged.
                outcome.Staged == true ? true : null));
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<SetModelPropertyResult>.Fail("MDL_MUTATION_UNSUPPORTED", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return MdlResult<SetModelPropertyResult>.Fail("MDL_MUTATION_INVALID_VALUE", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<SetModelPropertyResult>.Fail("MDL_MUTATION_FAILED", ex.Message);
        }
        catch (IOException ex)
        {
            return MdlResult<SetModelPropertyResult>.Fail("MDL_MUTATION_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }
}
