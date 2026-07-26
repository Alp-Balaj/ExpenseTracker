using ExpenseTracker.Domain.Entities.User;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IRefreshTokenRepository
    {
        void Add(RefreshToken token);
        Task<RefreshToken?> GetByHashAsync(string tokenHash);
        void Revoke(RefreshToken token, string? replacedByTokenHash = null);
        Task RevokeAllActiveForUserAsync(string userId);
        Task SaveAsync();
    }
}
