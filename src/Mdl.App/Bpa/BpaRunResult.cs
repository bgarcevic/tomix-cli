using Mdl.Core.Bpa;

namespace Mdl.App.Bpa;

public sealed record BpaRunResult(
    IReadOnlyList<BpaViolation> Violations,
    string ModelName,
    int RulesEvaluated,
    long DurationMs = 0);
