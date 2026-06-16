using Mdl.App.State;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Connect;

public sealed class ConnectHandler
{
    private readonly CliStateStore _store;

    public ConnectHandler()
        : this(new CliStateStore())
    {
    }

    public ConnectHandler(CliStateStore store) => _store = store;

    public MdlResult<ConnectShowResult> Show()
    {
        var state = _store.LoadCurrentSession();
        return MdlResult<ConnectShowResult>.Ok(new ConnectShowResult(state is not null, state));
    }

    public MdlResult<ConnectClearResult> Clear()
    {
        var existed = _store.LoadCurrentSession() is not null;
        _store.ClearCurrentSession();
        return MdlResult<ConnectClearResult>.Ok(new ConnectClearResult(existed));
    }

    public MdlResult<ConnectSetResult> Set(ConnectSetRequest request)
    {
        CliConnectionState? state;
        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            var profiles = _store.LoadProfiles();
            if (!profiles.TryGetValue(request.Profile, out var profile))
                return MdlResult<ConnectSetResult>.Fail(
                    "MDL_PROFILE_NOT_FOUND",
                    $"Profile '{request.Profile}' not found",
                    exitCode: 1);

            state = new CliConnectionState(
                profile.Server,
                profile.Database,
                NormalizeLocalPath(profile.Model),
                profile.Auth,
                Local: !string.IsNullOrWhiteSpace(profile.Model),
                Profile: profile.Name);
        }
        else if (!string.IsNullOrWhiteSpace(request.Model))
        {
            state = new CliConnectionState(
                null,
                request.Database,
                NormalizeLocalPath(request.Model),
                request.Auth,
                Local: true,
                Profile: null,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth);
        }
        else if (request.Local)
        {
            state = new CliConnectionState(
                null,
                request.Database,
                null,
                request.Auth,
                Local: true,
                Profile: null,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth);
        }
        else
        {
            state = new CliConnectionState(
                request.Server,
                request.Database,
                null,
                request.Auth,
                Local: false,
                Profile: null,
                request.Workspace,
                request.WorkspaceFormat,
                request.WorkspaceAuth);
        }

        _store.SaveCurrentSession(state);
        return MdlResult<ConnectSetResult>.Ok(new ConnectSetResult(Active: true, state));
    }

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
    string? WorkspaceAuth = null);
