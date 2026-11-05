using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>The caller's MCP tokens, managed from the settings screen.</summary>
    public interface IMcpTokenService
    {
        Task<IReadOnlyList<McpTokenDTO>> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Null when the caller already holds the maximum number of tokens.</summary>
        Task<CreatedMcpTokenDTO?> CreateAsync(CreateMcpTokenDTO dto, CancellationToken cancellationToken = default);

        /// <returns>False when the caller owns no token with that id.</returns>
        Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Takes a user id rather than reading the current user: password reset
        /// runs anonymously, and it is the moment this matters most.
        /// </summary>
        Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);
    }
}
