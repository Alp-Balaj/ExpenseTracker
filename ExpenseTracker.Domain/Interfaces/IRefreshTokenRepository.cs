using ExpenseTracker.Domain.User.JWTFeatures;

namespace ExpenseTracker.Domain.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        Task SaveRefreshTokenAsync(string userId, string refreshToken);
        Task<RefreshToken> GetRefreshTokenAsync(string refreshToken);
        Task DeleteRefreshTokenAsync(string refreshToken);
    }

}