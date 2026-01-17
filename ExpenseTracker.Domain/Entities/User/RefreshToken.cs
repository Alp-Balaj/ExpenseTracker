using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.User
{
    public class RefreshToken : BaseEntity
    {
        public string TokenHash { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? ReplacedByTokenHash { get; set; }

        public bool IsActive => RevokedAtUtc == null && ExpiresAtUtc > DateTime.UtcNow;
    }
}