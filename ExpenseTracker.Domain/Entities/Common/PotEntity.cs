namespace ExpenseTracker.Domain.Entities.Common
{
    /// <summary>
    /// The base for every row that belongs to a shared pot rather than to a
    /// person.
    /// <para>
    /// It deliberately does <b>not</b> derive from <see cref="BaseEntity"/> and
    /// carries no <c>UserId</c>. That is the whole point of it:
    /// <c>ApplicationDbContext.ConfigureUserOwnership</c> keys on
    /// <see cref="BaseEntity"/> and gives every match a cascading foreign key to
    /// the Identity user, which for a pot would mean one member's account
    /// deletion silently taking the group's history with it. And
    /// <c>UserRelatedRepository&lt;T&gt;</c> is constrained to
    /// <see cref="BaseEntity"/>, so a pot row cannot even be handed to it by
    /// accident — its reads filter on a single owner and its writes throw on
    /// anyone else's row, which is the exact opposite of what a pot needs.
    /// </para>
    /// <para>
    /// The price is that pot entities get nothing for free. Every index, every
    /// foreign key and every access check is written by hand, and the convention
    /// that stops a user-owned entity from missing its scoping does not protect
    /// them. Access goes through <c>IPotRepository</c>, whose reads are scoped by
    /// "pots this user is currently a member of".
    /// </para>
    /// </summary>
    public abstract class PotEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// A pot row that hangs off one specific pot. Lets the repository's guarded
    /// loader assert "this row exists, and it belongs to that pot" generically
    /// instead of once per entity.
    /// </summary>
    public interface IPotScoped
    {
        Guid PotId { get; }
    }
}
