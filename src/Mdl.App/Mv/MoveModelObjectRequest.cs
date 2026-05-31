using Mdl.Core.Models;

namespace Mdl.App.Mv;

public sealed record MoveModelObjectRequest(
    ModelReference Model,
    string Source,
    string Destination,
    ModelObjectKind? Type,
    bool Save,
    string? SaveTo,
    string Serialization,
    bool Force);
