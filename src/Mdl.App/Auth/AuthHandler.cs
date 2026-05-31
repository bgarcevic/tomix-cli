using Mdl.Core.Authentication;
using Mdl.Core.Results;

namespace Mdl.App.Auth;

/// <summary>
/// Application use case behind the <c>auth</c> command. Delegates to an <see cref="IAuthenticator"/>
/// (the MSAL implementation is injected from the CLI) and shapes the outcome into <see cref="MdlResult{T}"/>.
/// </summary>
public sealed class AuthHandler
{
    private readonly IAuthenticator _authenticator;

    public AuthHandler(IAuthenticator authenticator) => _authenticator = authenticator;

    public async Task<MdlResult<AuthLoginResult>> LoginAsync(
        AuthLoginOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var identity = await _authenticator.LoginAsync(options, cancellationToken);
            return MdlResult<AuthLoginResult>.Ok(new AuthLoginResult(identity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MdlResult<AuthLoginResult>.Fail("MDL_AUTH_FAILED", $"Authentication failed: {ex.Message}");
        }
    }

    public async Task<MdlResult<AuthStatusResult>> StatusAsync(CancellationToken cancellationToken)
    {
        var identity = await _authenticator.StatusAsync(cancellationToken);
        return MdlResult<AuthStatusResult>.Ok(new AuthStatusResult(identity is not null, identity));
    }

    public async Task<MdlResult<AuthLogoutResult>> LogoutAsync(CancellationToken cancellationToken)
    {
        var existed = await _authenticator.LogoutAsync(cancellationToken);
        return MdlResult<AuthLogoutResult>.Ok(new AuthLogoutResult(existed));
    }
}

public sealed record AuthLoginResult(AuthIdentity Identity);

public sealed record AuthStatusResult(bool LoggedIn, AuthIdentity? Identity);

public sealed record AuthLogoutResult(bool Existed);
