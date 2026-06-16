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
        var options = new MutationOptions(
            request.Save && !request.DryRun,
            request.DryRun ? null : request.SaveTo,
            request.Stage && !request.DryRun,
            request.Revert,
            request.Serialization,
            request.Force);

        return await MutationRunner.RunAsync(
            _providers, request.Model, options, "rm",
            async (mutator, _, _) =>
            {
                var mutation = mutator.RemoveObject(new ModelObjectRemoveRequest(
                    request.Path,
                    request.Type,
                    request.IfExists));

                return (mutation.Changed, $"rm {mutation.Path}",
                    outcome => new RemoveModelObjectResult(
                        mutation.Path, outcome.Saved, outcome.Staged,
                        mutation.Changed ? null : mutation.Reason,
                        mutation.Changed ? null : mutation.Path));
            },
            new RemoveModelObjectResult(false, null, null, null, null),
            cancellationToken);
    }
}
