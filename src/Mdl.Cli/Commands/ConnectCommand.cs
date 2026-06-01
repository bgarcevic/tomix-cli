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
            Description = "Workspace name, endpoint, local model path (TMDL/BIM), or omit with --local.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var databaseArgument = new Argument<string>("database")
        {
            Description = "Semantic model name (omit to list all models on workspace)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var workspaceOption = new Option<string?>("--workspace")
        {
            Description = "Enable workspace mode: mirror saves between the primary source and a secondary target. Primary remote -> pass a local folder or .bim file. Primary local -> pass <server> <database>."
        };
        workspaceOption.Aliases.Add("-w");
        var profileOption = new Option<string?>("--profile")
        {
            Description = "Activate a saved connection profile (see: mdl profile list)"
        };
        profileOption.Aliases.Add("-p");
        var clearOption = new Option<bool>("--clear")
        {
            Description = "Clear the active connection from CLI config"
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite a non-empty workspace target when initializing workspace mode."
        };
        var workspaceFormatOption = new Option<string?>("--workspace-format")
        {
            Description = "On-disk format for a local workspace (tmdl, bim, te-folder). Defaults to path-detected."
        };
        var workspaceAuthOption = new Option<string?>("--workspace-auth")
        {
            Description = "Auth method for a remote workspace (local primary). Defaults to --auth if set, else auto."
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

            var server = parseResult.GetValue(serverArgument);
            var database = parseResult.GetValue(databaseArgument);
            var profile = parseResult.GetValue(profileOption);
            var workspace = parseResult.GetValue(workspaceOption);
            var workspaceFormat = parseResult.GetValue(workspaceFormatOption);
            var workspaceAuth = parseResult.GetValue(workspaceAuthOption);
            var auth = GlobalOptions.AuthValue(parseResult);
            var local = parseResult.GetValue(GlobalOptions.Local);

            if (local &&
                string.IsNullOrWhiteSpace(database) &&
                !LooksLikeLocalModelPath(server) &&
                !ModelReference.IsRemoteEndpoint(server))
            {
                database = server;
                server = null;
            }

            if (!string.IsNullOrWhiteSpace(workspace))
            {
                if (local)
                    return RenderWorkspaceOptionError("--workspace is not supported with --local (PBI Desktop).");

                if (!string.IsNullOrWhiteSpace(profile))
                    return RenderWorkspaceOptionError("--workspace cannot be combined with --profile. Activate the profile first, then set up workspace mode separately.");

                if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(database))
                    return RenderWorkspaceOptionError("--workspace requires an explicit primary source (server+database or local path).");

                if (!string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(database))
                {
                    var message = LooksLikeLocalModelPath(server)
                        ? "--workspace requires <server> <database> (two values) when the primary is a local path."
                        : "--workspace requires both <server> and <database> for the primary connection.";
                    return RenderWorkspaceOptionError(message);
                }

                if (string.IsNullOrWhiteSpace(workspaceAuth))
                    workspaceAuth = string.IsNullOrWhiteSpace(auth) ? "auto" : auth;
            }
            else
            {
                workspaceFormat = null;
                workspaceAuth = null;
            }

            if (string.IsNullOrWhiteSpace(server) &&
                string.IsNullOrWhiteSpace(database) &&
                string.IsNullOrWhiteSpace(profile) &&
                string.IsNullOrWhiteSpace(workspace) &&
                !local)
            {
                return CommandOutput.Render(handler.Show(), format, RenderShow);
            }

            var isRemoteEndpoint = ModelReference.IsRemoteEndpoint(server);
            var model = (!isRemoteEndpoint && LooksLikeLocalModelPath(server)) ? server : null;
            var remoteServer = model is null ? server : null;

            if (local && model is null && !ModelReference.IsLocalInstanceEndpoint(remoteServer))
            {
                Console.WriteLine("Discovering Power BI Desktop instances...");
                var endpoints = DiscoverPowerBiDesktopEndpoints();
                if (endpoints.Count == 0)
                {
                    Console.Error.WriteLine("Error: No running Power BI Desktop instances found. Start Power BI Desktop and open a report, then retry.");
                    return 1;
                }

                if (endpoints.Count > 1 && string.IsNullOrWhiteSpace(database))
                {
                    Console.Error.WriteLine("Error: Multiple Power BI Desktop instances found. Specify a semantic model name.");
                    return 1;
                }

                remoteServer = endpoints[0];
                isRemoteEndpoint = ModelReference.IsRemoteEndpoint(remoteServer);
            }

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

                if (model is not null &&
                    !string.IsNullOrWhiteSpace(workspace) &&
                    !string.IsNullOrWhiteSpace(database) &&
                    ModelReference.IsRemoteEndpoint(workspace))
                {
                    var workspaceResult = await infoHandler.HandleAsync(
                        new InfoModelRequest(ModelReference.Remote(workspace, database)),
                        cancellationToken);

                    if (!workspaceResult.Success)
                    {
                        RenderConnectedModel(infoResult.Data!, format, model, remoteServer, database);
                        foreach (var diag in workspaceResult.Diagnostics)
                            Console.Error.WriteLine($"Error: Could not reach workspace server: {WorkspaceConnectMessage(workspace, diag.Message)}");
                        return workspaceResult.ExitCode == 0 ? 1 : workspaceResult.ExitCode;
                    }
                }

                handler.Set(new ConnectSetRequest(
                    remoteServer,
                    database,
                    model,
                    auth,
                    Local: model is not null || local,
                    profile,
                    workspace,
                    workspaceFormat,
                    workspaceAuth));

                if (format != OutputFormats.Text)
                    return CommandOutput.Render(
                        infoResult,
                        format,
                        _ => { },
                        data => ProjectConnectedModelJson(data, model, remoteServer, database));

                RenderConnectedModelText(infoResult.Data!, model, remoteServer, database);
                return 0;
            }

            var setResult = handler.Set(new ConnectSetRequest(
                remoteServer,
                database,
                model,
                auth,
                local,
                profile,
                workspace,
                workspaceFormat,
                workspaceAuth));

            if (!setResult.Success)
                return CommandOutput.Render(setResult, format, _ => { });

            if (format != OutputFormats.Text)
                return CommandOutput.Render(setResult, format, _ => { });

            RenderConnection(setResult.Data!.Connection);
            return setResult.ExitCode;
        });

        return command;
    }

    private static int RenderWorkspaceOptionError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    private static bool LooksLikeLocalModelPath(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (Directory.Exists(value) || File.Exists(value) || value.Contains('\\') || value.Contains('/'));

    private static string WorkspaceConnectMessage(string workspace, string message)
    {
        var prefix = $"Could not connect to '{workspace}': ";
        return message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? message[prefix.Length..]
            : message;
    }

    private static void RenderConnectedModel(
        InfoModelResult result,
        string format,
        string? model,
        string? remoteServer,
        string? database)
    {
        if (OutputFormats.IsJson(format))
            JsonOutput.Write(ProjectConnectedModelJson(result, model, remoteServer, database));
        else
            RenderConnectedModelText(result, model, remoteServer, database);
    }

    private static void RenderConnectedModelText(
        InfoModelResult result,
        string? model,
        string? remoteServer,
        string? database)
    {
        var s = result.Summary;
        var name = string.IsNullOrWhiteSpace(s.Name) ? "(unnamed)" : s.Name;
        Console.WriteLine($"Model: {name}");
        Console.WriteLine($"  CL: {s.CompatibilityLevel}");
        Console.WriteLine($"  tables: {s.Tables}  measures: {s.Measures}  relationships: {s.Relationships}  roles: {s.Roles}");
        Console.WriteLine();
        Console.WriteLine(model is not null
            ? $"Active: {Path.GetFullPath(model)}"
            : $"Active: {remoteServer}{(string.IsNullOrWhiteSpace(database) ? "" : $" / {database}")}");
    }

    private static IReadOnlyList<string> DiscoverPowerBiDesktopEndpoints()
    {
        var roots = PowerBiDesktopWorkspaceRoots()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var endpoints = new List<string>();

        foreach (var root in roots)
        {
            foreach (var portFile in Directory.EnumerateFiles(root, "msmdsrv.port.txt", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(portFile).Trim();
                if (int.TryParse(text, out var port) && port > 0)
                    endpoints.Add($"localhost:{port}");
            }
        }

        return endpoints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> PowerBiDesktopWorkspaceRoots()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Microsoft", "Power BI Desktop", "AnalysisServicesWorkspaces");
            yield return Path.Combine(localAppData, "Packages", "Microsoft.MicrosoftPowerBIDesktop_8wekyb3d8bbwe", "LocalCache", "Microsoft", "Power BI Desktop", "AnalysisServicesWorkspaces");
        }
    }

    private static object ProjectConnectedModelJson(
        InfoModelResult result,
        string? model,
        string? remoteServer,
        string? database)
    {
        var summary = result.Summary;
        var shared = new
        {
            compatibilityLevel = summary.CompatibilityLevel,
            tables = summary.Tables,
            measures = summary.Measures,
            relationships = summary.Relationships,
            roles = summary.Roles
        };

        if (!string.IsNullOrWhiteSpace(model))
        {
            return new
            {
                kind = "local",
                path = Path.GetFullPath(model),
                shared.compatibilityLevel,
                shared.tables,
                shared.measures,
                shared.relationships,
                shared.roles
            };
        }

        return new
        {
            kind = ModelReference.IsLocalInstanceEndpoint(remoteServer) ? "local" : "remote",
            server = remoteServer,
            database = string.IsNullOrWhiteSpace(database) ? summary.DatabaseName : database,
            shared.compatibilityLevel,
            shared.tables,
            shared.measures,
            shared.relationships,
            shared.roles
        };
    }

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
        if (!string.IsNullOrWhiteSpace(connection.Workspace))
            Console.WriteLine($"workspace: {connection.Workspace}");
        if (!string.IsNullOrWhiteSpace(connection.WorkspaceFormat))
            Console.WriteLine($"workspace-format: {connection.WorkspaceFormat}");
        if (!string.IsNullOrWhiteSpace(connection.WorkspaceAuth))
            Console.WriteLine($"workspace-auth: {connection.WorkspaceAuth}");
    }
}
