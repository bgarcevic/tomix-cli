using System.CommandLine;
using System.CommandLine.Parsing;
using Spectre.Console;
using Tomix.App.Session;
using Tomix.Cli.Commands;
using Tomix.Cli.Output;
using Tomix.Core.Diagnostics;
using Tomix.Core.Models;

namespace Tomix.Cli.Interactive;

/// <param name="Autosave">Save after every command that leaves the session with unsaved changes.</param>
/// <param name="DiscardOnExit">Leave without asking, discarding unsaved changes.</param>
/// <param name="Echo">Print each line before it runs.</param>
/// <param name="Batch">Stop at the first failing command. Applies only when input is not a terminal.</param>
/// <param name="Banner">Show the welcome lines. Applies only at a terminal.</param>
/// <param name="CanPrompt">Whether a person is at the keyboard to answer prompts.</param>
/// <param name="ErrorFormat">The session's <c>--error-format</c>, for its own messages.</param>
/// <param name="Inherited">Global options given to the session, added to every line that does not set them.</param>
internal sealed record InteractiveOptions(
    bool Autosave,
    bool DiscardOnExit,
    bool Echo,
    bool Batch,
    bool Banner,
    bool CanPrompt,
    string? ErrorFormat,
    IReadOnlyList<(Option Option, string[] Tokens)> Inherited);

/// <summary>
/// The read-run loop of <c>tx interactive</c>. Each line is parsed through the session's command
/// tree and runs as one transaction on the live session; the session-only commands
/// (<see cref="SessionCommands"/>) act on the session itself.
/// </summary>
internal sealed class InteractiveLoop
{
    /// <summary>The client name the session's change events and history report.</summary>
    public const string Client = "shell";

    private readonly SessionScope _scope;
    private readonly LiveSessionHandler _handler;
    private readonly RootCommand _root;
    private readonly IReadOnlySet<string> _oneShotCommands;
    private readonly InteractiveOptions _options;
    private CancellationTokenSource? _running;
    private bool _exitRequested;

    /// <param name="buildRoot">Builds the session's command tree around the session-only commands.</param>
    /// <param name="oneShotCommands">Every top-level command of <c>tx</c>, to explain the ones a session lacks.</param>
    public InteractiveLoop(
        SessionScope scope,
        Func<IEnumerable<Command>, RootCommand> buildRoot,
        IReadOnlySet<string> oneShotCommands,
        InteractiveOptions options)
    {
        _scope = scope;
        _handler = new LiveSessionHandler(scope.Session, Client);
        _root = buildRoot(SessionCommands.Build(_handler, () => _exitRequested = true));
        _oneShotCommands = oneShotCommands;
        _options = options;
    }

    public RootCommand Root => _root;

    /// <summary>Completions for a line typed up to the cursor, for the line editor's Tab.</summary>
    public IReadOnlyList<string> Complete(string typed)
    {
        try
        {
            return _root.Parse(typed).GetCompletions(typed.Length).Select(item => item.InsertText ?? item.Label).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A completion source that fails (a model that cannot be read) offers nothing.
            return [];
        }
    }

