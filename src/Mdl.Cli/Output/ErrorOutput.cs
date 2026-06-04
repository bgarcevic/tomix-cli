using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mdl.Core.Diagnostics;
using Spectre.Console;

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

        var errConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

        foreach (var diagnostic in diagnostics)
        {
            var label = diagnostic.Severity switch
            {
                DiagnosticSeverity.Info => Styling.SeverityMarkup("Info"),
                DiagnosticSeverity.Warning => Styling.SeverityMarkup("Warning"),
                _ => Styling.SeverityMarkup("Error")
            };
            var message = Styling.MarkupEscape(diagnostic.Message);
            errConsole.MarkupLine($"{label}: {message}");
        }
    }
}
