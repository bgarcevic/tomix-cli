using System.CommandLine;
using Spectre.Console;
using Tomix.Cli.Output;
using Tomix.Core.Diagnostics;

namespace Tomix.Cli.Commands;

internal static class ConfirmationHelper
{
    /// <summary>
    /// Confirms a destructive action: <c>--yes</c> bypasses, and the prompt is shown only when
    /// <see cref="InteractionGate"/> allows it — every non-promptable context (non-interactive,
    /// quiet, json/csv output, redirected stdin/stderr) fails fast with the flag that would
    /// have answered it.
    /// </summary>
    public static bool ConfirmOrAbort(
        string action,
        string subject,
        ParseResult parseResult,
        string outputFormat)
        => Confirm(
            action,
            subject,
            parseResult.GetValue(GlobalOptions.Yes),
            promptForbidden: !InteractionGate.CanPrompt(parseResult, outputFormat),
            GlobalOptions.ErrorFormatValue(parseResult, outputFormat));

    private static bool Confirm(
        string action,
        string subject,
        bool yes,
        bool promptForbidden,
        string? errorFormat)
    {
        if (yes)
            return true;

        if (promptForbidden)
        {
            // Through ErrorOutput, not raw markup: this is the failure a scripted caller actually
            // hits (json/csv output and --non-interactive both forbid the prompt), so it needs a
            // code to branch on rather than prose to grep.
            ErrorOutput.Write(
                [new TomixDiagnostic(
                    "TOMIX_CONFIRMATION_REQUIRED",
                    DiagnosticSeverity.Error,
                    $"Pass --yes to confirm {action}.",
                    $"{action} {subject}")],
                errorFormat);
            return false;
        }

        var errConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });
        return errConsole.Confirm($"  {Styling.MarkupEscape(action)} {Styling.MarkupEscape(subject)}?", defaultValue: false);
    }
}
