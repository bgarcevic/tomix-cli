namespace Mdl.App.Set;

public sealed record SetModelPropertyResult(
    string Set,
    string Property,
    string Value,
    object Saved,
    int ValidationErrors);
