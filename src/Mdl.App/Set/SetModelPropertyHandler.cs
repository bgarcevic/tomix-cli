using Mdl.App.Mutations;
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
        if (!request.Revert && request.Properties.Count == 0)
            return MdlResult<SetModelPropertyResult>.Fail(
                "MDL_SET_PROPERTY_REQUIRED",
                "At least one -q/-i property assignment is required.",
                exitCode: 2);

        var options = new MutationOptions(
            request.Save, request.SaveTo, request.Stage, request.Revert, request.Serialization, request.Force);

        return await MutationRunner.RunAsync(
            _providers, request.Model, options, "set",
            async (mutator, _, _) =>
            {
                var mutation = mutator.SetProperty(new ModelObjectSetRequest(
                    request.Path,
                    request.Properties,
                    request.Type));

                var property = mutation.Property ?? request.Properties[^1].Property;
                return (mutation.Changed, $"set {mutation.Path}.{property}",
                    outcome => new SetModelPropertyResult(
                        mutation.Path,
                        property,
                        mutation.Value ?? request.Properties[^1].Value,
                        outcome.Saved,
                        ValidationErrors: 0,
                        outcome.Staged == true ? true : null));
            },
            new SetModelPropertyResult(request.Path, Property: "", Value: "", Saved: false, ValidationErrors: 0),
            cancellationToken);
    }
}
