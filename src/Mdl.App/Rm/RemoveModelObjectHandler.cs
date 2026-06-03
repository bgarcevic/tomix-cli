using Mdl.App.Mutations;
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
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<RemoveModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<RemoveModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {request.Model.Value}");

        try
        {
            var mutation = mutator.RemoveObject(new ModelObjectRemoveRequest(
                request.Path,
                request.Type,
                request.IfExists));

            if (!mutation.Changed)
                return MdlResult<RemoveModelObjectResult>.Ok(
                    new RemoveModelObjectResult(false, null, null, mutation.Reason, mutation.Path));

            if (request.DryRun || !MutationSave.Requested(request.Save, request.SaveTo))
                return MdlResult<RemoveModelObjectResult>.Ok(
                    new RemoveModelObjectResult(mutation.Path, false, false, null, null));

            var saved = await MutationSave.RunAsync(
                mutator, request.SaveTo, request.Serialization, request.Force, cancellationToken);

            return MdlResult<RemoveModelObjectResult>.Ok(
                new RemoveModelObjectResult(mutation.Path, saved, null, null, null));
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
