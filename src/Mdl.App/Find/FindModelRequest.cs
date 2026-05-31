using Mdl.Core.Models;

namespace Mdl.App.Find;

public sealed record FindModelRequest(
    ModelReference Model,
    string Pattern,
    string Scope,
    bool Regex,
    bool CaseSensitive);
