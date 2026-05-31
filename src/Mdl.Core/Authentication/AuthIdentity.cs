namespace Mdl.Core.Authentication;

/// <summary>
/// The signed-in account as reported by <c>auth login</c>/<c>auth status</c>.
/// <paramref name="Storage"/> is a human label for where the token cache lives
/// (e.g. "OS keystore (DPAPI)").
/// </summary>
public sealed record AuthIdentity(
    string Username,
    string? TenantId,
    AuthMethod Method,
    DateTimeOffset? ExpiresOn,
    string Storage);
