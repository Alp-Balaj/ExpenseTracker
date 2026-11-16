using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ExpenseTracker.Infrastructure.Security;

/// <summary>Constants and URLs shared by the OAuth endpoints and the MCP auth handler.</summary>
public static class OAuthDefaults
{
    public const string ScopeRead = "mcp:read";
    public const string ScopeWrite = "mcp:write";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    /// <summary>Sliding: a connection unused for this long has to sign in again.</summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(60);
    public static readonly TimeSpan AuthorizationCodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>OAuth connections per user, on top of the pasted-token cap. Oldest goes first.</summary>
    public const int MaxConnectionsPerUser = 20;

    /// <summary>The browser-facing consent page. A SPA route, not an API endpoint.</summary>
    public const string AuthorizePagePath = "/oauth/authorize";
    public const string TokenPath = "/api/oauth/token";
    public const string RegisterPath = "/api/oauth/register";
    public const string RevokePath = "/api/oauth/revoke";
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";

    /// <summary>
    /// The origin clients see, which is both the issuer and the base of every URL in
    /// the metadata. OAuth:PublicOrigin pins it; otherwise it comes from the request,
    /// whose scheme Program.cs corrects from Railway's X-Forwarded-Proto.
    /// </summary>
    public static string PublicOrigin(HttpRequest request, IConfiguration configuration)
    {
        var configured = configuration["OAuth:PublicOrigin"];
        return !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";
    }

    /// <summary>The resource indicator (RFC 8707) for the MCP endpoint.</summary>
    public static string Resource(HttpRequest request, IConfiguration configuration) =>
        PublicOrigin(request, configuration) + McpAuthDefaults.Path;

    /// <summary>Path-suffixed form (RFC 9728 §3.1), which is what a 401 points at.</summary>
    public static string ResourceMetadataUrl(HttpRequest request, IConfiguration configuration) =>
        PublicOrigin(request, configuration) + ProtectedResourceMetadataPath + McpAuthDefaults.Path;
}
