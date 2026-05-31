using Mdl.Core.Models;

namespace Mdl.App.State;

public sealed class ActiveModelResolver
{
    private readonly CliStateStore _store;

    public ActiveModelResolver()
        : this(new CliStateStore())
    {
    }

    public ActiveModelResolver(CliStateStore store) => _store = store;

    public string Resolve(string? explicitModel)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
            return explicitModel;

        return _store.LoadCurrentSession()?.Model ?? "";
    }

    /// <summary>
    /// Resolves the model to open as a <see cref="ModelReference"/>. An explicit value is taken
    /// verbatim (local path or remote endpoint). Otherwise the active session is used: a local
    /// model path if present, else a remote endpoint built from the session server + database.
    /// An explicit <paramref name="database"/> applies to remote endpoints (the dataset/catalog)
    /// and overrides the session database.
    /// </summary>
    public ModelReference ResolveReference(string? explicitModel, string? database = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
            return ModelReference.IsRemoteEndpoint(explicitModel)
                ? new ModelReference(explicitModel, NullIfBlank(database))
                : new ModelReference(explicitModel);

        var session = _store.LoadCurrentSession();
        if (session is null)
            return new ModelReference("");

        if (!string.IsNullOrWhiteSpace(session.Model))
            return new ModelReference(session.Model);

        if (!string.IsNullOrWhiteSpace(session.Server))
            return new ModelReference(session.Server, NullIfBlank(database) ?? session.Database);

        return new ModelReference("");
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
