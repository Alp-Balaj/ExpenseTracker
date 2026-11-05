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

        var match = await _context.McpTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash)
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
            return AuthenticateResult.Fail("Unknown MCP token.");

        var now = DateTime.UtcNow;
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

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        return Task.CompletedTask;
    }
}
