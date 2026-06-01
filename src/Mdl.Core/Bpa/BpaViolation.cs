namespace Mdl.Core.Bpa;

public sealed record BpaViolation(
    string RuleId,
    string RuleName,
    string Category,
    BpaSeverity Severity,
    string ObjectType,
    string ObjectName,
    string ObjectPath,
    string? Description = null);
