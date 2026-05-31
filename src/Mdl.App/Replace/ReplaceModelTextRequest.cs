using Mdl.Core.Models;

namespace Mdl.App.Replace;

public sealed record ReplaceModelTextRequest(
    ModelReference Model,
    string Pattern,
    string Replacement,
    string Scope,
    bool Regex,
    bool CaseSensitive,
    bool DryRun,
    bool Save,
    string? SaveTo,
    string Serialization,
    bool Force);
