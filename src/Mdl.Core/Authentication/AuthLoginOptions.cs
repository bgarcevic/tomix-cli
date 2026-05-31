namespace Mdl.Core.Authentication;

/// <summary>
/// Inputs for a sign-in attempt. The authenticator selects the MSAL flow from
/// <see cref="Method"/>; service-principal flows additionally read the client id,
/// secret and/or certificate fields. <see cref="TargetEndpoint"/> determines the
/// token scope (Power BI vs Azure AS); when null a default Power BI scope is used.
/// </summary>
public sealed record AuthLoginOptions(
    AuthMethod Method,
    string? TargetEndpoint = null,
    string? Tenant = null,
    string? ClientId = null,
    string? ClientSecret = null,
    string? CertificatePath = null,
    string? CertificatePassword = null);
