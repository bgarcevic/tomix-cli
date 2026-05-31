using System.CommandLine;
using Mdl.App.Auth;
using Mdl.App.State;
using Mdl.Auth;
using Mdl.Cli.Output;
using Mdl.Core.Authentication;

namespace Mdl.Cli.Commands;

/// <summary>
/// <c>auth login | status | logout</c> — browser/device-code/service-principal sign-in to
/// Power BI, Fabric, or Azure Analysis Services, caching the token in the OS keystore.
/// </summary>
internal sealed class AuthCommand : ICommandModule
{
    public Command Build()
    {
        var command = new Command("auth", "Manage authentication for remote workspaces");
        command.Subcommands.Add(BuildLogin());
        command.Subcommands.Add(BuildStatus());
        command.Subcommands.Add(BuildLogout());
        return command;
    }

    private static Command BuildLogin()
    {
        var serverArgument = new Argument<string?>("server")
        {
            Description = "Target endpoint (powerbi://..., asazure://...). Defaults to the active connection.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var usernameOption = new Option<string?>("--username") { Description = "Service-principal application (client) id" };
        usernameOption.Aliases.Add("-u");
        var passwordOption = new Option<string?>("--password") { Description = "Service-principal client secret" };
        passwordOption.Aliases.Add("-p");
        var tenantOption = new Option<string?>("--tenant") { Description = "Tenant id or domain (required for service principal)" };
        tenantOption.Aliases.Add("-t");
        var identityOption = new Option<bool>("--identity") { Description = "Sign in with a managed identity (Azure-hosted)" };
        identityOption.Aliases.Add("-I");
        var certificateOption = new Option<string?>("--certificate") { Description = "Path to a service-principal certificate (.pfx)" };
        var certificatePasswordOption = new Option<string?>("--certificate-password") { Description = "Password for the certificate file" };
        var deviceCodeOption = new Option<bool>("--device-code") { Description = "Use the device-code flow instead of a local browser" };
        var clientIdOption = new Option<string?>("--client-id") { Description = "Override the Azure AD client id used for interactive/device-code sign-in" };

        var command = new Command("login", "Log in to a Power BI / Fabric / Azure AS account")
        {
            serverArgument,
            usernameOption,
            passwordOption,
            tenantOption,
            identityOption,
            certificateOption,
            certificatePasswordOption,
            deviceCodeOption,
            clientIdOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var tenant = parseResult.GetValue(tenantOption);
            var certificate = parseResult.GetValue(certificateOption);
            var useIdentity = parseResult.GetValue(identityOption);
            var useDeviceCode = parseResult.GetValue(deviceCodeOption);

            var method = ResolveMethod(useIdentity, certificate, username, password, useDeviceCode);
            var endpoint = parseResult.GetValue(serverArgument)
                ?? new CliStateStore().LoadCurrentSession()?.Server;

            var options = new AuthLoginOptions(
                method,
                TargetEndpoint: endpoint,
                Tenant: tenant,
                ClientId: IsServicePrincipal(method) ? username : null,
                ClientSecret: password,
                CertificatePath: certificate,
                CertificatePassword: parseResult.GetValue(certificatePasswordOption));

            var authenticator = CreateAuthenticator(parseResult.GetValue(clientIdOption), tenant);
            var handler = new AuthHandler(authenticator);

            if (method == AuthMethod.Interactive && !OutputFormats.IsJson(format))
                Console.Error.WriteLine("Opening browser for authentication...");

            var result = await handler.LoginAsync(options, cancellationToken);
            return CommandOutput.Render(result, format, RenderLogin, data => data.Identity);
        });

        return command;
    }

    private static Command BuildStatus()
    {
        var command = new Command("status", "Show authentication status");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var handler = new AuthHandler(CreateAuthenticator(clientIdOverride: null, tenant: null));
            var result = await handler.StatusAsync(cancellationToken);
            return CommandOutput.Render(result, format, RenderStatus);
        });
        return command;
    }

    private static Command BuildLogout()
    {
        var command = new Command("logout", "Clear cached authentication credentials");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = GlobalOptions.OutputFormatValue(parseResult);
            if (!CommandOutput.TryValidateFormat(format))
                return 2;

            var handler = new AuthHandler(CreateAuthenticator(clientIdOverride: null, tenant: null));
            var result = await handler.LogoutAsync(cancellationToken);
            return CommandOutput.Render(
                result,
                format,
                data => Console.WriteLine(data.Existed ? "Logged out." : "Not logged in."));
        });
        return command;
    }

    private static AuthMethod ResolveMethod(bool useIdentity, string? certificate, string? username, string? password, bool useDeviceCode)
    {
        if (useIdentity)
            return AuthMethod.ManagedIdentity;
        if (!string.IsNullOrWhiteSpace(certificate))
            return AuthMethod.ServicePrincipalCertificate;
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            return AuthMethod.ServicePrincipalSecret;
        return useDeviceCode ? AuthMethod.DeviceCode : AuthMethod.Interactive;
    }

    private static bool IsServicePrincipal(AuthMethod method)
        => method is AuthMethod.ServicePrincipalSecret or AuthMethod.ServicePrincipalCertificate;

    private static MsalAuthenticator CreateAuthenticator(string? clientIdOverride, string? tenant)
        => AuthSettingsFactory.CreateAuthenticator(clientIdOverride, tenant);

    private static void RenderLogin(AuthLoginResult result)
    {
        Console.WriteLine("Authenticated");
        RenderIdentity(result.Identity, includeMethodAndStorage: false);
    }

    private static void RenderStatus(AuthStatusResult result)
    {
        if (!result.LoggedIn || result.Identity is null)
        {
            Console.WriteLine("Not logged in");
            Console.WriteLine("Run 'mdl auth login' to authenticate.");
            return;
        }

        Console.WriteLine("Logged in");
        RenderIdentity(result.Identity, includeMethodAndStorage: true);
    }

    private static void RenderIdentity(AuthIdentity identity, bool includeMethodAndStorage)
    {
        Console.WriteLine($"  Account:  {identity.Username}");
        Console.WriteLine($"  Tenant:   {identity.TenantId ?? ""}");
        if (includeMethodAndStorage)
        {
            Console.WriteLine($"  Method:   {MethodLabel(identity.Method)}");
            Console.WriteLine($"  Storage:  {identity.Storage}");
        }

        if (identity.ExpiresOn is { } expires)
            Console.WriteLine($"  Expires:  {expires.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    }

    private static string MethodLabel(AuthMethod method) => method switch
    {
        AuthMethod.Interactive => "Interactive",
        AuthMethod.DeviceCode => "Device code",
        AuthMethod.ServicePrincipalSecret => "Service principal",
        AuthMethod.ServicePrincipalCertificate => "Service principal (certificate)",
        AuthMethod.ManagedIdentity => "Managed identity",
        _ => method.ToString()
    };
}
