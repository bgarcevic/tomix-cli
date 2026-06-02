using System.CommandLine;

namespace Mdl.Cli.Commands;

internal static class RootHelpRenderer
{
    private static readonly IReadOnlyDictionary<string, string> UsageOverrides = new Dictionary<string, string>
    {
        ["add"] = "add <path> [model]",
        ["completion"] = "completion <shell>",
        ["connect"] = "connect [server] [database]",
        ["deploy"] = "deploy [model]",
        ["deps"] = "deps [path] [model]",
        ["diff"] = "diff <left> <right>",
        ["find"] = "find <pattern> [model]",
        ["format"] = "format [model]",
        ["get"] = "get <path> [model]",
        ["init"] = "init [output-path]",
        ["interactive"] = "interactive [model]",
        ["load"] = "load [model]",
        ["ls"] = "ls [model] [path-filter]",
        ["mv"] = "mv <source> <destination> [model]",
        ["replace"] = "replace [pattern] [replacement] [model]",
        ["rm"] = "rm <path> [model]",
        ["save"] = "save [model]",
        ["script"] = "script [model]",
        ["set"] = "set <path> [model]",
        ["validate"] = "validate [model]",
        ["vertipaq"] = "vertipaq [path]"
    };

    public static bool IsRootHelpRequest(IReadOnlyList<string> args)
        => args.Count == 1 && args[0] is "-h" or "/h" or "-?" or "/?" or "--help";

    public static bool IsRootInvocation(IReadOnlyList<string> args)
        => args.Count == 0;

    public static void Write(RootCommand root, TextWriter output)
    {
        output.WriteLine("mdl");
        output.WriteLine("  Semantic model command line");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  mdl [command] [options]");
        output.WriteLine();
        output.WriteLine("Global options:");
        output.WriteLine("  -h, /h, -?, /?, --help           Show help and usage information");
        output.WriteLine("  --version                        Show version information");
        output.WriteLine("  -m, --model <model>              Path to semantic model (TMDL folder, .bim file, or TE folder)");
        output.WriteLine("  --output-format <output-format>  Stdout format: text (default), json, csv, tmsl (alias: bim), tmdl. Not all formats are supported by every command.");
        output.WriteLine("  --error-format <error-format>    Stderr format for errors/warnings/hints: text (default) or json. Other values fall back to text.");
        output.WriteLine("  -s, --server <server>            Workspace name or endpoint (e.g., MyWorkspace, powerbi://..., asazure://..., localhost)");
        output.WriteLine("  -d, --database <database>        Semantic model name on the workspace");
        output.WriteLine("  --local                          Connect to a locally running Power BI Desktop instance (Windows only)");
        output.WriteLine("  --auth <auth>                    Auth method: auto, interactive, spn, env, managed-identity (default: auto)");
        output.WriteLine("  --recent <recent>                Use a recently used model. No value = interactive picker, N = Nth most recent (1 = last used).");
        output.WriteLine("  --debug                          Enable debug logging to stderr (connection strings, auth flow, timing)");
        output.WriteLine("  --non-interactive                Disable all interactive prompts. Fail with an actionable error if required input is missing.");
        output.WriteLine();
        output.WriteLine("Commands:");

        var rows = root.Subcommands
            .Select(command => (Usage: UsageFor(command), command.Description))
            .ToArray();
        var width = rows.Max(row => row.Usage.Length) + 2;

        foreach (var (usage, description) in rows)
            output.WriteLine($"  {usage.PadRight(width)}  {description}");

        output.WriteLine();
        output.WriteLine("Use `mdl <command> --help` for command-specific options.");
    }

    private static string UsageFor(Command command)
    {
        if (UsageOverrides.TryGetValue(command.Name, out var usage))
            return usage;

        return command.Name;
    }
}
