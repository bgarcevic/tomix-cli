using Mdl.Core.Models;

namespace Mdl.App.Get;

public sealed record GetModelRequest(
    ModelReference Model,
    string Path,
    string? Query,
    ModelObjectKind? Type);
