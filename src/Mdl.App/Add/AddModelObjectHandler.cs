using Mdl.App.Mutations;
using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Add;

public sealed class AddModelObjectHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public AddModelObjectHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<AddModelObjectResult>> HandleAsync(
        AddModelObjectRequest request,
        CancellationToken cancellationToken)
    {
        var options = new MutationOptions(
            request.Save, request.SaveTo, request.Stage, request.Revert, request.Serialization, request.Force);
        var stagingStore = new StagingStore();
        var connection = new CliStateStore().LoadCurrentSession();

        var begin = await MutationLifecycle.BeginAsync(
            _providers, request.Model, options, stagingStore, connection, cancellationToken);
        if (begin.Error is { } error)
            return MdlResult<AddModelObjectResult>.Fail(error.Code, error.Message, error.ExitCode);

        if (begin.Mode == MutationMode.Revert)
        {
            stagingStore.Discard(request.Model);
            return MdlResult<AddModelObjectResult>.Ok(new AddModelObjectResult(false, false, null));
        }

        var context = begin.Context!;
        var provider = _providers.FirstOrDefault(p => p.CanOpen(context.EffectiveModel));
        if (provider is null)
            return MdlResult<AddModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {context.EffectiveModel.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        await using var session = await provider.OpenAsync(context.EffectiveModel, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<AddModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {context.EffectiveModel.Value}");

        try
        {
            var mutation = mutator.AddObject(new ModelObjectAddRequest(
                request.Path,
                request.Type,
                request.Value,
                request.Properties,
                request.IfNotExists));

            var added = mutation.Changed ? mutation.Path : (object)false;
            var outcome = await MutationLifecycle.CompleteAsync(
                mutator, context, "add", $"add {mutation.Path}", cancellationToken);

            return MdlResult<AddModelObjectResult>.Ok(
                new AddModelObjectResult(added, outcome.Saved, outcome.Staged));
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<AddModelObjectResult>.Fail("MDL_MUTATION_UNSUPPORTED", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return MdlResult<AddModelObjectResult>.Fail("MDL_MUTATION_INVALID_VALUE", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<AddModelObjectResult>.Fail("MDL_MUTATION_FAILED", ex.Message);
        }
        catch (IOException ex)
        {
            return MdlResult<AddModelObjectResult>.Fail("MDL_MUTATION_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }
}
