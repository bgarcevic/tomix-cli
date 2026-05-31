using Mdl.App.State;
using Mdl.Core.Results;

namespace Mdl.App.Session;

public sealed class SessionHandler
{
    private readonly CliStateStore _store;

    public SessionHandler()
        : this(new CliStateStore())
    {
    }

    public SessionHandler(CliStateStore store) => _store = store;

    public MdlResult<SessionShowResult> Show()
    {
        var state = _store.LoadCurrentSession();
        return MdlResult<SessionShowResult>.Ok(new SessionShowResult(
            _store.CurrentSessionId,
            _store.CurrentSessionKind,
            _store.CurrentSessionFile,
            state is not null,
            state));
    }

    public MdlResult<SessionListResult> List()
        => MdlResult<SessionListResult>.Ok(new SessionListResult(_store.ListSessions()));

    public MdlResult<SessionClearResult> Clear()
    {
        var existed = _store.LoadCurrentSession() is not null;
        _store.ClearCurrentSession();
        return MdlResult<SessionClearResult>.Ok(new SessionClearResult(existed));
    }

    public MdlResult<SessionPruneResult> Prune(bool all, bool dryRun)
    {
        if (dryRun)
        {
            var count = _store.ListSessions().Count(s => !s.Current);
            return MdlResult<SessionPruneResult>.Ok(new SessionPruneResult(count, DryRun: true));
        }

        return MdlResult<SessionPruneResult>.Ok(new SessionPruneResult(_store.PruneSessions(all), DryRun: false));
    }
}
