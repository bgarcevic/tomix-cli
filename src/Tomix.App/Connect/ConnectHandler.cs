using Tomix.App.State;
using Tomix.Core.Models;
using Tomix.Core.Results;

namespace Tomix.App.Connect;

public sealed class ConnectHandler
{
    private readonly CliStateStore _store;

    public ConnectHandler(CliStateStore store) => _store = store;

    public TomixResult<ConnectShowResult> Show()
    {
        var state = _store.LoadCurrentSession();

        // Drop a cached Desktop report name that no longer describes whatever is on that port —
        // Desktop may have restarted, or closed entirely — so no caller can display a stale name.
        if (state?.ReportName is not null
            && !PowerBiDesktopDiscovery.StillServes(state.ReportPortFile, state.Server))
        {
            state = state with { ReportName = null, ReportPortFile = null };
        }

        return TomixResult<ConnectShowResult>.Ok(new ConnectShowResult(state is not null, state));
    }

    public TomixResult<ConnectClearResult> Clear()
    {
        var existed = _store.LoadCurrentSession() is not null;
        _store.ClearCurrentSession();
        return TomixResult<ConnectClearResult>.Ok(new ConnectClearResult(existed));
    }

    public TomixResult<ConnectSetResult> Set(ConnectSetRequest request)
    {
        CliConnectionState state;
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            state = new CliConnectionState(
                null,
                request.Database,
                NormalizeLocalPath(request.Model),
                request.Auth,
                Local: true,
                Profile: request.Profile,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth);
        }
        else if (request.Local)
        {
            state = new CliConnectionState(
                // A Power BI Desktop instance is addressed by its discovered `localhost:<port>`
                // endpoint, so it has to survive here: with no Model and no Server the state says
                // "local" without naming a target, and ActiveModelResolver resolves it to nothing.
                // Anything that is not a local-instance endpoint is not a `--local` target.
                ModelReference.IsLocalInstanceEndpoint(request.Server) ? request.Server : null,
                request.Database,
                null,
                request.Auth,
                Local: true,
                Profile: request.Profile,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth,
                request.ReportName,
                request.ReportPortFile);
        }
        else
        {
            state = new CliConnectionState(
                request.Server,
                request.Database,
                null,
                request.Auth,
                Local: false,
                Profile: request.Profile,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth);
        }

        _store.SaveCurrentSession(state);
        _store.AddRecentConnection(state);
        return TomixResult<ConnectSetResult>.Ok(new ConnectSetResult(Active: true, state));
    }

    public TomixResult<ConnectRecentListResult> Recents()
        => TomixResult<ConnectRecentListResult>.Ok(
            new ConnectRecentListResult(_store.LoadRecentConnections()));

    private static string? NormalizeLocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || ModelReference.IsRemoteEndpoint(path) || Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(path);
    }
}

public sealed record ConnectSetRequest(
    string? Server,
    string? Database,
    string? Model,
    string? Auth,
    bool Local,
    string? Profile,
    string? Workspace = null,
    string? WorkspaceFormat = null,
    string? WorkspaceAuth = null,
    /// <summary>Desktop report name to cache; see <c>CliConnectionState.ReportName</c>.</summary>
    string? ReportName = null,
    /// <summary>Port file the report name came from, used to revalidate it cheaply.</summary>
    string? ReportPortFile = null);
