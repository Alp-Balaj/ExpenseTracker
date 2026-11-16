using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Infrastructure.OAuth;

/// <summary>An RFC 6749 §5.2 error. Status is 400 except for invalid_client (401).</summary>
public sealed record OAuthError(string Error, string Description, int Status = 400)
{
    public static OAuthError InvalidRequest(string d) => new("invalid_request", d);
    public static OAuthError InvalidClient(string d) => new("invalid_client", d, 401);
    public static OAuthError InvalidGrant(string d) => new("invalid_grant", d);
    public static OAuthError InvalidScope(string d) => new("invalid_scope", d);
    public static OAuthError InvalidTarget(string d) => new("invalid_target", d);
    public static OAuthError UnsupportedGrantType(string d) => new("unsupported_grant_type", d);
    public static OAuthError InvalidRedirectUri(string d) => new("invalid_redirect_uri", d);
    public static OAuthError InvalidClientMetadata(string d) => new("invalid_client_metadata", d);
}

/// <summary>
/// The authorization request's parameters. Bound from the query string when the SPA
/// asks whether a request is valid, and from the JSON body (same snake_case names)
/// when it posts the user's decision.
/// </summary>
public class AuthorizeParameters
{
    [FromQuery(Name = "response_type"), JsonPropertyName("response_type")] public string? ResponseType { get; set; }
    [FromQuery(Name = "client_id"), JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [FromQuery(Name = "redirect_uri"), JsonPropertyName("redirect_uri")] public string? RedirectUri { get; set; }
    [FromQuery(Name = "state"), JsonPropertyName("state")] public string? State { get; set; }
    [FromQuery(Name = "scope"), JsonPropertyName("scope")] public string? Scope { get; set; }
    [FromQuery(Name = "code_challenge"), JsonPropertyName("code_challenge")] public string? CodeChallenge { get; set; }
    [FromQuery(Name = "code_challenge_method"), JsonPropertyName("code_challenge_method")] public string? CodeChallengeMethod { get; set; }
    [FromQuery(Name = "resource"), JsonPropertyName("resource")] public string? Resource { get; set; }
}

public sealed class AuthorizeDecision : AuthorizeParameters
{
    [JsonPropertyName("approve")] public bool Approve { get; set; }

    /// <summary>The consent screen's checkbox. Ignored unless approving.</summary>
    [JsonPropertyName("can_write")] public bool CanWrite { get; set; }
}

/// <summary>What the consent screen shows. Exactly one of the two shapes is filled.</summary>
public sealed class AuthorizeRequestInfo
{
    [JsonPropertyName("client_name")] public string? ClientName { get; init; }

    /// <summary>Where the browser goes afterwards — the one part of the client's identity that is not self-declared.</summary>
    [JsonPropertyName("redirect_host")] public string? RedirectHost { get; init; }

    [JsonPropertyName("requested_write")] public bool RequestedWrite { get; init; }

    /// <summary>
    /// Set when the request is malformed but the client and redirect URI are genuine,
    /// so the error belongs to the client (RFC 6749 §4.1.2.1): the SPA just goes there.
    /// </summary>
    [JsonPropertyName("error_redirect_url")] public string? ErrorRedirectUrl { get; init; }
}

/// <summary>A client resolved from either a registration row or a metadata document.</summary>
public sealed record ResolvedClient(string ClientId, string Name, IReadOnlyList<string> RedirectUris, string? SecretHash);

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")] public required string AccessToken { get; init; }
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "Bearer";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("refresh_token")] public required string RefreshToken { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
}
