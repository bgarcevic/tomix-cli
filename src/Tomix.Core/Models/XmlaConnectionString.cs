namespace Tomix.Core.Models;

/// <summary>
/// Builds the connection string every client connection to a tabular endpoint uses — AMO
/// sessions, deploy targets, query and trace connections, and VertiPaq extraction.
/// </summary>
/// <remarks>
/// This exists so the remote connect timeout has exactly one home. It previously lived at a
/// single call site, which left <c>deploy</c> and <c>vertipaq</c> reaching their endpoints
/// through connection strings that carried no timeout at all.
/// <para>
/// Not for connection strings written <em>into</em> a model: a partition's data-source
/// definition is model content that happens to share the syntax, and it must not inherit the
/// CLI's client-side timeout.
/// </para>
/// </remarks>
public static class XmlaConnectionString
{
    /// <summary>
    /// Seconds AMO waits for a remote connect before failing. Matches the 30 s timeout the REST
    /// side already applies to its <c>HttpClient</c>, so the two remote paths give up together.
    /// </summary>
    /// <remarks>
    /// Without a cap, a cold or unreachable Power BI XMLA endpoint parks every command that
    /// reaches a remote model — <c>info</c>, <c>ls</c>, <c>get</c>, <c>query</c>, <c>deploy</c>,
    /// <c>refresh</c>, <c>vertipaq</c> — on its spinner indefinitely. <c>Server.Connect</c> is a
    /// blocking call that never observes the cancellation token it was handed, so Ctrl-C cannot
    /// break out either. Capping the wait is the cheap half of that fix; making the call
    /// genuinely cancellable is a separate problem, and only worth solving if a capped wait
    /// still feels stuck in practice.
    /// </remarks>
    public const int RemoteConnectTimeoutSeconds = 30;

    /// <summary>
    /// The connection string for <paramref name="endpoint"/>, normalized to a fully-qualified
    /// XMLA address, optionally scoped to <paramref name="database"/>, and carrying the remote
    /// connect timeout unless the endpoint is a local Power BI Desktop instance.
    /// </summary>
    public static string Build(string? endpoint, string? database = null)
    {
        var connectionString = $"Data Source={ModelReference.NormalizeEndpoint(endpoint)}";
        if (!string.IsNullOrWhiteSpace(database))
            connectionString += $";Initial Catalog={database}";

        // A Power BI Desktop instance is on loopback and answers immediately; a timeout there
        // would only add a way to fail.
        return ModelReference.IsLocalInstanceEndpoint(endpoint)
            ? connectionString
            : $"{connectionString};Connect Timeout={RemoteConnectTimeoutSeconds}";
    }

    /// <summary>The connection string for <paramref name="reference"/> and its database, if any.</summary>
    public static string Build(ModelReference reference)
        => Build(reference.Value, reference.Database);
}
