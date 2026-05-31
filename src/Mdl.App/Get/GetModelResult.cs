namespace Mdl.App.Get;

public sealed record GetModelResult(
    string Type,
    string Path,
    IReadOnlyDictionary<string, object?> Properties);
