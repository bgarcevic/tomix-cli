namespace Mdl.Cli.Tests.Compatibility;

public sealed class CompatibilityHelpTests
{
    private static readonly string[] ExpectedCommands =
    [
        "add",
        "auth",
        "bpa",
        "completion",
        "config",
        "connect",
        "deploy",
        "deps",
        "diff",
        "find",
        "format",
        "get",
        "incremental-refresh",
        "init",
        "interactive",
        "load",
        "ls",
        "macro",
        "migrate",
        "mv",
        "open",
        "profile",
        "query",
        "refresh",
        "replace",
        "rm",
        "save",
        "script",
        "session",
        "set",
        "test",
        "validate",
        "vertipaq"
    ];

    public static TheoryData<string> Commands()
    {
        var data = new TheoryData<string>();
        foreach (var command in ExpectedCommands)
            data.Add(command);
        return data;
    }

    public static TheoryData<string> ImplementedReadOnlyCommands()
    {
        var data = new TheoryData<string>();
        foreach (var command in ExpectedCommands)
            data.Add(command);
        return data;
    }

    public static TheoryData<string, string> ReferenceSubcommands()
    {
        var data = new TheoryData<string, string>();
        foreach (var (command, children) in new (string Command, string[] Children)[]
                 {
                     ("auth", ["login", "logout", "status"]),
                     ("bpa", ["rules", "run"]),
                     ("config", ["init", "paths", "set", "show"]),
                     ("incremental-refresh", ["apply", "rm", "set", "show"]),
                     ("macro", ["add", "init", "list", "rm", "run", "set", "sort"]),
                     ("profile", ["list", "remove", "set", "show"]),
                     ("session", ["clear", "list", "prune", "show"]),
                     ("test", ["compare", "init", "list", "run", "snapshot", "spec", "use"])
                 })
        {
            foreach (var child in children)
                data.Add(command, child);
        }

        return data;
    }

    [Fact]
    public void RootHelp_ExposesSameCommandNamesAsReference()
    {
        var reference = CliProcess.RunReference("--help");
        var mdl = CliProcess.RunMdl("--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.RootCommandNames(reference.StdOut),
            CompatibilityText.RootCommandNames(mdl.StdOut));
    }

    [Fact]
    public void RootHelp_ContainsReferenceGlobalLongOptions()
    {
        var reference = CompatibilityText.LongOptions(CliProcess.RunReference("--help").StdOut);
        var mdl = CompatibilityText.LongOptions(CliProcess.RunMdl("--help").StdOut);

        foreach (var option in reference.Where(option => option != "--help"))
            Assert.Contains(option, mdl);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandHelp_ExistsAndExitsSuccessfully(string command)
    {
        var reference = CliProcess.RunReference(command, "--help");
        var mdl = CliProcess.RunMdl(command, "--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Contains(command, mdl.StdOut);
    }

    private static readonly IReadOnlySet<string> NotYetImplementedOptions = new HashSet<string>
    {
        "--no-antipatterns"
    };

    [Theory]
    [MemberData(nameof(ImplementedReadOnlyCommands))]
    public void CommandHelp_ContainsReferenceCommandSpecificOptions(string command)
    {
        var referenceOptions = CompatibilityText.CommandSpecificLongOptions(
            CliProcess.RunReference(command, "--help").StdOut);
        var mdlOptions = CompatibilityText.LongOptions(CliProcess.RunMdl(command, "--help").StdOut);

        foreach (var option in referenceOptions.Except(NotYetImplementedOptions))
            Assert.Contains(option, mdlOptions);
    }

    [Theory]
    [MemberData(nameof(ReferenceSubcommands))]
    public void SubcommandHelp_ExistsAndExitsSuccessfully(string command, string child)
    {
        var reference = CliProcess.RunReference(command, child, "--help");
        var mdl = CliProcess.RunMdl(command, child, "--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Contains(child, mdl.StdOut);
    }
}
