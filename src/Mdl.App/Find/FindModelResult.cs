namespace Mdl.App.Find;

public sealed record FindModelResult(IReadOnlyList<FindMatch> Matches);

public sealed record FindMatch(
    string Path,
    string Type,
    string Name,
    string Field,
    string Value);
