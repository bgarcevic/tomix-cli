using System.Text.RegularExpressions;
using Mdl.App.ModelObjects;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Find;

public sealed class FindModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public FindModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<FindModelResult>> HandleAsync(
        FindModelRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));

        if (provider is null)
            return MdlResult<FindModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);

        var matches = new List<FindMatch>();
        foreach (var obj in ModelObjectProjection.Flatten(snapshot).Where(IsSearchableByDefault))
        {
            foreach (var (field, value) in SearchFields(obj, request.Scope))
            {
                if (value is null || !IsMatch(value, request.Pattern, request.Regex, request.CaseSensitive))
                    continue;

                matches.Add(new FindMatch(
                    obj.Path,
                    ModelObjectProjection.KindLabel(obj.Kind),
                    obj.Name,
                    field,
                    value));
                break;
            }
        }

        return MdlResult<FindModelResult>.Ok(new FindModelResult(matches));
    }

    private static bool IsSearchableByDefault(ModelObject obj)
        => obj.Kind is not ModelObjectKind.Partition and not ModelObjectKind.Relationship;

    private static IEnumerable<(string Field, string? Value)> SearchFields(ModelObject obj, string scope)
    {
        var normalized = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();

        if (normalized is "all" or "names")
            yield return ("name", obj.Name);
        if (normalized is "all" or "expressions")
            yield return ("expression", obj.Expression);
        if (normalized is "all" or "descriptions")
            yield return ("description", obj.Description);
        if (normalized is "all" or "formatstrings" or "displayfolders" or "annotations")
            yield break;
    }

    private static bool IsMatch(string value, string pattern, bool regex, bool caseSensitive)
    {
        if (regex)
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(value, pattern, options);
        }

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return value.Contains(pattern, comparison);
    }
}
