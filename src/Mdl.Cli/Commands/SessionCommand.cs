using System.CommandLine;
using Mdl.App.Session;
using Mdl.Cli.Output;

namespace Mdl.Cli.Commands;

internal sealed class SessionCommand : ICommandModule
{
    public Command Build()
    {
        var command = new Command("session", "Show or manage the current terminal session");
        command.SetAction(parseResult => RenderShow(parseResult));
        command.Subcommands.Add(BuildClear());
        command.Subcommands.Add(BuildList());
        command.Subcommands.Add(BuildPrune());
        command.Subcommands.Add(BuildShow());
        return command;
    }

    private static Command BuildShow()
    {
        var command = new Command("show", "Show current session details (ID, file path, active state)");
        command.SetAction(parseResult => RenderShow(parseResult));
        return command;
    }

    private static Command BuildList()
    {
        var command = new Command("list", "List all session files");
        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            return CommandOutput.Render(new SessionHandler().List(), format, RenderList);
        });
        return command;
    }

    private static Command BuildClear()
    {
        var command = new Command("clear", "Clear active state for the current session");
        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            return CommandOutput.Render(
                new SessionHandler().Clear(),
                format,
                result => Console.WriteLine(result.Cleared ? "Cleared current session." : "No active session."));
        });
        return command;
    }

    private static Command BuildPrune()
    {
        var allOption = new Option<bool>("--all")
        {
            Description = "Also remove named and live process sessions. The current session is kept."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Show what would be removed without doing it"
        };
        var command = new Command("prune", "Delete session files whose shell process is no longer running")
        {
            allOption,
            dryRunOption
        };

        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            return CommandOutput.Render(
                new SessionHandler().Prune(parseResult.GetValue(allOption), parseResult.GetValue(dryRunOption)),
                format,
                result => Console.WriteLine(result.DryRun
                    ? $"Would remove {result.Removed} session(s)."
                    : $"Removed {result.Removed} session(s)."));
        });
        return command;
    }

    private static int RenderShow(ParseResult parseResult)
    {
        var format = GlobalOptions.OutputFormatValue(parseResult);
        if (!CommandOutput.TryValidateFormat(format))
            return 2;

        return CommandOutput.Render(new SessionHandler().Show(), format, RenderShowResult);
    }

    private static void RenderShowResult(SessionShowResult result)
    {
        Console.WriteLine($"sessionId: {result.SessionId}");
        Console.WriteLine($"kind:      {result.Kind}");
        Console.WriteLine($"path:      {result.Path}");
        Console.WriteLine($"exists:    {result.Exists}");
        if (result.Active is not null)
            Console.WriteLine($"model:     {result.Active.Model ?? ""}");
    }

    private static void RenderList(SessionListResult result)
    {
        foreach (var session in result.Sessions)
            Console.WriteLine($"{session.SessionId}\t{(session.Current ? "current" : "")}\t{session.Path}");
    }
}
