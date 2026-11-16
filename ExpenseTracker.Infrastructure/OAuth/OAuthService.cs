using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Infrastructure.OAuth;

/// <summary>
/// The authorization server in front of /api/mcp, so a client that only speaks OAuth
/// (claude.ai custom connectors) can connect by signing in rather than by pasting a token.
/// <para>
/// OAuth 2.1 as the MCP authorization spec profiles it: authorization code with PKCE
/// S256 only, dynamic client registration (RFC 7591) or a client metadata document,
/// rotating refresh tokens, and resource indicators (RFC 8707). Access tokens are
/// ordinary <see cref="McpToken"/> rows with an expiry, so <c>McpTokenAuthenticationHandler</c>
/// needs no second code path and every connection shows up — and can be revoked — on
/// the same settings screen as a pasted token.
/// </para>
/// </summary>
public sealed class OAuthService
{
    private const string ClientIdPrefix = "etc_";
    private const string ClientSecretPrefix = "et_mcs_";
    private const string CodePrefix = "et_mcc_";
    private const string RefreshPrefix = "et_mcr_";
    private const int MaxRedirectUris = 10;

    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ClientMetadataDocumentFetcher _documents;

    public OAuthService(ApplicationDbContext context, IConfiguration configuration, ClientMetadataDocumentFetcher documents)
    {
        _context = context;
        _configuration = configuration;
        _documents = documents;
    }

    private string Hash(string raw) => TokenUtil.HashRefreshToken(raw, _configuration["RefreshToken:Pepper"]);

    private static string Random(string prefix) =>
        prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    #region Registration (RFC 7591)

    public async Task<(Dictionary<string, object?>? Response, OAuthError? Error)> RegisterAsync(
        JsonElement body, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return (null, OAuthError.InvalidClientMetadata("Expected a JSON object."));

        if (!body.TryGetProperty("redirect_uris", out var urisElement) || urisElement.ValueKind != JsonValueKind.Array)
            return (null, OAuthError.InvalidRedirectUri("redirect_uris is required."));

        var redirectUris = new List<string>();
        foreach (var item in urisElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !RedirectUriRules.IsAcceptable(item.GetString()!))
                return (null, OAuthError.InvalidRedirectUri($"Not an acceptable redirect URI: {item}"));
            redirectUris.Add(item.GetString()!);
        }
        if (redirectUris.Count is 0 or > MaxRedirectUris)
            return (null, OAuthError.InvalidRedirectUri($"Register between 1 and {MaxRedirectUris} redirect URIs."));

