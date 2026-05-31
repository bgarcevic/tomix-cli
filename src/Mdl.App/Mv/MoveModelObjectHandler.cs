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
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<MoveModelObjectResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        if (!TryGetRename(request.Source, request.Destination, out var newName, out var error))
            return MdlResult<MoveModelObjectResult>.Fail("MDL_MOVE_UNSUPPORTED", error);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<MoveModelObjectResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {request.Model.Value}");

        try
        {
            mutator.SetProperty(new ModelObjectSetRequest(
                request.Source,
                [new ModelPropertyAssignment("name", newName)],
                request.Type));

            object saved = false;
            bool? staged = false;
            if (request.Save || !string.IsNullOrWhiteSpace(request.SaveTo))
            {
                var export = await mutator.SaveAsync(
                    request.SaveTo,
                    request.Serialization,
                    request.Force,
                    cancellationToken);
                saved = export.SavedPath;
                staged = null;
            }

            return MdlResult<MoveModelObjectResult>.Ok(
                new MoveModelObjectResult(NormalizePath(request.Source), NormalizePath(request.Destination), saved, staged));
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
