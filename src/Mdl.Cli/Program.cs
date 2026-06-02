using System.CommandLine;
using System.Reflection;
using Mdl.App.Format;
using Mdl.Cli.Commands;
using Mdl.Core.Models;
using Mdl.Provider.Tom;
using Mdl.Provider.Tmdl;

namespace Mdl.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var root = new RootCommand("mdl - CLI for semantic models");
        foreach (var option in GlobalOptions.All())
            root.Options.Add(option);

        var tokenProvider = AuthSettingsFactory.CreateAuthenticator();
        IReadOnlyList<IModelProvider> providers =
            [new TmdlModelProvider(tokenProvider), new TomFileModelProvider(tokenProvider), new TomServerModelProvider(tokenProvider)];
        var formatter = new CompositeExpressionFormatterClient(
            [
                new DaxFormatterApiClient(),
                new PowerQueryFormatterApiClient(new HttpClient())
            ]);
        var stubs = CompatibilityStubCommand.All().ToDictionary(command => command.Name);

        var modules = new ICommandModule[]
        {
            new AddCommand(providers),
            new AuthCommand(),
            new BpaCommand(providers),
            new CompletionCommand(() => root.Subcommands.Select(command => command.Name).ToList()),
            new ConfigCommand(),
            new ConnectCommand(providers),
            new DeployCommand(providers),
            new DepsCommand(providers),
            new DiffCommand(providers),
            new FindCommand(providers),
            new FormatCommand(providers, formatter),
            new GetCommand(providers),
            stubs["incremental-refresh"],
            new InitCommand(),
            stubs["interactive"],
            new LoadCommand(providers),
            new LsCommand(providers),
            new MacroCommand(),
            new MvCommand(providers),
            new ProfileCommand(),
            stubs["query"],
            stubs["refresh"],
            new ReplaceCommand(providers),
            new RmCommand(providers),
            new SaveCommand(providers),
            new ScriptCommand(providers),
            new SessionCommand(),
            new SetCommand(providers),
            stubs["test"],
            new ValidateCommand(providers),
            stubs["vertipaq"]
        };

        foreach (var module in modules)
            root.Subcommands.Add(module.Build());

        if (RootHelpRenderer.IsRootHelpRequest(args))
        {
            RootHelpRenderer.Write(root, Console.Out);
            return 0;
        }

        if (RootHelpRenderer.IsRootInvocation(args))
        {
            RootHelpRenderer.Write(root, Console.Out);
            Console.Error.WriteLine("Required command was not provided.");
            return 0;
        }

        return root.Parse(args).Invoke();
    }

    private static string ResolveVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return "0.0.0";

        // Strip any "+<commit>" SourceLink suffix so doctor reports a clean version.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
