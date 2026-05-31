using Mdl.App.Config;
using Mdl.Auth;
using Mdl.Core.Configuration;

namespace Mdl.Cli.Commands;

/// <summary>
/// Resolves Azure AD client settings and builds the shared <see cref="MsalAuthenticator"/>.
/// Precedence: explicit override → environment variable → local config → built-in default.
/// Both the <c>auth</c> command and the remote TOM token provider go through here so they
/// agree on the client id (silent token refresh requires the same app that signed in).
/// </summary>
internal static class AuthSettingsFactory
{
    public static MsalAuthenticator CreateAuthenticator(string? clientIdOverride = null, string? tenant = null)
        => new(Resolve(clientIdOverride, tenant), messageWriter: Console.Error.WriteLine);

    public static MsalAuthSettings Resolve(string? clientIdOverride = null, string? tenant = null)
    {
        var config = new MdlConfigStore().Load();

        var clientId = FirstNonEmpty(
            clientIdOverride,
            Environment.GetEnvironmentVariable("MDL_AUTH_CLIENT_ID"),
            ConfigValue(config, ConfigKeys.AuthClientId),
            MsalAuthSettings.DefaultClientId)!;

        var authority = ConfigValue(config, ConfigKeys.AuthAuthority);
        if (string.IsNullOrWhiteSpace(authority))
        {
            var resolvedTenant = FirstNonEmpty(
                tenant,
                Environment.GetEnvironmentVariable("MDL_AUTH_TENANT"),
                ConfigValue(config, ConfigKeys.AuthTenant));

            authority = string.IsNullOrWhiteSpace(resolvedTenant)
                ? MsalAuthSettings.DefaultAuthority
                : $"https://login.microsoftonline.com/{resolvedTenant}";
        }

        return new MsalAuthSettings(clientId, authority);
    }

    private static string? ConfigValue(IDictionary<string, string> config, string key)
        => config.TryGetValue(key, out var value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
