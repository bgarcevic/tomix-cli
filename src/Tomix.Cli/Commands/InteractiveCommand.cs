using System.CommandLine;
using Tomix.App.Models;
using Tomix.App.State;
using Tomix.Cli.Interactive;
using Tomix.Cli.Output;
using Tomix.Core.Diagnostics;
using Tomix.Core.Models;

namespace Tomix.Cli.Commands;

/// <summary>
/// <c>tx interactive</c> (alias <c>tx shell</c>): opens a live session on one model and runs
/// commands against it until <c>exit</c> or the end of input (ADR 0001).
/// </summary>
internal sealed class InteractiveCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly CliStateStore _state;
    private readonly StagingStore _staging;
    private readonly Func<SessionScope, IEnumerable<Command>, RootCommand> _buildSessionRoot;

    /// <param name="buildSessionRoot">Builds the command tree a session runs, around its session-only commands.</param>
    public InteractiveCommand(
        IReadOnlyList<IModelProvider> providers,
        CliStateStore state,
        StagingStore staging,
        Func<SessionScope, IEnumerable<Command>, RootCommand> buildSessionRoot)
    {
        _providers = providers;
        _state = state;
        _staging = staging;
        _buildSessionRoot = buildSessionRoot;
    }

    public Command Build()
    {
        var modelArgument = new Argument<string>("model")
        {
            Description = "Model to open: a TMDL folder or .bim file; defaults to the active connection",
            Arity = ArgumentArity.ZeroOrOne
        };
        var autosaveOption = new Option<bool>("--autosave")
        {
            Description = "Save after every command that changes the model, instead of only when you run 'save'"
        };
        var discardOption = new Option<bool>("--discard-on-exit")
        {
            Description = "Leave without asking, discarding unsaved changes (so does --yes)"
        };
        var echoOption = new Option<bool>("--echo")
        {
            Description = "Print each command before it runs, so a log of a scripted session reads in order"
        };
        var noBatchOption = new Option<bool>("--no-batch")
        {
            Description = "Keep running a script after a command fails (by default piped input stops at the first failure)"
        };
        var noBannerOption = new Option<bool>("--no-banner")
        {
            Description = "Start without the welcome lines"
        };

        var command = new Command("interactive", "Edit a model in a session with undo, then save")
        {
            modelArgument,
            autosaveOption,
            discardOption,
            echoOption,
            noBatchOption,
            noBannerOption
        };
        command.Aliases.Add("shell");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            var errorFormat = GlobalOptions.ErrorFormatValue(parseResult, format);
            if (!CommandOutput.TryValidateFormat(parseResult, format, "interactive", OutputFormats.Text, OutputFormats.Json))
                return 2;

            if (!RecentConnections.TryResolveModel(
                    parseResult,
                    GlobalOptions.ModelValue(parseResult) ?? parseResult.GetValue(modelArgument),
                    _state,
                    out var reference,
                    out var recentExit))
                return recentExit;

            if (_staging.TryLoad(reference) is not null)
                return Fail(
                    errorFormat,
                    "TOMIX_STAGE_PENDING",
                    $"{reference.Value} has staged changes; a session does not pick them up.",
                    "Run 'tx stage commit' or 'tx stage discard' first.");

            var provider = _providers.ResolveSingleProvider(reference);
            if (provider is null)
                return Fail(
                    errorFormat,
                    "TOMIX_NO_PROVIDER",
                    $"No provider can open model: {reference.Value}",
                    "Pass a TMDL folder or a .bim file.");
            if (provider is not ILiveModelProvider live)
                return Fail(
                    errorFormat,
                    "TOMIX_SESSION_SOURCE_UNSUPPORTED",
                    $"An interactive session cannot open {reference.Value} yet; it opens TMDL folders and .bim files.",
                    "Save the model locally with 'tx save -o <folder>' and open that.");

            var quiet = parseResult.GetValue(GlobalOptions.Quiet);
            var session = await CliSpinner.RunAsync(
                "Loading model...",
                () => live.OpenLiveAsync(reference, cancellationToken),
                suppress: quiet || OutputFormats.IsJson(format));
            await using (session)
            {
                var testInput = InputValueResolver.TestStdin.Value;
                var terminal = testInput is null && !Console.IsInputRedirected;
                var canPrompt = terminal && InteractionGate.CanPrompt(parseResult, format);
                var scope = new SessionScope(new LiveSessionSource(session, new LiveLeaseOptions(InteractiveLoop.Client)));
                var oneShot = parseResult.RootCommandResult.Command.Subcommands
                    .SelectMany(sub => sub.Aliases.Prepend(sub.Name))
                    .ToHashSet(StringComparer.Ordinal);
                var loop = new InteractiveLoop(
                    scope,
                    sessionCommands => _buildSessionRoot(scope, sessionCommands),
                    oneShot,
                    new InteractiveOptions(
                        Autosave: parseResult.GetValue(autosaveOption),
                        DiscardOnExit: parseResult.GetValue(discardOption) || parseResult.GetValue(GlobalOptions.Yes),
                        Echo: parseResult.GetValue(echoOption),
                        Batch: !parseResult.GetValue(noBatchOption),
                        Banner: terminal && !quiet && !parseResult.GetValue(noBannerOption) && !OutputFormats.IsJson(format),
                        CanPrompt: canPrompt,
                        errorFormat,
                        InheritedOptions(parseResult)));

                ILineReader reader = terminal ? new ConsoleLineEditor(loop.Complete) : new RedirectedLineReader(testInput ?? Console.In);
                return await loop.RunAsync(reader, cancellationToken);
            }
        });

        return command;
    }

    /// <summary>
    /// The global options given to <c>tx interactive</c> itself, as tokens every line inherits:
    /// <c>tx interactive --output-format json</c> makes JSON each command's default.
    /// </summary>
    internal static IReadOnlyList<(Option Option, string[] Tokens)> InheritedOptions(ParseResult parseResult)
    {
        var inherited = new List<(Option, string[])>();
        foreach (var option in new Option[] { GlobalOptions.OutputFormat, GlobalOptions.ErrorFormat, GlobalOptions.Auth })
            if (parseResult.GetResult(option) is { Implicit: false } result && result.Tokens.Count > 0)
                inherited.Add((option, [option.Name, result.Tokens[0].Value]));
        foreach (var flag in new[] { GlobalOptions.Quiet, GlobalOptions.Debug, GlobalOptions.Yes, GlobalOptions.NonInteractive })
            if (parseResult.GetValue(flag))
                inherited.Add((flag, [flag.Name]));
        return inherited;
    }

    private static int Fail(string? errorFormat, string code, string message, string hint)
    {
        ErrorOutput.Write([new TomixDiagnostic(code, DiagnosticSeverity.Error, message, hint)], errorFormat);
        return 2;
    }
}