    /// <summary>Runs until <c>exit</c> or the end of input, and returns the process exit code.</summary>
    public async Task<int> RunAsync(ILineReader reader, CancellationToken cancellationToken)
    {
        if (_options.Banner)
            Welcome();

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            var firstFailure = 0;
            var lineNumber = 0;
            while (true)
            {
                var line = reader.ReadLine(Prompt());
                lineNumber++;
                if (line is null)
                {
                    if (await TryLeaveAsync(cancellationToken) is { } code)
                        return code != 0 ? code : firstFailure;
                    continue;
                }

                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                if (_options.Echo)
                    StdErr.MarkupLine(Styling.Muted($"> {line}"));

                var exitCode = await ExecuteAsync(line, cancellationToken);
                if (_exitRequested)
                {
                    _exitRequested = false;
                    if (await TryLeaveAsync(cancellationToken) is { } code)
                        return code != 0 ? code : firstFailure;
                    continue;
                }

                if (exitCode != 0)
                {
                    if (_options.CanPrompt)
                        continue;

                    if (firstFailure == 0)
                        firstFailure = exitCode;
                    if (_options.Batch)
                    {
                        await StopAsync(lineNumber, cancellationToken);
                        return exitCode;
                    }

                    continue;
                }

                if (_options.Autosave && _scope.Session.IsDirty && !_handler.InTransaction)
                {
                    var saved = await ExecuteAsync("save", cancellationToken);
                    if (saved != 0 && !_options.CanPrompt && _options.Batch)
                    {
                        await StopAsync(lineNumber, cancellationToken);
                        return saved;
                    }
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    /// <summary>Parses and runs one line. A line naming a command the session lacks is explained
    /// rather than reported as unknown.</summary>
    internal async Task<int> ExecuteAsync(string line, CancellationToken cancellationToken)
    {
        var args = Program.RewriteHelpCommand(CommandLineParser.SplitCommandLine(line).ToArray());
        if (args.Length > 0
            && _oneShotCommands.Contains(args[0])
            && !_root.Subcommands.Any(command => command.Name == args[0] || command.Aliases.Contains(args[0])))
        {
            Error(
                "TOMIX_SESSION_COMMAND_UNAVAILABLE",
                $"'{args[0]}' is not available inside an interactive session.",
                $"Run 'tx {args[0]}' outside the session; 'help' lists what works here.");
            return 2;
        }

        args = Inherit(args);
        var parseResult = _root.Parse(args);
        if (parseResult.Errors.Count > 0)
            return UsageErrors.Report(parseResult, args);
        if (UnknownOptionGuard.TryReject(parseResult, args))
            return 2;

        // Each line is one transaction; its label is what history shows.
        _scope.Source.Options = new LiveLeaseOptions(Client, line);
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = running;
        try
        {
            return await parseResult.InvokeAsync(
                new InvocationConfiguration { EnableDefaultExceptionHandler = false, ProcessTerminationTimeout = null },
                running.Token);
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            StdErr.MarkupLine(Styling.Warning("Cancelled."));
            return 130;
        }
        catch (Exception ex)
        {
            return Program.ReportFailure(ex, parseResult);
        }
        finally
        {
            _running = null;
        }
    }

    /// <summary>The line's arguments plus each inherited option the line does not set itself.</summary>
    private string[] Inherit(string[] args)
    {
        var inherited = _options.Inherited
            .Where(entry => !args.Any(arg => Names(entry.Option).Any(name =>
                arg == name || arg.StartsWith(name + "=", StringComparison.Ordinal) || arg.StartsWith(name + ":", StringComparison.Ordinal))))
            .SelectMany(entry => entry.Tokens);
        return [.. args, .. inherited];
    }

    private static IEnumerable<string> Names(Option option) => option.Aliases.Prepend(option.Name);

    /// <summary>
    /// Leaves when nothing would be lost, when <c>--discard-on-exit</c> or <c>--yes</c> says to, or
    /// when the person at the keyboard confirms. Returns the exit code, or <c>null</c> to stay.
    /// </summary>
    private async Task<int?> TryLeaveAsync(CancellationToken cancellationToken)
    {
        if (!_handler.HasUnsavedWork)
            return 0;

        var what = Unsaved();
        if (!_options.DiscardOnExit)
        {
            if (!_options.CanPrompt)
            {
                Error(
                    "TOMIX_SESSION_DIRTY",
                    $"The session has {what}; leaving would discard them.",
                    "Run 'save' before 'exit', or pass --discard-on-exit (or --yes) to leave without saving.");
                await _handler.RollbackOpenTransactionAsync(cancellationToken);
                return 1;
            }

            if (!StdErr.Console().Confirm($"  Discard {Styling.MarkupEscape(what)} and exit?", defaultValue: false))
                return null;
        }

        await _handler.RollbackOpenTransactionAsync(cancellationToken);
        StdErr.MarkupLine(Styling.Warning($"Discarded {what}."));
        return 0;
    }

    /// <summary>Batch mode: a command failed, so the rest of the script does not run.</summary>
    private async Task StopAsync(int lineNumber, CancellationToken cancellationToken)
    {
        var lost = _handler.HasUnsavedWork ? $"; discarded {Unsaved()}" : "";
        await _handler.RollbackOpenTransactionAsync(cancellationToken);
        StdErr.MarkupLine(Styling.Warning($"Stopped at line {lineNumber}{lost}. Pass --no-batch to run past failures."));
    }

    private string Unsaved()
        => _handler.InTransaction
            ? _scope.Session.IsDirty ? "unsaved changes and an open transaction" : "an open transaction"
            : "unsaved changes";

    private void Error(string code, string message, string hint)
        => ErrorOutput.Write([new TomixDiagnostic(code, DiagnosticSeverity.Error, message, hint)], _options.ErrorFormat);

    private string Prompt()
    {
        var name = Styling.Path(ModelName(_scope.Model));
        var dirty = _scope.Session.IsDirty ? Styling.Warning("*") : "";
        var transaction = _handler.InTransaction ? " " + Styling.Muted("(transaction)") : "";
        return $"{name}{dirty}{transaction} {Styling.Muted(">")} ";
    }

    /// <summary>The short name the prompt shows: the folder or file name, or the remote database.</summary>
    internal static string ModelName(ModelReference model)
    {
        if (model.IsRemote && !string.IsNullOrWhiteSpace(model.Database))
            return model.Database;

        var path = Path.TrimEndingDirectorySeparator(model.Value);
        var name = Path.GetFileName(path);
        if (Path.HasExtension(name) && File.Exists(path))
            name = Path.GetFileNameWithoutExtension(name);
        return name.Length > 0 ? name : model.Value;
    }

    private void Welcome()
    {
        foreach (var line in Logo)
            StdErr.MarkupLine(Styling.Title(line));
        StdErr.MarkupLine($"{Styling.Bold("tx interactive")} {Styling.Muted("·")} {Styling.Path(_scope.Session.SourcePath)}");
        StdErr.MarkupLine(Styling.Guidance(
            "Edits stay in memory until you run 'save'. 'undo' steps back, 'help' lists commands, 'exit' leaves."));
    }

    private static readonly string[] Logo =
    [
        "▀█▀ ▀▄▀",
        " █  █ █"
    ];

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Ctrl-C stops the running command, never the session.
        e.Cancel = true;
        _running?.Cancel();
    }
}
