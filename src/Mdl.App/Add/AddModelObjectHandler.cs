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
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<AddModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<AddModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {request.Model.Value}");

        try
        {
            var mutation = mutator.AddObject(new ModelObjectAddRequest(
                request.Path,
                request.Type,
                request.Value,
                request.Properties,
                request.IfNotExists));

            if (!request.Save && string.IsNullOrWhiteSpace(request.SaveTo))
                return MdlResult<AddModelObjectResult>.Ok(
                    new AddModelObjectResult(mutation.Changed ? mutation.Path : false, false, false));

            var export = await mutator.SaveAsync(
                request.SaveTo,
                request.Serialization,
                request.Force,
                cancellationToken);

            return MdlResult<AddModelObjectResult>.Ok(
                new AddModelObjectResult(mutation.Changed ? mutation.Path : false, export.SavedPath, null));
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
