using Mdl.App.State;
using Mdl.Core.Models;

namespace Mdl.Cli.Commands;

internal static class ModelSourceResolver
{
    public static string Resolve(string? explicitModel)
        => new ActiveModelResolver().Resolve(explicitModel);

    /// <summary>
    /// Resolves the model source as a <see cref="ModelReference"/>, falling back to the active
    /// session's remote endpoint (server + database) when no local model is in play. An explicit
    /// <paramref name="database"/> selects the dataset/catalog for a remote endpoint.
    /// </summary>
    public static ModelReference ResolveReference(string? explicitModel, string? database = null)
        => new ActiveModelResolver().ResolveReference(explicitModel, database);
}
