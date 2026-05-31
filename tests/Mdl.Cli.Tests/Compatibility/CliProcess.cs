using System.Diagnostics;

namespace Mdl.Cli.Tests.Compatibility;

internal sealed record CliRun(int ExitCode, string StdOut, string StdErr);

internal static class CliProcess
{
    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    public static string RepositoryRoot => Root.Value;

    public static CliRun RunMdl(params string[] args)
    {
        var cliDll = Path.Combine(
            RepositoryRoot,
            "src",
            "Mdl.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Mdl.Cli.dll");

        Assert.True(File.Exists(cliDll), $"Expected built CLI at {cliDll}");
        return Run("dotnet", [cliDll, .. args], environment: null);
    }

    public static CliRun RunMdlWithEnvironment(
        IReadOnlyDictionary<string, string> environment,
        params string[] args)
    {
        var cliDll = Path.Combine(
            RepositoryRoot,
            "src",
            "Mdl.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Mdl.Cli.dll");

        Assert.True(File.Exists(cliDll), $"Expected built CLI at {cliDll}");
        return Run("dotnet", [cliDll, .. args], environment);
    }

    public static CliRun RunReference(params string[] args)
    {
        var te = Path.Combine(RepositoryRoot, "references", "te.exe");
        Assert.True(File.Exists(te), $"Expected compatibility reference at {te}");
        return Run(te, args, environment: null);
    }

    private static CliRun Run(
        string fileName,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = RepositoryRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                process.StartInfo.Environment[key] = value;
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(milliseconds: 30000), $"Timed out running {fileName} {string.Join(" ", args)}");
        return new CliRun(process.ExitCode, Normalize(stdout), Normalize(stderr));
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").TrimEnd();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mdl.slnx")) &&
                File.Exists(Path.Combine(dir.FullName, "references", "te.exe")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing Mdl.slnx and references/te.exe.");
    }
}
