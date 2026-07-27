namespace ExpenseTracker.Domain.Entities.User;

// One row per user, keyed by UserId — there is no surrogate id (Sugg-10 moved
// this off MongoDB, where it was a single two-field document per user).
// Deliberately not a BaseEntity: that carries a Guid Id the table has no use for.
public class UserPreferences
{
    public string UserId { get; set; } = null!;
    public string Theme { get; set; } = "system";
    public string BaseCurrency { get; set; } = "EUR";
}
