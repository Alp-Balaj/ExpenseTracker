using System.Security.Claims;
using System.Text.Encodings.Web;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ExpenseTracker.Infrastructure.Security;

public static class McpAuthDefaults
{
    public const string Scheme = "McpToken";

    /// <summary>The one path this scheme answers for. See JWTServices.</summary>
    public const string Path = "/api/mcp";

    /// <summary>Present on the principal only when the token may use the write tools.</summary>
    public const string WriteClaim = "mcp_write";
}

/// <summary>
/// Turns an MCP bearer token into the same NameIdentifier principal a JWT produces,
/// so ICurrentUserServices and every user-scoped repository work unchanged.
/// </summary>
public sealed class McpTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Keeps "last used" useful without a write on every tool call.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromHours(1);

    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public McpTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext context,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _context = context;
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string bearer = "Bearer ";

        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header[bearer.Length..].Trim();
        if (!raw.StartsWith(TokenUtil.McpTokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.Fail("Not an MCP token.");

        var hash = TokenUtil.HashRefreshToken(raw, _configuration["RefreshToken:Pepper"]);
        var cancellationToken = Context.RequestAborted;

        var now = DateTime.UtcNow;
        var match = await _context.McpTokens.AsNoTracking()
            // ExpiresAtUtc is null for pasted tokens, which never expire.
            .Where(t => t.TokenHash == hash && (t.ExpiresAtUtc == null || t.ExpiresAtUtc > now))
            .Join(_context.Users, t => t.UserId, u => u.Id, (t, u) => new
            {
                t.Id,
                t.UserId,
                t.CanWrite,
                t.LastUsedAtUtc,
                u.UserName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
            return AuthenticateResult.Fail("Unknown or expired MCP token.");

        if (match.LastUsedAtUtc is null || match.LastUsedAtUtc <= now - LastUsedResolution)
        {
            await _context.McpTokens
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.LastUsedAtUtc, now), cancellationToken);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, match.UserId),
            new(ClaimTypes.Name, match.UserName ?? string.Empty)
        };
        if (match.CanWrite) claims.Add(new Claim(McpAuthDefaults.WriteClaim, "true"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// Points the client at the protected-resource metadata (RFC 9728 §5.1). That is
    /// how an OAuth client such as claude.ai discovers where to sign in; a client
    /// holding a pasted token ignores it. A token that was presented but rejected
    /// gets error="invalid_token", which tells an OAuth client to refresh.
    /// </summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var metadata = OAuthDefaults.ResourceMetadataUrl(Request, _configuration);
        var challenge = $"Bearer resource_metadata=\"{metadata}\"";

        var result = await HandleAuthenticateOnceSafeAsync();
        if (result.Failure is not null)
            challenge += ", error=\"invalid_token\"";

        Response.StatusCode = 401;
        Response.Headers[HeaderNames.WWWAuthenticate] = challenge;
    }
}