        // RFC 7591 §2: the default is client_secret_basic. Anything we cannot honour is
        // refused rather than silently downgraded, so the client knows.
        var authMethod = body.TryGetProperty("token_endpoint_auth_method", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()!
            : "client_secret_basic";
        if (authMethod is not ("none" or "client_secret_post" or "client_secret_basic"))
            return (null, OAuthError.InvalidClientMetadata($"token_endpoint_auth_method {authMethod} is not supported."));

        if (body.TryGetProperty("grant_types", out var grants) && grants.ValueKind == JsonValueKind.Array &&
            grants.EnumerateArray().Any(g => g.GetString() is not ("authorization_code" or "refresh_token")))
            return (null, OAuthError.InvalidClientMetadata("Only authorization_code and refresh_token are supported."));

        var name = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
            ? n.GetString()!.Trim()
            : "MCP client";
        if (name.Length > 100) name = name[..100];

        var clientId = ClientIdPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        string? secret = authMethod == "none" ? null : Random(ClientSecretPrefix);

        var client = new OAuthClient
        {
            ClientId = clientId,
            ClientName = name,
            RedirectUris = redirectUris,
            ClientSecretHash = secret is null ? null : Hash(secret)
        };
        _context.OAuthClients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["client_id_issued_at"] = new DateTimeOffset(client.CreatedAt).ToUnixTimeSeconds(),
            ["client_name"] = name,
            ["redirect_uris"] = redirectUris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = authMethod,
        };
        if (secret is not null)
        {
            response["client_secret"] = secret;
            response["client_secret_expires_at"] = 0; // never
        }
        return (response, null);
    }

    #endregion

    #region Client resolution

    /// <summary>
    /// A registered client, or — when the client_id is an https URL — the client
    /// described by the metadata document at that URL (draft-ietf-oauth-client-id-metadata-document).
    /// </summary>
    public async Task<ResolvedClient?> ResolveClientAsync(string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;

        if (clientId.StartsWith("https://", StringComparison.Ordinal))
            return await _documents.FetchAsync(clientId, cancellationToken);

        var row = await _context.OAuthClients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
        return row is null ? null : new ResolvedClient(row.ClientId, row.ClientName, row.RedirectUris, row.ClientSecretHash);
    }

    /// <summary>
    /// Client authentication at the token and revocation endpoints (RFC 6749 §2.3.1).
    /// A confidential client must present its secret; a public one is identified by
    /// client_id alone, and PKCE (or possession of the refresh token) does the rest.
    /// </summary>
    public async Task<(ResolvedClient? Client, OAuthError? Error)> AuthenticateClientAsync(
        string? basicHeader, string? formClientId, string? formClientSecret, CancellationToken cancellationToken)
    {
        string? clientId = formClientId, secret = formClientSecret;

        if (basicHeader is not null)
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(basicHeader));
                var colon = decoded.IndexOf(':');
                if (colon < 0) return (null, OAuthError.InvalidClient("Malformed Basic credentials."));
                clientId = Uri.UnescapeDataString(decoded[..colon].Replace('+', ' '));
                secret = Uri.UnescapeDataString(decoded[(colon + 1)..].Replace('+', ' '));
            }
            catch (FormatException)
            {
                return (null, OAuthError.InvalidClient("Malformed Basic credentials."));
            }
        }

        if (string.IsNullOrEmpty(clientId))
            return (null, OAuthError.InvalidClient("client_id is required."));

        // Token requests never fetch a metadata document: everything they need was
        // bound into the code or the connection when the user approved it.
        if (clientId.StartsWith("https://", StringComparison.Ordinal))
            return (new ResolvedClient(clientId, clientId, Array.Empty<string>(), null), null);

        var client = await ResolveClientAsync(clientId, cancellationToken);
        if (client is null) return (null, OAuthError.InvalidClient("Unknown client."));

        if (client.SecretHash is not null)
        {
            if (string.IsNullOrEmpty(secret) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(secret)), Encoding.UTF8.GetBytes(client.SecretHash)))
                return (null, OAuthError.InvalidClient("Client authentication failed."));
        }

        return (client, null);
    }

    #endregion

    #region Authorization

    /// <summary>
    /// Checks an authorization request before the consent screen is shown.
    /// <para>
    /// RFC 6749 §4.1.2.1 splits errors in two: if the client or redirect URI is bad
    /// the user must be told and nobody redirected (or this becomes an open redirect);
    /// anything else is reported to the client by redirecting back with ?error=.
    /// </para>
    /// </summary>
    public async Task<(AuthorizeRequestInfo? Info, string? FatalError, ResolvedClient? Client)> ValidateAsync(
        AuthorizeParameters p, string issuer, string resource, CancellationToken cancellationToken)
    {
        var client = await ResolveClientAsync(p.ClientId, cancellationToken);
        if (client is null)
            return (null, "This app is not registered with ExpenseTracker, or its registration could not be read.", null);

        if (string.IsNullOrEmpty(p.RedirectUri) || !RedirectUriRules.Matches(client.RedirectUris, p.RedirectUri))
            return (null, "This app asked to send you to an address it did not register.", null);

        string? error = null, description = null;
        if (p.ResponseType != "code")
            (error, description) = ("unsupported_response_type", "Only response_type=code is supported.");
        else if (string.IsNullOrEmpty(p.CodeChallenge) || p.CodeChallengeMethod != "S256")
            (error, description) = ("invalid_request", "PKCE with code_challenge_method=S256 is required.");
        else if (p.CodeChallenge.Length is < 43 or > 128)
            (error, description) = ("invalid_request", "code_challenge is malformed.");
        else if (!ScopeRules.TryParse(p.Scope, out _))
            (error, description) = ("invalid_scope", $"Supported scopes: {OAuthDefaults.ScopeRead} {OAuthDefaults.ScopeWrite}.");
        else if (!ResourceMatches(p.Resource, resource))
            (error, description) = ("invalid_target", $"This server is the resource {resource}.");

        if (error is not null)
            return (new AuthorizeRequestInfo { ErrorRedirectUrl = BuildRedirect(p.RedirectUri, issuer, p.State, ("error", error), ("error_description", description!)) }, null, client);

        ScopeRules.TryParse(p.Scope, out var requestedWrite);
        return (new AuthorizeRequestInfo
        {
            ClientName = client.Name,
            RedirectHost = new Uri(p.RedirectUri).Host,
            RequestedWrite = requestedWrite
        }, null, client);
    }

    /// <summary>
    /// Records the user's decision and returns where to send the browser. Validates
    /// again from scratch: the SPA is a convenience, not a gatekeeper.
    /// </summary>
    public async Task<(string? RedirectUrl, string? FatalError)> DecideAsync(
        string userId, AuthorizeDecision d, string issuer, string resource, CancellationToken cancellationToken)
    {
        var (info, fatal, client) = await ValidateAsync(d, issuer, resource, cancellationToken);
        if (fatal is not null) return (null, fatal);
        if (info!.ErrorRedirectUrl is not null) return (info.ErrorRedirectUrl, null);

        if (!d.Approve)
            return (BuildRedirect(d.RedirectUri!, issuer, d.State, ("error", "access_denied"), ("error_description", "The user declined.")), null);

        var code = Random(CodePrefix);
        _context.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
        {
            UserId = userId,
            CodeHash = Hash(code),
            ClientId = client!.ClientId,
            ClientName = client.Name,
            RedirectUri = d.RedirectUri!,
            CodeChallenge = d.CodeChallenge!,
            CanWrite = d.CanWrite,
            ExpiresAtUtc = DateTime.UtcNow + OAuthDefaults.AuthorizationCodeLifetime
        });
        await _context.SaveChangesAsync(cancellationToken);

        return (BuildRedirect(d.RedirectUri!, issuer, d.State, ("code", code)), null);
    }

    /// <summary>Appends to whatever query the registered URI already has; adds iss (RFC 9207).</summary>
    private static string BuildRedirect(string redirectUri, string issuer, string? state, params (string Key, string Value)[] values)
    {
        var query = new Dictionary<string, string?>();
        foreach (var (k, v) in values) query[k] = v;
        if (!string.IsNullOrEmpty(state)) query["state"] = state;
        query["iss"] = issuer;
        return QueryHelpers.AddQueryString(redirectUri, query);
    }

    /// <summary>A missing resource is fine (it can only mean this server); a different one is not.</summary>
    private static bool ResourceMatches(string? requested, string resource) =>
        string.IsNullOrEmpty(requested) ||
        string.Equals(requested.TrimEnd('/'), resource.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Token endpoint

    public async Task<(TokenResponse? Response, OAuthError? Error)> ExchangeCodeAsync(
        ResolvedClient client, string? code, string? redirectUri, string? codeVerifier, string? resource,
        string ownResource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(redirectUri))
            return (null, OAuthError.InvalidRequest("code, redirect_uri and code_verifier are required."));
        if (!ResourceMatches(resource, ownResource))
            return (null, OAuthError.InvalidTarget($"This server is the resource {ownResource}."));

        var hash = Hash(code);
        var row = await _context.OAuthAuthorizationCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CodeHash == hash, cancellationToken);
        if (row is null) return (null, OAuthError.InvalidGrant("Unknown or already used code."));

        // Burn it first. Of two concurrent redemptions only one DELETE removes the row,
        // and a code that fails a check below is spent too, as RFC 6749 §4.1.2 wants.
        var burned = await _context.OAuthAuthorizationCodes.Where(c => c.Id == row.Id).ExecuteDeleteAsync(cancellationToken);
        if (burned == 0) return (null, OAuthError.InvalidGrant("Unknown or already used code."));

        if (row.ExpiresAtUtc <= DateTime.UtcNow) return (null, OAuthError.InvalidGrant("The code has expired."));
        if (row.ClientId != client.ClientId) return (null, OAuthError.InvalidGrant("The code was issued to another client."));
        if (row.RedirectUri != redirectUri) return (null, OAuthError.InvalidGrant("redirect_uri does not match the authorization request."));
        if (!PkceMatches(codeVerifier, row.CodeChallenge)) return (null, OAuthError.InvalidGrant("PKCE verification failed."));

        return (await CreateConnectionAsync(row), null);
    }

    private async Task<TokenResponse> CreateConnectionAsync(OAuthAuthorizationCode grant)
    {
        var now = DateTime.UtcNow;

        // Reconnecting the same client replaces its old connection instead of piling up
        // a new row per sign-in; lapsed connections are swept while we are here.
        await _context.McpTokens
            .Where(t => t.UserId == grant.UserId && t.OAuthClientId != null &&
                        (t.OAuthClientId == grant.ClientId || t.RefreshExpiresAtUtc <= now))
            .ExecuteDeleteAsync();

        var excess = await _context.McpTokens
            .Where(t => t.UserId == grant.UserId && t.OAuthClientId != null)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(OAuthDefaults.MaxConnectionsPerUser - 1)
            .Select(t => t.Id)
            .ToListAsync();
        if (excess.Count > 0)
            await _context.McpTokens.Where(t => excess.Contains(t.Id)).ExecuteDeleteAsync();

        var access = TokenUtil.GenerateMcpToken();
        var refresh = Random(RefreshPrefix);
        _context.McpTokens.Add(new McpToken
        {
            UserId = grant.UserId,
            Name = grant.ClientName,
            TokenHash = Hash(access),
            LastFour = access[^4..],
            CanWrite = grant.CanWrite,
            OAuthClientId = grant.ClientId,
            ExpiresAtUtc = now + OAuthDefaults.AccessTokenLifetime,
            RefreshTokenHash = Hash(refresh),
            RefreshExpiresAtUtc = now + OAuthDefaults.RefreshTokenLifetime
        });
        await _context.SaveChangesAsync();

        return Response(access, refresh, grant.CanWrite);
    }

    public async Task<(TokenResponse? Response, OAuthError? Error)> RefreshAsync(
        ResolvedClient client, string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return (null, OAuthError.InvalidRequest("refresh_token is required."));

        var now = DateTime.UtcNow;
        var oldHash = Hash(refreshToken);
        var row = await _context.McpTokens.AsNoTracking()
            .Where(t => t.RefreshTokenHash == oldHash && t.RefreshExpiresAtUtc > now)
            .Select(t => new { t.Id, t.OAuthClientId, t.CanWrite })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.OAuthClientId != client.ClientId)
            return (null, OAuthError.InvalidGrant("Unknown, expired or revoked refresh token."));

        // Rotation (OAuth 2.1 §4.3.1) as a compare-and-swap on the old hash: of two
        // concurrent refreshes with the same token, exactly one wins.
        var access = TokenUtil.GenerateMcpToken();
        var refresh = Random(RefreshPrefix);
        var newAccessHash = Hash(access);
        var newRefreshHash = Hash(refresh);
        var updated = await _context.McpTokens
            .Where(t => t.Id == row.Id && t.RefreshTokenHash == oldHash)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.TokenHash, newAccessHash)
                .SetProperty(t => t.LastFour, access.Substring(access.Length - 4))
                .SetProperty(t => t.ExpiresAtUtc, now + OAuthDefaults.AccessTokenLifetime)
                .SetProperty(t => t.RefreshTokenHash, newRefreshHash)
                .SetProperty(t => t.RefreshExpiresAtUtc, now + OAuthDefaults.RefreshTokenLifetime)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        if (updated == 0) return (null, OAuthError.InvalidGrant("The refresh token was already used."));

        return (Response(access, refresh, row.CanWrite), null);
    }

    /// <summary>RFC 7009: always succeeds from the client's point of view.</summary>
    public async Task RevokeAsync(ResolvedClient client, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token)) return;
        var hash = Hash(token);
        await _context.McpTokens
            .Where(t => t.OAuthClientId == client.ClientId && (t.TokenHash == hash || t.RefreshTokenHash == hash))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static TokenResponse Response(string access, string refresh, bool canWrite) => new()
    {
        AccessToken = access,
        RefreshToken = refresh,
        ExpiresIn = (int)OAuthDefaults.AccessTokenLifetime.TotalSeconds,
        Scope = canWrite ? $"{OAuthDefaults.ScopeRead} {OAuthDefaults.ScopeWrite}" : OAuthDefaults.ScopeRead
    };

    /// <summary>RFC 7636 §4.6: BASE64URL(SHA256(ASCII(verifier))) == challenge.</summary>
    internal static bool PkceMatches(string verifier, string challenge)
    {
        if (verifier.Length is < 43 or > 128) return false;
        var computed = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    #endregion
}

