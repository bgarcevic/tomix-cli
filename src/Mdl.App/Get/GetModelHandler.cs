using Mdl.App.ModelObjects;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Get;

public sealed class GetModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public GetModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<GetModelResult>> HandleAsync(
        GetModelRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));

        if (provider is null)
            return MdlResult<GetModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);
        var matches = ModelObjectLookup.Find(snapshot, request.Path, request.Type).ToList();

        if (matches.Count == 0)
            return MdlResult<GetModelResult>.Fail(
                code: "MDL_OBJECT_NOT_FOUND",
                message: ModelObjectLookup.NotFoundMessage(request.Path),
                exitCode: 1);

        if (matches.Count > 1)
            return MdlResult<GetModelResult>.Fail(
                code: "MDL_OBJECT_AMBIGUOUS",
                message: $"Object path matched more than one object: {request.Path}",
                exitCode: 1);

        var obj = matches[0];
        var properties = ModelObjectProjection.ToProperties(obj);

        if (!string.IsNullOrWhiteSpace(request.Query))
            properties = ProjectSingleProperty(properties, request.Query);

        return MdlResult<GetModelResult>.Ok(new GetModelResult(
            ModelObjectProjection.KindLabel(obj.Kind),
            obj.Path,
            properties,
            obj));
    }

    private static IReadOnlyDictionary<string, object?> ProjectSingleProperty(
        IReadOnlyDictionary<string, object?> properties,
        string query)
    {
        var match = properties.FirstOrDefault(p =>
            string.Equals(p.Key, query, StringComparison.OrdinalIgnoreCase));

        return match.Key is null
            ? new Dictionary<string, object?> { [query] = null }
            : new Dictionary<string, object?> { [match.Key] = match.Value };
    }

}
