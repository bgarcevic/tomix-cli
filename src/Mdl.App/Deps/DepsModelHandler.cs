using System.Text.RegularExpressions;
using Mdl.App.ModelObjects;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Deps;

public sealed partial class DepsModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public DepsModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<DepsModelResult>> HandleAsync(
        DepsModelRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Unused)
            return MdlResult<DepsModelResult>.Fail(
                code: "MDL_DEPS_UNUSED_NOT_IMPLEMENTED",
                message: "Unused dependency analysis is not implemented yet.",
                exitCode: 1);

        if (string.IsNullOrWhiteSpace(request.Path))
            return MdlResult<DepsModelResult>.Fail(
                code: "MDL_DEPS_PATH_REQUIRED",
                message: "A dependency path is required unless --unused is specified.",
                exitCode: 2);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));

        if (provider is null)
            return MdlResult<DepsModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);
        var targetMatches = ModelObjectLookup.Find(snapshot, request.Path, request.Type).ToList();

        if (targetMatches.Count == 0)
            return MdlResult<DepsModelResult>.Fail(
                code: "MDL_OBJECT_NOT_FOUND",
                message: ModelObjectLookup.NotFoundMessage(request.Path),
                exitCode: 1);

        if (targetMatches.Count > 1)
            return MdlResult<DepsModelResult>.Fail(
                code: "MDL_OBJECT_AMBIGUOUS",
                message: $"Object path matched more than one object: {request.Path}",
                exitCode: 1);

        var target = targetMatches[0];
        var objects = ModelObjectProjection.Flatten(snapshot);
        var upstream = FindDependencies(target, objects);
        var downstream = objects
            .Where(o => !ReferenceEquals(o, target) && FindDependencies(o, objects).Any(d => d.Path == target.Path))
            .Select(ToDependency)
            .ToList();

        if (request.UpstreamOnly)
            downstream = [];
        if (request.DownstreamOnly)
            upstream = [];

        return MdlResult<DepsModelResult>.Ok(new DepsModelResult(
            target.Path,
            ModelObjectProjection.KindLabel(target.Kind),
            upstream,
            downstream));
    }

    private static List<DependencyObject> FindDependencies(
        ModelObject obj,
        IReadOnlyList<ModelObject> objects)
    {
        if (string.IsNullOrWhiteSpace(obj.Expression))
            return [];

        var dependencies = new List<DependencyObject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TableObjectReference().Matches(obj.Expression))
        {
            var path = $"{match.Groups["table"].Value}/{match.Groups["object"].Value}";
            AddIfFound(path, objects, dependencies, seen);
        }

        foreach (Match match in LoneMeasureReference().Matches(obj.Expression))
        {
            if (match.Index > 0 && obj.Expression[match.Index - 1] == ']')
                continue;

            AddIfFound(match.Groups["object"].Value, objects, dependencies, seen);
        }

        return dependencies;
    }

    private static void AddIfFound(
        string path,
        IReadOnlyList<ModelObject> objects,
        List<DependencyObject> dependencies,
        HashSet<string> seen)
    {
        var match = objects.FirstOrDefault(o => ModelObjectLookup.Matches(o, path));
        if (match is null || !seen.Add(match.Path))
            return;

        dependencies.Add(ToDependency(match));
    }

    private static DependencyObject ToDependency(ModelObject obj)
        => new(obj.Path, ModelObjectProjection.KindLabel(obj.Kind), ToDaxReference(obj));

    private static string ToDaxReference(ModelObject obj)
    {
        var parts = obj.Path.Split('/', 2);
        if (parts.Length == 2 && obj.Kind is ModelObjectKind.Column or ModelObjectKind.Measure)
            return $"'{parts[0]}'[{parts[1]}]";

        return obj.Name;
    }

    [GeneratedRegex("'?(?<table>[^'\\[\\]\\(\\),]+)'?\\[(?<object>[^\\]]+)\\]")]
    private static partial Regex TableObjectReference();

    [GeneratedRegex("(?<![A-Za-z0-9_'])\\[(?<object>[^\\]]+)\\]")]
    private static partial Regex LoneMeasureReference();
}