internal static class ScopeRules
{
    /// <summary>Absent or empty means read-only. Unknown scopes are an error rather than ignored.</summary>
    public static bool TryParse(string? scope, out bool write)
    {
        write = false;
        if (string.IsNullOrWhiteSpace(scope)) return true;
        foreach (var s in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (s == OAuthDefaults.ScopeWrite) write = true;
            else if (s != OAuthDefaults.ScopeRead) return false;
        }
        return true;
    }
}

public static class RedirectUriRules
{
    /// <summary>
    /// https anywhere; http only to a loopback address (native apps, RFC 8252 §7.3);
    /// private-use schemes like cursor:// (§7.1). Never a fragment, never a scheme a
    /// browser would execute.
    /// </summary>
    public static bool IsAcceptable(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.Fragment)) return false;
        return u.Scheme switch
        {
            "https" => true,
            "http" => u.IsLoopback,
            "javascript" or "data" or "file" or "vbscript" or "blob" or "about" or "ftp" or "ws" or "wss" => false,
            _ => u.Scheme.Contains('.') || u.Scheme.Length > 2 // reverse-DNS or app-named private scheme
        };
    }

    /// <summary>Exact match, except that a loopback redirect may use any port (RFC 8252 §7.3).</summary>
    public static bool Matches(IReadOnlyList<string> registered, string requested)
    {
        if (registered.Contains(requested, StringComparer.Ordinal)) return true;
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var req) || req.Scheme != "http" || !req.IsLoopback) return false;

        return registered.Any(r =>
            Uri.TryCreate(r, UriKind.Absolute, out var reg) && reg.Scheme == "http" && reg.IsLoopback &&
            reg.Host == req.Host && reg.PathAndQuery == req.PathAndQuery);
    }
}
