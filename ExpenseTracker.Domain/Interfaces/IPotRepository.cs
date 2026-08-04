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

        // ── W2, the ledger ─────────────────────────────────────────────────────

        /// <summary>Top-ups into a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotTopUp> TopUpsIn(Guid potId);

        /// <summary>Expenses of a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotExpense> ExpensesIn(Guid potId);

        /// <summary>Every share written in a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotExpenseShare> SharesIn(Guid potId);

        /// <summary>Withdrawals out of a pot the user belongs to. Empty for a non-member.</summary>
        IQueryable<PotWithdrawal> WithdrawalsIn(Guid potId);

        /// <summary>
        /// One expense with its shares loaded and tracked, or null when it is not
        /// in a pot the caller is a member of. Deleting reverses the pot's debit
        /// and every share in one transaction, so both have to arrive together.
        /// </summary>
        Task<PotExpense?> FindExpenseWithSharesAsync(
            Guid potId, Guid expenseId, CancellationToken cancellationToken = default);

        /// <summary>Removes a pot row. Only the ledger writes need it, and only for a pot expense and its shares.</summary>
        void Remove<T>(T entity) where T : PotEntity;

        void RemoveRange<T>(IEnumerable<T> entities) where T : PotEntity;

        /// <summary>
        /// Every share belonging to the current user, across every pot, with the
        /// expense and the pot's currency alongside. Untracked.
        /// <para>
        /// <b>The one read here scoped by identity rather than by membership</b>,
        /// and the exception is deliberate. Every other method answers a group
        /// question — "what does this pot look like to somebody in it" — and
        /// stops at the edge of a pot you have left. This one answers a personal
        /// question: what did <i>I</i> consume, for my own budget and my own
        /// category totals. Money you spent in July is still money you spent
        /// after you leave the pot in August, so the filter is
        /// <c>Membership.UserId == currentUser</c> with no <c>LeftAt</c> clause —
        /// and it can only ever return the caller's own rows, which is why
        /// widening it that way discloses nothing.
        /// </para>
        /// </summary>
        IQueryable<PotExpenseShare> SharesOfCurrentUser();

        /// <summary>
        /// The cash balance and the caller's own position for every pot they are
        /// in, in a fixed number of grouped aggregates rather than one pass per
        /// pot.
        /// <para>
        /// The pots list shows both, and a per-pot loop here would put a query
        /// count on a page behind the number of groups somebody belongs to. The
        /// detail screen's full ledger is loaded row by row for a different
        /// reason — it has to show the entries anyway, and its positions have to
        /// come from exactly those rows.
        /// </para>
        /// </summary>
        Task<IReadOnlyDictionary<Guid, PotMoney>> MoneyForMemberPotsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether any pot expense share anywhere still points at that category.
        /// The "Shared" category's delete guard, asked as the sentence the
        /// contract states it in.
        /// </summary>
        Task<bool> AnySharesInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

        void Add<T>(T entity) where T : PotEntity;
        void Update<T>(T entity) where T : PotEntity;

        Task<int> SaveAsync(CancellationToken cancellationToken = default);
        Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}
