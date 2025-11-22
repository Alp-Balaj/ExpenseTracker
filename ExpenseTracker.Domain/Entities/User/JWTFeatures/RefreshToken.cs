using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.User.JWTFeatures
{
    public class RefreshToken : BaseEntity
    {
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

}
