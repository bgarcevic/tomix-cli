namespace Mdl.Core.Diagnostics;

public sealed record MdlDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? Hint = null,
    string? ObjectPath = null,
    string? File = null,
    int? Line = null,
    int? Column = null);