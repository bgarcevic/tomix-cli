using Mdl.Core.Models;

namespace Mdl.App.Diff;

public sealed record DiffModelRequest(
    ModelReference Left,
    ModelReference Right);
