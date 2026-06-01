using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mdl.Core.Diagnostics;

namespace Mdl.Cli.Output;

internal static class ErrorOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static void Write(IReadOnlyList<MdlDiagnostic> diagnostics, string? format)
    {
        if (string.Equals(format, OutputFormats.Json, StringComparison.OrdinalIgnoreCase))
        {
            var error = diagnostics.FirstOrDefault(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
                ?? diagnostics.FirstOrDefault();
            Console.Error.WriteLine(JsonSerializer.Serialize(
                new Dictionary<string, string> { ["error"] = error?.Message ?? "" },
                Options));
            return;
        }

        foreach (var diagnostic in diagnostics)
            Console.Error.WriteLine($"{Label(diagnostic.Severity)}: {diagnostic.Message}");
    }

    private static string Label(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Info => "Info",
        DiagnosticSeverity.Warning => "Warning",
        _ => "Error"
    };

}
