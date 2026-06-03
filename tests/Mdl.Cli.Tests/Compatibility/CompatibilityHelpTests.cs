using System.Collections.Concurrent;

namespace Mdl.Cli.Tests.Compatibility;

public sealed class CompatibilityHelpTests
{
    private static readonly ConcurrentDictionary<string, CliRun> ReferenceHelpCache = new();
    private static readonly ConcurrentDictionary<string, CliRun> MdlHelpCache = new();

    private static readonly IReadOnlySet<string> SkippedCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "migrate",
        "open"
    };

    // mdl-specific commands that have no te.exe counterpart and so are filtered out of the mdl side
    // before comparing command lists against the reference.
    private static readonly IReadOnlySet<string> MdlOnlyCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "stage"
    };

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
        "mv",
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

    public static TheoryData<string> ParentCommandsWithSubcommands()
    {
        var data = new TheoryData<string>();
        foreach (var command in new[]
                 {
                     "auth",
                     "bpa",
                     "config",
                     "incremental-refresh",
                     "macro",
                     "profile",
                     "session",
                     "test"
                 })
        {
            data.Add(command);
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
            WithoutSkippedCommandNames(CompatibilityText.RootCommandNames(reference.StdOut)),
            WithoutMdlOnlyCommandNames(CompatibilityText.RootCommandNames(mdl.StdOut)));
        AssertSkippedCommandsAreHidden(mdl.StdOut);
    }

    [Fact]
    public void RootHelp_ExposesSameCommandUsageLabelsAsReference()
    {
        var reference = ReferenceHelp("--help");
        var mdl = MdlHelp("--help");

        Assert.Equal(0, reference.ExitCode);
        Assert.Equal(0, mdl.ExitCode);
        Assert.Equal(
            WithoutSkippedUsageLabels(CompatibilityText.RootCommandUsageLabels(reference.StdOut)),
            WithoutMdlOnlyUsageLabels(CompatibilityText.RootCommandUsageLabels(mdl.StdOut)));
        AssertSkippedCommandsAreHidden(mdl.StdOut);
    }

    [Fact]
    public void RootHelp_UsesMdlExecutableName()
    {
        var mdl = MdlHelp("--help");

        Assert.Equal(0, mdl.ExitCode);
        Assert.Contains("Usage:\n  mdl [command] [options]", mdl.StdOut);
        Assert.DoesNotContain("Mdl.Cli", mdl.StdOut);
    }

    [Fact]
    public void RootHelp_UsesDistinctMdlStyling()
    {
        var mdl = MdlHelp("--help");

        Assert.Equal(0, mdl.ExitCode);
        Assert.StartsWith("mdl\n  Semantic model command line", mdl.StdOut);
        Assert.Contains("Global options:", mdl.StdOut);
        Assert.DoesNotContain("  mdl -", mdl.StdOut);
        Assert.Contains("Use `mdl <command> --help` for command-specific options.", mdl.StdOut);
    }

    [Fact]
    public void RootInvocationWithoutArguments_MatchesReferenceExitAndCommandUsageLabels()
    {
        var reference = ReferenceHelp();
        var mdl = MdlHelp();

        Assert.Equal(reference.ExitCode, mdl.ExitCode);
        Assert.Equal(
            WithoutSkippedUsageLabels(CompatibilityText.RootCommandUsageLabels(reference.StdOut)),
            WithoutMdlOnlyUsageLabels(CompatibilityText.RootCommandUsageLabels(mdl.StdOut)));
    }

    [Fact]
    public void RootHelp_ContainsReferenceGlobalLongOptions()
    {
        var reference = CompatibilityText.LongOptions(ReferenceHelp("--help").StdOut);
        var mdl = CompatibilityText.LongOptions(MdlHelp("--help").StdOut);

        foreach (var option in reference.Where(option => option != "--help"))
            Assert.Contains(option, mdl);
    }

    [Fact]
    public void RootHelp_SlashHelpAliasesMatchReferenceBehavior()
    {
        foreach (var option in new[] { "/h", "/?" })
        {
            var reference = ReferenceHelp(option);
            var mdl = MdlHelp(option);

            Assert.Equal(0, reference.ExitCode);
            Assert.Equal(0, mdl.ExitCode);
            Assert.Equal(
                WithoutSkippedCommandNames(CompatibilityText.RootCommandNames(reference.StdOut)),
                WithoutMdlOnlyCommandNames(CompatibilityText.RootCommandNames(mdl.StdOut)));
            AssertSkippedCommandsAreHidden(mdl.StdOut);
        }
    }

    [Theory]
    [MemberData(nameof(ParentCommandsWithSubcommands))]
    public void ParentCommandHelp_ExposesSameSubcommandNamesAsReference(string command)
    {
        Assert.Equal(
            CompatibilityText.CommandNames(ReferenceHelp(command, "--help").StdOut),
            CompatibilityText.CommandNames(MdlHelp(command, "--help").StdOut));
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
        "--no-antipatterns",
        "--save"
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

    private static IReadOnlyList<string> WithoutSkippedCommandNames(IReadOnlyList<string> commandNames)
        => commandNames.Where(command => !SkippedCommands.Contains(command)).ToArray();

    private static IReadOnlyList<string> WithoutMdlOnlyCommandNames(IReadOnlyList<string> commandNames)
        => commandNames.Where(command => !MdlOnlyCommands.Contains(command)).ToArray();

    private static IReadOnlyList<string> WithoutMdlOnlyUsageLabels(IReadOnlyList<string> usageLabels)
        => usageLabels.Where(label => !MdlOnlyCommands.Contains(label.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])).ToArray();

    private static IReadOnlyList<string> WithoutSkippedUsageLabels(IReadOnlyList<string> usageLabels)
        => usageLabels.Where(label => !SkippedCommands.Contains(label.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])).ToArray();

    private static void AssertSkippedCommandsAreHidden(string helpText)
    {
        var commandNames = CompatibilityText.RootCommandNames(helpText);
        foreach (var command in SkippedCommands)
            Assert.DoesNotContain(command, commandNames);
    }
}
