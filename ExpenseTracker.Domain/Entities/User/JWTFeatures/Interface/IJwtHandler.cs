namespace ExpenseTracker.Domain.User.JWTFeatures.Interface
{
    public interface IJwtHandler
    {
        public Task<(string, RefreshToken)> CreateTokenAsync(User user, IList<string> roles, string? testRefreshToken);
    }
}
