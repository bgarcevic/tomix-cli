namespace Mdl.App.Deps;

public sealed record DepsModelResult(
    string Path,
    string Type,
    IReadOnlyList<DependencyObject> Upstream,
    IReadOnlyList<DependencyObject> Downstream);

public sealed record DependencyObject(
    string Path,
    string Type,
    string Reference);
