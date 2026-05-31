using Mdl.Core.Models;

namespace Mdl.App.Set;

public sealed record SetModelPropertyRequest(
    ModelReference Model,
    string Path,
    IReadOnlyList<ModelPropertyAssignment> Properties,
    ModelObjectKind? Type,
    bool Save,
    string? SaveTo,
    string Serialization,
    bool Force);
