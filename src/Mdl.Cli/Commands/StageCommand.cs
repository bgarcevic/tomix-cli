using System.CommandLine;
using Mdl.App.Stage;
using Mdl.Cli.Output;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

/// <summary>
/// <c>stage status | list | discard</c> — inspect and clear the working copies that <c>--stage</c>
/// accumulates for the active model. <c>stage commit</c> (promotion) is added separately.
/// </summary>
internal sealed class StageCommand : ICommandModule
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public StageCommand(IReadOnlyList<IModelProvider> providers) => _providers = providers;

    public Command Build()
    {
        var command = new Command("stage", "Inspect and manage staged (uncommitted) model mutations");
        command.SetAction(parseResult => RenderStatus(parseResult));
        command.Subcommands.Add(BuildStatus());
        command.Subcommands.Add(BuildList());
        command.Subcommands.Add(BuildDiscard());
        command.Subcommands.Add(BuildCommit());
        return command;
    }

    private Command BuildCommit()
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Commit even if the source changed since staging began (overwrites it)."
        };
        var command = new Command("commit", "Promote staged mutations onto the source (and workspace mirror)")
        {
            forceOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var result = await new StageHandler().CommitAsync(
                ResolveModel(parseResult), _providers, parseResult.GetValue(forceOption), cancellationToken);
            return CommandOutput.Render(result, format, RenderCommit);
        });
        return command;
    }

    private static Command BuildStatus()
    {
        var command = new Command("status", "Show staged mutations for the active model");
        command.SetAction(parseResult => RenderStatus(parseResult));
        return command;
    }

    private static Command BuildList()
    {
        var command = new Command("list", "List all staged models in the current session");
        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            return CommandOutput.Render(new StageHandler().List(), format, RenderList);
        });
        return command;
    }

    private static Command BuildDiscard()
    {
        var allOption = new Option<bool>("--all")
        {
            Description = "Discard staged mutations for every model in the session, not just the active one"
        };
        var command = new Command("discard", "Discard staged mutations without committing them")
        {
            allOption
        };

        command.SetAction(parseResult =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var all = parseResult.GetValue(allOption);
            return CommandOutput.Render(
                new StageHandler().Discard(ResolveModel(parseResult), all),
                format,
                result => Console.WriteLine($"Discarded {result.Discarded} staged change set(s)."));
        });
        return command;
    }

    private static int RenderStatus(ParseResult parseResult)
    {
        var format = GlobalOptions.OutputFormatValue(parseResult);
        if (!CommandOutput.TryValidateFormat(format))
            return 2;

        return CommandOutput.Render(new StageHandler().Status(ResolveModel(parseResult)), format, RenderStatusResult);
    }

    private static void RenderStatusResult(StageStatusResult result)
    {
        if (!result.Staged)
        {
            Console.WriteLine($"Nothing staged for {result.Source}.");
            return;
        }

        Console.WriteLine($"Source:   {result.Source}");
        Console.WriteLine($"Staged:   {result.WorkingCopy}  ({result.Serialization})");
        if (result.Workspace)
            Console.WriteLine("Workspace: yes (commit syncs local + remote)");
        Console.WriteLine($"Updated:  {result.UpdatedUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Ops:      {result.OpCount}");
        foreach (var op in result.Ops)
            Console.WriteLine($"  {op.Seq}. {op.Summary}");
        Console.WriteLine("Run 'mdl stage commit' to promote, or 'mdl stage discard' to drop.");
    }

    private static void RenderList(StageListResult result)
    {
        if (result.Staged.Count == 0)
        {
            Console.WriteLine("No staged models in this session.");
            return;
        }

        foreach (var entry in result.Staged)
            Console.WriteLine($"{entry.Source}\t{entry.OpCount} op(s)\t{entry.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    }

    private static void RenderCommit(StageCommitResult result)
    {
        Console.WriteLine($"Committed {result.OpsCommitted} staged change(s).");
        if (result.LocalSaved is not null)
            Console.WriteLine($"Local:  {result.LocalSaved}");
        if (result.RemoteDeployed)
            Console.WriteLine($"Remote: {result.Server}{(string.IsNullOrEmpty(result.Database) ? "" : $" / {result.Database}")}");
    }

    private static ModelReference ResolveModel(ParseResult parseResult)
        => ModelSourceResolver.ResolveReference(
            GlobalOptions.ModelValue(parseResult),
            parseResult.GetValue(GlobalOptions.Database));
}
