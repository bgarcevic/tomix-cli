using Mdl.Core.Models;

namespace Mdl.App.Bpa;

public sealed record BpaRunRequest(
    ModelReference Model,
    IReadOnlyList<string>? RulesFiles = null,
    bool NoDefaults = false,
    string? PathFilter = null,
    IReadOnlyList<string>? RuleIds = null,
    bool Fix = false);
