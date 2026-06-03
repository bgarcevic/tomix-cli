using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;
using Microsoft.Identity.Client.Extensions.Msal;
using Mdl.Core.Authentication;
using Mdl.Core.Configuration;

namespace Mdl.Auth;

/// <summary>
/// MSAL-backed authentication: interactive browser, device code, service principal
/// (secret/certificate), and managed identity. Tokens are cached via MSAL.Extensions
/// (DPAPI on Windows, Keychain on macOS, libsecret on Linux). Implements both the
/// <see cref="IAuthenticator"/> contract used by the <c>auth</c> command and the
/// <see cref="IAccessTokenProvider"/> contract used by the remote TOM provider.
/// </summary>
public sealed class MsalAuthenticator : IAuthenticator, IAccessTokenProvider
{
    private const string CacheFileName = "mdl-msal.cache";

    private readonly MsalAuthSettings _settings;
    private readonly string _cacheDirectory;
    private readonly AuthStateStore _stateStore;
    private readonly Action<string> _messageWriter;

    private IPublicClientApplication? _publicApp;

    public MsalAuthenticator(
        MsalAuthSettings settings,
        string? cacheDirectory = null,
        string? stateFile = null,
        Action<string>? messageWriter = null)
    {
        _settings = settings;
        _cacheDirectory = cacheDirectory ?? MdlPaths.AuthDirectory;
        _stateStore = new AuthStateStore(stateFile ?? MdlPaths.AuthStateFile);
        _messageWriter = messageWriter ?? (_ => { });
    }

    public async Task<AuthIdentity> LoginAsync(AuthLoginOptions options, CancellationToken cancellationToken)
    {
        var scopes = AuthScopes.ForEndpoint(options.TargetEndpoint);
        var result = options.Method switch
        {
            AuthMethod.Interactive => await InteractiveAsync(scopes, options, cancellationToken).ConfigureAwait(false),
            AuthMethod.DeviceCode => await DeviceCodeAsync(scopes, options, cancellationToken).ConfigureAwait(false),
            AuthMethod.ServicePrincipalSecret => await ClientCredentialsAsync(scopes, options, useCertificate: false, cancellationToken).ConfigureAwait(false),
            AuthMethod.ServicePrincipalCertificate => await ClientCredentialsAsync(scopes, options, useCertificate: true, cancellationToken).ConfigureAwait(false),
            AuthMethod.ManagedIdentity => await ManagedIdentityAsync(options.TargetEndpoint, options.ClientId, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Method, "Unsupported auth method.")
        };

        var identity = ToIdentity(result, options);
        _stateStore.Save(new AuthState(
            options.Method,
            identity.Username,
            identity.TenantId,
            options.ClientId,
            options.TargetEndpoint,
            identity.ExpiresOn));
        return identity;
    }

    public async Task<AuthIdentity?> StatusAsync(CancellationToken cancellationToken)
    {
        var state = _stateStore.Load();
        if (state is null)
            return null;

        if (state.Method is AuthMethod.Interactive or AuthMethod.DeviceCode)
        {
            var app = await EnsurePublicAppAsync(state.TenantId).ConfigureAwait(false);
            var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (account is null)
            {
                // The MSAL cache was cleared independently of our sidecar; treat as signed out.
                _stateStore.Delete();
                return null;
            }

            var expiresOn = state.ExpiresOn;
            try
            {
                var refreshed = await app.AcquireTokenSilent(AuthScopes.ForEndpoint(state.Endpoint), account)
                    .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                expiresOn = refreshed.ExpiresOn;
            }
            catch (MsalUiRequiredException)
            {
                // Token needs an interactive refresh; report the cached account with the stored expiry.
            }

            return new AuthIdentity(account.Username, state.TenantId, state.Method, expiresOn, StorageLabel);
        }

        // Service principal / managed identity: no user account in the cache to refresh from.
        return new AuthIdentity(state.Username, state.TenantId, state.Method, state.ExpiresOn, StorageLabel);
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken)
    {
        var hadState = _stateStore.Delete();
        var removedAccounts = false;

        try
        {
            var app = await EnsurePublicAppAsync(null).ConfigureAwait(false);
            foreach (var account in (await app.GetAccountsAsync().ConfigureAwait(false)).ToList())
            {
                await app.RemoveAsync(account).ConfigureAwait(false);
                removedAccounts = true;
            }
        }
        catch
        {
            // Cache may be absent or unreadable; nothing else to remove.
        }

        return hadState || removedAccounts;
    }

