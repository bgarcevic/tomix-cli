using System.CommandLine;
using Mdl.App.Connect;
using Mdl.App.State;
using Mdl.Cli.Output;

namespace Mdl.Cli.Commands;

internal sealed class ConnectCommand : ICommandModule
{
    public Command Build()
    {
        var serverArgument = new Argument<string>("server")
        {
            Description = "Workspace name, endpoint, local model path, or omit with --local.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var databaseArgument = new Argument<string>("database")
        {
            Description = "Semantic model name",
            Arity = ArgumentArity.ZeroOrOne
        };
        var workspaceOption = new Option<string?>("--workspace")
        {
            Description = "Enable workspace mode with a secondary target"
        };
        workspaceOption.Aliases.Add("-w");
        var profileOption = new Option<string?>("--profile")
        {
            Description = "Activate a saved connection profile"
        };
        profileOption.Aliases.Add("-p");
        var clearOption = new Option<bool>("--clear")
        {
            Description = "Clear the active connection"
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite a non-empty workspace target when initializing workspace mode"
        };
        var workspaceFormatOption = new Option<string?>("--workspace-format")
        {
            Description = "On-disk format for a local workspace"
        };
        var workspaceAuthOption = new Option<string?>("--workspace-auth")
        {
            Description = "Auth method for a remote workspace"
        };

        var command = new Command("connect", "Set active connection (workspace, local path, or PBI Desktop). No args = show current.")
        {
            serverArgument,
            databaseArgument,
            workspaceOption,
            profileOption,
            clearOption,
            forceOption,
            workspaceFormatOption,
            workspaceAuthOption
        };

        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var handler = new ConnectHandler();
            if (parseResult.GetValue(clearOption))
                return CommandOutput.Render(
                    handler.Clear(),
                    format,
                    result => Console.WriteLine(result.Cleared ? "Cleared active connection." : "No active connection."));

            var server = parseResult.GetValue(serverArgument) ?? parseResult.GetValue(GlobalOptions.Server);
            var database = parseResult.GetValue(databaseArgument) ?? parseResult.GetValue(GlobalOptions.Database);
            var globalModel = GlobalOptions.ModelValue(parseResult);
            var profile = parseResult.GetValue(profileOption);

            if (string.IsNullOrWhiteSpace(server) &&
                string.IsNullOrWhiteSpace(database) &&
                string.IsNullOrWhiteSpace(globalModel) &&
                string.IsNullOrWhiteSpace(profile) &&
                !parseResult.GetValue(GlobalOptions.Local))
            {
                return CommandOutput.Render(handler.Show(), format, RenderShow);
            }

            var model = !string.IsNullOrWhiteSpace(globalModel)
                ? globalModel
                : LooksLikeLocalModelPath(server) ? server : null;
            var remoteServer = model is null ? server : null;

            return CommandOutput.Render(
                handler.Set(new ConnectSetRequest(
                    remoteServer,
                    database,
                    model,
                    GlobalOptions.AuthValue(parseResult),
                    parseResult.GetValue(GlobalOptions.Local),
                    profile)),
                format,
                result => RenderConnection(result.Connection));
        });

        return command;
    }

    private static bool LooksLikeLocalModelPath(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (Directory.Exists(value) || File.Exists(value) || value.Contains('\\') || value.Contains('/'));

    private static void RenderShow(ConnectShowResult result)
    {
        if (!result.Active || result.Connection is null)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        RenderConnection(result.Connection);
    }

    private static void RenderConnection(CliConnectionState connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.Profile))
            Console.WriteLine($"profile:  {connection.Profile}");
        if (!string.IsNullOrWhiteSpace(connection.Model))
            Console.WriteLine($"model:    {connection.Model}");
        if (!string.IsNullOrWhiteSpace(connection.Server))
            Console.WriteLine($"server:   {connection.Server}");
        if (!string.IsNullOrWhiteSpace(connection.Database))
            Console.WriteLine($"database: {connection.Database}");
    }
}
