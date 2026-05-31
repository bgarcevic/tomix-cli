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
        if (request.Properties.Count == 0)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_SET_PROPERTY_REQUIRED",
                "At least one -q/-i property assignment is required.",
                exitCode: 2);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {request.Model.Value}");

        try
        {
            var mutation = mutator.SetProperty(new ModelObjectSetRequest(
                request.Path,
                request.Properties,
                request.Type));

            object saved = false;
            if (request.Save || !string.IsNullOrWhiteSpace(request.SaveTo))
            {
                var export = await mutator.SaveAsync(
                    request.SaveTo,
                    request.Serialization,
                    request.Force,
                    cancellationToken);
                saved = export.SavedPath;
            }

            return MdlResult<SetModelPropertyResult>.Ok(new SetModelPropertyResult(
                mutation.Path,
                mutation.Property ?? request.Properties[^1].Property,
                mutation.Value ?? request.Properties[^1].Value,
                saved,
                ValidationErrors: 0));
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
