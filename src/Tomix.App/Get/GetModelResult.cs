using Tomix.App.Deps;
using Tomix.App.Ls;

namespace Tomix.App.Get;

/// <summary>
/// The outcome of a read: exactly one member is set, matching <see cref="Mode"/>
/// (<see cref="GetMode.Deps"/> and <see cref="GetMode.Unused"/> both fill <see cref="Deps"/>).
/// </summary>
public sealed record GetModelResult(
    GetMode Mode,
    GetObjectResult? Object = null,
    LsModelResult? List = null,
    DepsModelResult? Deps = null);
