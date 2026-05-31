using Mdl.Core.Models;

namespace Mdl.App.Rm;

public sealed record RemoveModelObjectRequest(
    ModelReference Model,
    string Path,
    ModelObjectKind? Type,
    bool IfExists,
    bool DryRun,
    bool Save,
    string? SaveTo,
    string Serialization,
    bool Force);
