using System.CommandLine;
using Mdl.App.Doctor;
using Mdl.Cli.Output;
using Mdl.Core.Doctor;
using Spectre.Console;

namespace Mdl.Cli.Commands;

internal sealed class DoctorCommand : ICommandModule
{
    private readonly string _version;

    public DoctorCommand(string version) => _version = version;

    public Command Build()
    {
        var format = OutputFormats.CreateOption();

        var command = new Command("doctor", "Check whether the local MDL environment is ready.")
        {
            format
        };

        command.SetAction(parseResult =>
        {
            var formatValue = parseResult.GetValue(format) ?? OutputFormats.Text;

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var result = new DoctorHandler().Handle(_version);
            return CommandOutput.Render(result, formatValue, Render);
        });

        return command;
    }

    private static void Render(DoctorResult result)
    {
        AnsiConsole.MarkupLine(Styling.Title("MDL doctor"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(Styling.KeyValue("Version:          ", result.Version));
        AnsiConsole.MarkupLine(Styling.KeyValue("Operating system: ", result.OperatingSystem));
        AnsiConsole.MarkupLine(Styling.KeyValue(".NET version:     ", result.DotNetVersion));
        AnsiConsole.MarkupLine(Styling.KeyValue("Config directory: ", result.ConfigDirectory));
        AnsiConsole.WriteLine();

        foreach (var check in result.Checks)
        {
            var statusMarkup = check.Status switch
            {
                DoctorCheckStatus.Pass => Styling.Success("OK"),
                DoctorCheckStatus.Warning => Styling.Warning("WARN"),
                DoctorCheckStatus.Fail => Styling.Error("FAIL"),
                _ => Styling.Muted("UNKNOWN")
            };

            AnsiConsole.MarkupLine($"{statusMarkup} {Styling.MarkupEscape(check.Name)}: {Styling.MarkupEscape(check.Message)}");
        }
    }
}
