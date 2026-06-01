using System.Collections.Concurrent;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class CompatibilityHelpTests
{
    private static readonly ConcurrentDictionary<string, CliRun> ReferenceHelpCache = new();
    private static readonly ConcurrentDictionary<string, CliRun> MdlHelpCache = new();

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
        var reference = ReferenceHelp("--help");
        var mdl = MdlHelp("--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Equal(
            CompatibilityText.RootCommandNames(reference.StdOut),
            CompatibilityText.RootCommandNames(mdl.StdOut));
    }

    [Fact]
    public void RootHelp_ContainsReferenceGlobalLongOptions()
    {
        var reference = CompatibilityText.LongOptions(ReferenceHelp("--help").StdOut);
        var mdl = CompatibilityText.LongOptions(MdlHelp("--help").StdOut);

        foreach (var option in reference.Where(option => option != "--help"))
            Assert.Contains(option, mdl);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandHelp_ExistsAndExitsSuccessfully(string command)
    {
        var reference = ReferenceHelp(command, "--help");
        var mdl = MdlHelp(command, "--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Contains(command, mdl.StdOut);
    }

    private static readonly IReadOnlySet<string> NotYetImplementedOptions = new HashSet<string>
    {
        "--no-antipatterns"
    };

    private static readonly IReadOnlySet<string> CoveredByGlobalOptions = new HashSet<string>
    {
        "--auth",
        "--database",
        "--local",
        "--model",
        "--server",
        "-d",
        "-m",
        "-s"
    };

    [Theory]
    [MemberData(nameof(ImplementedReadOnlyCommands))]
    public void CommandHelp_ContainsReferenceCommandSpecificOptions(string command)
    {
        var referenceOptions = CompatibilityText.CommandSpecificLongOptions(
            ReferenceHelp(command, "--help").StdOut);
        var mdlOptions = CompatibilityText.LongOptions(MdlHelp(command, "--help").StdOut);

        foreach (var option in referenceOptions.Except(NotYetImplementedOptions))
            Assert.Contains(option, mdlOptions);
    }

    [Theory]
    [MemberData(nameof(ImplementedReadOnlyCommands))]
    public void CommandHelp_ContainsReferenceCommandSpecificOptionTokens(string command)
    {
        var referenceTokens = CompatibilityText.CommandSpecificOptionTokens(
            ReferenceHelp(command, "--help").StdOut);
        var mdlHelp = MdlHelp(command, "--help").StdOut;
        var mdlTokens = CompatibilityText.LongOptions(mdlHelp)
            .Concat(CompatibilityText.CommandSpecificOptionTokens(mdlHelp))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var option in referenceTokens.Except(NotYetImplementedOptions).Except(CoveredByGlobalOptions))
            Assert.Contains(option, mdlTokens);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandHelp_ContainsReferenceArguments(string command)
    {
        Assert.Equal(
            CompatibilityText.ArgumentNames(ReferenceHelp(command, "--help").StdOut),
            CompatibilityText.ArgumentNames(MdlHelp(command, "--help").StdOut));
    }

    [Fact]
    public void ConnectHelp_ContainsReferenceOptionAliases()
    {
        var reference = ReferenceHelp("connect", "--help");
        var mdl = MdlHelp("connect", "--help");

        Assert.Contains("-w, --workspace", reference.StdOut);
        Assert.Contains("-w, --workspace", mdl.StdOut);
        Assert.Contains("-p, --profile", reference.StdOut);
        Assert.Contains("-p, --profile", mdl.StdOut);
    }

    [Theory]
    [MemberData(nameof(ReferenceSubcommands))]
    public void SubcommandHelp_ExistsAndExitsSuccessfully(string command, string child)
    {
        var reference = ReferenceHelp(command, child, "--help");
        var mdl = MdlHelp(command, child, "--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Contains(child, mdl.StdOut);
    }

    [Theory]
    [MemberData(nameof(ReferenceSubcommands))]
    public void SubcommandHelp_ContainsReferenceCommandSpecificOptionTokens(string command, string child)
    {
        var referenceTokens = CompatibilityText.CommandSpecificOptionTokens(
            ReferenceHelp(command, child, "--help").StdOut);
        var mdlHelp = MdlHelp(command, child, "--help").StdOut;
        var mdlTokens = CompatibilityText.LongOptions(mdlHelp)
            .Concat(CompatibilityText.CommandSpecificOptionTokens(mdlHelp))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var option in referenceTokens.Except(NotYetImplementedOptions).Except(CoveredByGlobalOptions))
            Assert.Contains(option, mdlTokens);
    }

    [Theory]
    [MemberData(nameof(ReferenceSubcommands))]
    public void SubcommandHelp_ContainsReferenceArguments(string command, string child)
    {
        Assert.Equal(
            CompatibilityText.ArgumentNames(ReferenceHelp(command, child, "--help").StdOut),
            CompatibilityText.ArgumentNames(MdlHelp(command, child, "--help").StdOut));
    }

    private static CliRun ReferenceHelp(params string[] args)
        => ReferenceHelpCache.GetOrAdd(string.Join('\u001f', args), _ => CliProcess.RunReference(args));

    private static CliRun MdlHelp(params string[] args)
        => MdlHelpCache.GetOrAdd(string.Join('\u001f', args), _ => CliProcess.RunMdl(args));
}
