using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Entities.Pots;

namespace ExpenseTracker.Domain.Interfaces
{
    /// <summary>
    /// Access to pot rows, scoped by <em>membership</em> instead of by ownership.
    /// <para>
    /// This is not <see cref="IUserRelatedRepository{T}"/> and must never become
    /// it. That one answers "rows this user owns": it filters every read on
    /// <c>UserId == currentUser</c> and throws on an update or delete of anyone
    /// else's row. A pot is read and written by people who did not create the
    /// rows in it, so that shape is not merely too narrow here — its guards are
    /// the wrong assertion.
    /// </para>
    /// <para>
    /// The rule that replaces it: <b>every read is scoped by "pots this user is
    /// currently a member of", and every write additionally asserts the target
    /// row belongs to that pot.</b> A miss on either is indistinguishable from a
    /// row that does not exist — callers turn it into 404, never 403, because
    /// the existence of someone else's pot is itself private.
    /// </para>
    /// </summary>
    public interface IPotRepository
    {
        string CurrentUserId { get; }

        /// <summary>Pots the current user is currently a member of. Untracked.</summary>
        IQueryable<Pot> MemberPots();

        /// <summary>
        /// One pot, tracked, or null when the current user is not a current
        /// member of it — including when no such pot exists at all.
        /// </summary>
        Task<Pot?> FindMemberPotAsync(Guid potId, CancellationToken cancellationToken = default);

        /// <summary>The current user's live membership in that pot, tracked, or null.</summary>
        Task<PotMembership?> FindMyMembershipAsync(Guid potId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Any membership row for this (pot, user) pair, tracked, including one
        /// that has already left. Rejoining reuses it rather than writing a
        /// second, which is what makes redemption idempotent and keeps join
        /// order stable.
        /// </summary>
        Task<PotMembership?> FindMembershipForUserAsync(Guid potId, string userId, CancellationToken cancellationToken = default);

        /// <summary>Members of a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotMembership> MembershipsIn(Guid potId);

        /// <summary>Invites on a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotInvite> InvitesIn(Guid potId);

        /// <summary>The visible history of a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotActivity> ActivityIn(Guid potId);

        /// <summary>
        /// A tracked child row that exists, belongs to <paramref name="potId"/>,
        /// and sits in a pot the current user is a member of. Null if any of the
        /// three fails — the write-side guard, in one place rather than repeated
        /// per entity.
        /// </summary>
        Task<T?> FindInPotAsync<T>(Guid potId, Guid id, CancellationToken cancellationToken = default)
            where T : PotEntity, IPotScoped;

        /// <summary>
        /// The one read that is deliberately not membership-scoped: redeeming an
        /// invite is how someone who is not yet a member becomes one. The code's
        /// entropy is the guard, and nothing about the pot is disclosed except
        /// through <c>IPotService</c>'s deliberately thin preview.
        /// </summary>
        Task<PotInvite?> FindInviteByCodeAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every membership belonging to one user, across every pot, tracked.
        /// The anonymize path, which runs for the user themselves as the first
        /// step of deleting their account.
        /// </summary>
        Task<IReadOnlyList<PotMembership>> MembershipsOfUserAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Pots that user opened, tracked. Anonymizing clears the creator reference.</summary>
        Task<IReadOnlyList<Pot>> PotsCreatedByAsync(string userId, CancellationToken cancellationToken = default);

        void Add<T>(T entity) where T : PotEntity;
        void Update<T>(T entity) where T : PotEntity;

        Task<int> SaveAsync(CancellationToken cancellationToken = default);
        Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}
