using Mdl.Core.Models;

namespace Mdl.App.Deploy;

public sealed record DeployModelRequest(
    ModelReference Model,
    string? Server,
    string? Database,
    string? Profile,
    bool DeployFull,
    bool CreateOnly,
    bool SkipBpa,
    bool FixBpa,
    string[]? BpaRules,
    string? XmlaOutput,
    bool Force,
    string? Ci,
    bool DryRun = false);
