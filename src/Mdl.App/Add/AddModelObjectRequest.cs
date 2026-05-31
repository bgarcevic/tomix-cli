using Mdl.Core.Models;

namespace Mdl.App.Add;

public sealed record AddModelObjectRequest(
    ModelReference Model,
    string Path,
    string? Type,
    string? Value,
    IReadOnlyList<ModelPropertyAssignment> Properties,
    bool IfNotExists,
    bool Save,
    string? SaveTo,
    string Serialization,
    bool Force);
