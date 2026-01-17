using ExpenseTracker.Domain.Entities.User;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task AddAsync(RefreshToken token);
        Task<RefreshToken?> GetByHashAsync(string tokenHash);
        Task RevokeAsync(RefreshToken token, string? replacedByTokenHash = null);
        Task RevokeAllActiveForUserAsync(string userId);
    }
}