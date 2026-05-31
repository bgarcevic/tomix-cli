using System.CommandLine;
using Mdl.App.Config;
using Mdl.Cli.Output;
using Mdl.Core.Configuration;

namespace Mdl.Cli.Commands;

internal sealed class ConfigCommand : ICommandModule
{
    public Command Build()
    {
        var command = new Command("config", "View and manage CLI configuration");

        command.Subcommands.Add(BuildInit());
        command.Subcommands.Add(BuildList());
        command.Subcommands.Add(BuildGet());
        command.Subcommands.Add(BuildPaths());
        command.Subcommands.Add(BuildSet());
        command.Subcommands.Add(BuildShow());

        return command;
    }

    private static Command BuildInit()
    {
        var command = new Command("init", "Create a default config.json.");

        command.SetAction(_ =>
        {
            Directory.CreateDirectory(MdlPaths.ConfigDirectory);
            if (!File.Exists(MdlPaths.ConfigFile))
                File.WriteAllText(MdlPaths.ConfigFile, "{\n}\n");

            Console.WriteLine(MdlPaths.ConfigFile);
            return 0;
        });

        return command;
    }

    private static Command BuildList()
    {
        var command = new Command("list", "List all configuration values.");

        command.SetAction(parseResult =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var result = new ConfigHandler().List();
            return CommandOutput.Render(result, formatValue, RenderList);
        });

        return command;
    }

    private static Command BuildGet()
    {
        var keyArgument = new Argument<string>("key") { Description = "Configuration key." };

        var command = new Command("get", "Get a configuration value.")
        {
            keyArgument
        };

        command.SetAction(parseResult =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var key = parseResult.GetValue(keyArgument) ?? "";
            var result = new ConfigHandler().Get(key);
            return CommandOutput.Render(result, formatValue, RenderGet);
        });

        return command;
    }

    private static Command BuildSet()
    {
        var keyArgument = new Argument<string>("key") { Description = "Configuration key." };
        var valueArgument = new Argument<string>("value") { Description = "Configuration value." };

        var command = new Command("set", "Set a configuration value.")
        {
            keyArgument,
            valueArgument
        };

        command.SetAction(parseResult =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var key = parseResult.GetValue(keyArgument) ?? "";
            var value = parseResult.GetValue(valueArgument) ?? "";
            var result = new ConfigHandler().Set(key, value);
            return CommandOutput.Render(result, formatValue, RenderSet);
        });

        return command;
    }

    private static Command BuildPaths()
    {
        var command = new Command("paths", "Show resolved paths for local CLI files.");

        command.SetAction(_ =>
        {
            Console.WriteLine($"configDir   {MdlPaths.ConfigDirectory}");
            Console.WriteLine($"configFile  {MdlPaths.ConfigFile}");
            return 0;
        });

        return command;
    }

    private static Command BuildShow()
    {
        var command = new Command("show", "Show current CLI configuration.");

        command.SetAction(parseResult =>
        {
            var formatValue = GlobalOptions.OutputFormatValue(parseResult);

            if (!CommandOutput.TryValidateFormat(formatValue))
                return 2;

            var result = new ConfigHandler().List();
            return CommandOutput.Render(result, formatValue, RenderList);
        });

        return command;
    }

    private static void RenderList(ConfigListResult result)
    {
        if (result.Values.Count == 0)
        {
            Console.WriteLine("No configuration values set.");
            return;
        }

        var nameWidth = result.Values.Keys.Max(key => key.Length);

        foreach (var (key, value) in result.Values)
            Console.WriteLine($"{key.PadRight(nameWidth)}  {value}");
    }

    private static void RenderGet(ConfigGetResult result)
        => Console.WriteLine(result.Value ?? "");

    private static void RenderSet(ConfigSetResult result)
        => Console.WriteLine($"{result.Key} = {result.Value}");
}
