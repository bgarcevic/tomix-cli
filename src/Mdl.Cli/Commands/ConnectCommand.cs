using System.CommandLine;
using Mdl.App.Connect;
using Mdl.App.Info;
using Mdl.App.State;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal sealed class ConnectCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public ConnectCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

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

        command.SetAction(async (parseResult, cancellationToken) =>
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

            var isRemoteEndpoint = ModelReference.IsRemoteEndpoint(server);
            var model = !string.IsNullOrWhiteSpace(globalModel)
                ? globalModel
                : (!isRemoteEndpoint && LooksLikeLocalModelPath(server)) ? server : null;
            var remoteServer = model is null ? server : null;
            var auth = GlobalOptions.AuthValue(parseResult);
            var local = parseResult.GetValue(GlobalOptions.Local);

            // Validate by opening before storing: local model paths always, and remote XMLA
            // endpoints when a dataset is given (so we open that specific catalog, not the whole
            // workspace). A bare workspace name or a remote endpoint without a dataset is stored
            // as-is without opening.
            ModelReference? validation = null;
            if (string.IsNullOrWhiteSpace(profile))
            {
                if (!string.IsNullOrWhiteSpace(model))
                    validation = new ModelReference(model);
                else if (isRemoteEndpoint && !string.IsNullOrWhiteSpace(database))
                    validation = ModelReference.Remote(remoteServer!, database);
            }

            if (validation is not null)
            {
                var infoHandler = new InfoModelHandler(_providers);
                var infoResult = await infoHandler.HandleAsync(
                    new InfoModelRequest(validation),
                    cancellationToken);

                if (!infoResult.Success)
                {
                    foreach (var diag in infoResult.Diagnostics)
                        Console.Error.WriteLine($"Error: {diag.Message}");
                    return infoResult.ExitCode == 0 ? 1 : infoResult.ExitCode;
                }

                handler.Set(new ConnectSetRequest(remoteServer, database, model, auth, Local: model is not null, profile));

                if (format != OutputFormats.Text)
                    return CommandOutput.Render(infoResult, format, _ => { });

                var s = infoResult.Data!.Summary;
                var name = string.IsNullOrWhiteSpace(s.Name) ? "(unnamed)" : s.Name;
                Console.WriteLine($"Model: {name}");
                Console.WriteLine($"  CL: {s.CompatibilityLevel}");
                Console.WriteLine($"  tables: {s.Tables}  measures: {s.Measures}  relationships: {s.Relationships}  roles: {s.Roles}");
                Console.WriteLine();
                Console.WriteLine(model is not null
                    ? $"Active: {Path.GetFullPath(model)}"
                    : $"Active: {remoteServer}{(string.IsNullOrWhiteSpace(database) ? "" : $" / {database}")}");
                return 0;
            }

            var setResult = handler.Set(new ConnectSetRequest(remoteServer, database, model, auth, local, profile));

            if (format != OutputFormats.Text)
                return CommandOutput.Render(setResult, format, _ => { });

            RenderConnection(setResult.Data!.Connection);
            return setResult.ExitCode;
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

        var connection = result.Connection;
        if (!string.IsNullOrWhiteSpace(connection.Model))
        {
            Console.WriteLine($"Active: {connection.Model}");
            return;
        }

        RenderConnection(connection);
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
