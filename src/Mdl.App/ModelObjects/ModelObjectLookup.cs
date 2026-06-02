using System.Text.RegularExpressions;
using Mdl.Core.Models;

namespace Mdl.App.ModelObjects;

internal static partial class ModelObjectLookup
{
    public static IReadOnlyList<ModelObject> Find(
        ModelSnapshot snapshot,
        string path,
        ModelObjectKind? type = null)
    {
        var objects = ModelObjectProjection
            .Flatten(snapshot)
            .Where(o => type is null || o.Kind == type)
            .ToList();

        var normalized = NormalizePath(path);
        var exact = objects
            .Where(o => string.Equals(NormalizePath(o.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count > 0)
            return exact;

        return objects
            .Where(o => Matches(o, path))
            .ToList();
    }

    public static bool Matches(ModelObject obj, string path)
    {
        var normalized = NormalizePath(path);

        if (TryParseLoneBracket(path, out var objectName))
            return string.Equals(obj.Name, objectName, StringComparison.OrdinalIgnoreCase);

        return string.Equals(NormalizePath(obj.Path), normalized, StringComparison.OrdinalIgnoreCase) ||
               (!normalized.Contains('/') &&
                string.Equals(obj.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizePath(string path)
    {
        var dax = DaxTableObject().Match(path.Trim());
        if (dax.Success)
            return $"{dax.Groups["table"].Value}/{dax.Groups["object"].Value}";

        return path.Trim().Trim('/').Replace("'", "");
    }

    public static string NotFoundMessage(string path)
        =>
        $"Object '{path}' not found. Expected: table name, '.', or container " +
        "(Tables, Measures, Columns, Hierarchies, Relationships, Roles, Perspectives, " +
        "Cultures, DataSources, CalculationGroups, Expressions, Functions, Annotations)";

    private static bool TryParseLoneBracket(string path, out string name)
    {
        var match = LoneBracketObject().Match(path.Trim());
        name = match.Success ? match.Groups["object"].Value : "";
        return match.Success;
    }

    [GeneratedRegex("^'?(?<table>[^'\\[]+)'?\\[(?<object>[^\\]]+)\\]$")]
    private static partial Regex DaxTableObject();

    [GeneratedRegex("^\\[(?<object>[^\\]]+)\\]$")]
    private static partial Regex LoneBracketObject();
}
