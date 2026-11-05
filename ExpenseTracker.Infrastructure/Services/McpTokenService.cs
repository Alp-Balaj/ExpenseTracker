using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IMcpTokenService"/>
public sealed class McpTokenService : IMcpTokenService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _currentUser;
    private readonly IConfiguration _configuration;

    public McpTokenService(ApplicationDbContext context, ICurrentUserServices currentUser, IConfiguration configuration)
    {
        _context = context;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<McpTokenDTO>> ListAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        return await _context.McpTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new McpTokenDTO
            {
                Id = t.Id,
                Name = t.Name,
                LastFour = t.LastFour,
                CanWrite = t.CanWrite,
                CreatedAt = t.CreatedAt,
                LastUsedAtUtc = t.LastUsedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CreatedMcpTokenDTO?> CreateAsync(CreateMcpTokenDTO dto, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var held = await _context.McpTokens.CountAsync(t => t.UserId == userId, cancellationToken);
        if (held >= McpTokenLimits.MaxTokensPerUser) return null;

        var raw = TokenUtil.GenerateMcpToken();
        var token = new McpToken
        {
            UserId = userId,
            Name = dto.Name.Trim(),
            TokenHash = TokenUtil.HashRefreshToken(raw, _configuration["RefreshToken:Pepper"]),
            LastFour = raw[^4..],
            CanWrite = dto.CanWrite
        };

        _context.McpTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreatedMcpTokenDTO
        {
            Id = token.Id,
            Name = token.Name,
            LastFour = token.LastFour,
            CanWrite = token.CanWrite,
            CreatedAt = token.CreatedAt,
            Token = raw
        };
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var removed = await _context.McpTokens
            .Where(t => t.Id == id && t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }

    public Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.McpTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}
