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
}