    public async Task<AccessToken> GetTokenAsync(string endpoint, CancellationToken cancellationToken)
    {
        var scopes = AuthScopes.ForEndpoint(endpoint);
        var state = _stateStore.Load();
        var method = state?.Method ?? AuthMethod.Interactive;

        switch (method)
        {
            case AuthMethod.Interactive:
            case AuthMethod.DeviceCode:
            {
                var app = await EnsurePublicAppAsync(state?.TenantId).ConfigureAwait(false);
                var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault()
                    ?? throw new AuthenticationRequiredException("Not authenticated. Run 'mdl auth login'.");
                try
                {
                    var result = await app.AcquireTokenSilent(scopes, account)
                        .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    return new AccessToken(result.AccessToken, result.ExpiresOn);
                }
                catch (MsalUiRequiredException)
                {
                    throw new AuthenticationRequiredException("Session expired. Run 'mdl auth login'.");
                }
            }

            case AuthMethod.ServicePrincipalSecret:
            case AuthMethod.ServicePrincipalCertificate:
            {
                var options = ServicePrincipalFromEnvironment(state, method, endpoint)
                    ?? throw new AuthenticationRequiredException(
                        "Service-principal credentials are not available. Set MDL_AUTH_CLIENT_ID, MDL_AUTH_TENANT, and MDL_AUTH_CLIENT_SECRET (or MDL_AUTH_CERTIFICATE), then retry.");
                var result = await ClientCredentialsAsync(scopes, options, method == AuthMethod.ServicePrincipalCertificate, cancellationToken).ConfigureAwait(false);
                return new AccessToken(result.AccessToken, result.ExpiresOn);
            }

            case AuthMethod.ManagedIdentity:
            {
                var result = await ManagedIdentityAsync(endpoint, state?.ClientId, cancellationToken).ConfigureAwait(false);
                return new AccessToken(result.AccessToken, result.ExpiresOn);
            }

            default:
                throw new AuthenticationRequiredException("Not authenticated. Run 'mdl auth login'.");
        }
    }

    private async Task<AuthenticationResult> InteractiveAsync(
        string[] scopes,
        AuthLoginOptions options,
        CancellationToken cancellationToken)
    {
        var app = await EnsurePublicAppAsync(options.Tenant).ConfigureAwait(false);
        var builder = app.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false);

        var existing = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        if (existing is not null)
            builder = builder.WithAccount(existing);

        return await builder.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthenticationResult> DeviceCodeAsync(
        string[] scopes,
        AuthLoginOptions options,
        CancellationToken cancellationToken)
    {
        var app = await EnsurePublicAppAsync(options.Tenant).ConfigureAwait(false);
        return await app.AcquireTokenWithDeviceCode(scopes, deviceCode =>
            {
                _messageWriter(deviceCode.Message);
                return Task.CompletedTask;
            })
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthenticationResult> ClientCredentialsAsync(
        string[] scopes,
        AuthLoginOptions options,
        bool useCertificate,
        CancellationToken cancellationToken)
    {
        var app = BuildConfidentialApp(options, useCertificate);
        return await app.AcquireTokenForClient(scopes).ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AuthenticationResult> ManagedIdentityAsync(string? endpoint, string? clientId, CancellationToken cancellationToken)
    {
        var identityId = string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.WithUserAssignedClientId(clientId);
        var app = ManagedIdentityApplicationBuilder.Create(identityId).Build();
        return await app.AcquireTokenForManagedIdentity(AuthScopes.ResourceForEndpoint(endpoint))
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private IConfidentialClientApplication BuildConfidentialApp(AuthLoginOptions options, bool useCertificate)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new AuthenticationRequiredException("Service-principal sign-in requires a client id (--username/-u).");

        if (string.IsNullOrWhiteSpace(options.Tenant))
            throw new AuthenticationRequiredException("Service-principal sign-in requires a tenant (--tenant/-t).");

        var builder = ConfidentialClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{options.Tenant}");

        if (useCertificate)
        {
            if (string.IsNullOrWhiteSpace(options.CertificatePath))
                throw new AuthenticationRequiredException("Certificate sign-in requires --certificate <path>.");

            var certificate = LoadCertificate(options.CertificatePath, options.CertificatePassword);
            builder = builder.WithCertificate(certificate);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.ClientSecret))
                throw new AuthenticationRequiredException("Service-principal sign-in requires a client secret (--password/-p).");

            builder = builder.WithClientSecret(options.ClientSecret);
        }

        return builder.Build();
    }

