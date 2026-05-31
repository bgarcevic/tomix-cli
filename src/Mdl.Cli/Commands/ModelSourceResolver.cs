using Mdl.App.State;

namespace Mdl.Cli.Commands;

internal static class ModelSourceResolver
{
    public static string Resolve(string? explicitModel)
        => new ActiveModelResolver().Resolve(explicitModel);
}