    private static X509Certificate2 LoadCertificate(string path, string? password)
    {
        if (path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(password)
                ? X509Certificate2.CreateFromPemFile(path)
                : X509Certificate2.CreateFromEncryptedPemFile(path, password.AsSpan());

        return X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }

    private async Task<IPublicClientApplication> EnsurePublicAppAsync(string? tenant)
    {
        if (_publicApp is not null)
            return _publicApp;

        var app = PublicClientApplicationBuilder
            .Create(_settings.ClientId)
            .WithAuthority(ResolveAuthority(tenant))
            .WithRedirectUri("http://localhost")
            .Build();

        var helper = await CreateCacheHelperAsync().ConfigureAwait(false);
        helper.RegisterCache(app.UserTokenCache);

        _publicApp = app;
        return app;
    }

    private string ResolveAuthority(string? tenant)
        => string.IsNullOrWhiteSpace(tenant)
            ? _settings.Authority
            : $"https://login.microsoftonline.com/{tenant}";

    private async Task<MsalCacheHelper> CreateCacheHelperAsync()
    {
        var storage = new StorageCreationPropertiesBuilder(CacheFileName, _cacheDirectory)
            .WithLinuxKeyring(
                schemaName: "com.mdl.tokencache",
                collection: MsalCacheHelper.LinuxKeyRingDefaultCollection,
                secretLabel: "MDL token cache",
                attribute1: new KeyValuePair<string, string>("Version", "1"),
                attribute2: new KeyValuePair<string, string>("Product", "mdl"))
            .WithMacKeyChain(serviceName: "mdl-msal-cache", accountName: "MDLCache")
            .Build();

        return await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);
    }

    private AuthIdentity ToIdentity(AuthenticationResult result, AuthLoginOptions options)
    {
        var username = result.Account?.Username ?? DefaultUsername(options);
        var tenant = result.TenantId
            ?? options.Tenant
            ?? result.Account?.HomeAccountId?.TenantId;

        return new AuthIdentity(username, tenant, options.Method, result.ExpiresOn, StorageLabel);
    }

    private static string DefaultUsername(AuthLoginOptions options)
        => options.Method == AuthMethod.ManagedIdentity
            ? "managed-identity"
            : string.IsNullOrWhiteSpace(options.ClientId) ? "service-principal" : $"app:{options.ClientId}";

    private static AuthLoginOptions? ServicePrincipalFromEnvironment(AuthState? state, AuthMethod method, string endpoint)
    {
        var clientId = state?.ClientId ?? Environment.GetEnvironmentVariable("MDL_AUTH_CLIENT_ID");
        var tenant = state?.TenantId ?? Environment.GetEnvironmentVariable("MDL_AUTH_TENANT");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tenant))
            return null;

        if (method == AuthMethod.ServicePrincipalCertificate)
        {
            var certificatePath = Environment.GetEnvironmentVariable("MDL_AUTH_CERTIFICATE");
            return string.IsNullOrWhiteSpace(certificatePath)
                ? null
                : new AuthLoginOptions(method, endpoint, tenant, clientId,
                    CertificatePath: certificatePath,
                    CertificatePassword: Environment.GetEnvironmentVariable("MDL_AUTH_CERTIFICATE_PASSWORD"));
        }

        var secret = Environment.GetEnvironmentVariable("MDL_AUTH_CLIENT_SECRET");
        return string.IsNullOrWhiteSpace(secret)
            ? null
            : new AuthLoginOptions(method, endpoint, tenant, clientId, ClientSecret: secret);
    }

    private static string StorageLabel =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "OS keystore (DPAPI)"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "OS keystore (Keychain)"
        : "OS keystore (libsecret)";
}
