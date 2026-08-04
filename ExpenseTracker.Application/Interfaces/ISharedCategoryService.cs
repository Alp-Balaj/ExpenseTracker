using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// The reserved "Shared" category — one per user, common to all of their
    /// pots, created the first time they join one.
    ///
    /// <para>
    /// It exists because a pot expense has to land somewhere in the member's own
    /// taxonomy, and the charter chose one reserved category over a pot-owned
    /// category set with per-member mapping: the mapping table and its settings
    /// screen were judged more machinery than the feature earns, and this
    /// version works with zero setup before the first pot expense. The knowingly
    /// accepted cost is budget granularity — all pot spending, across every pot,
    /// rolls into one line.
    /// </para>
    ///
    /// <para>
    /// <b>Lazily, and only on joining.</b> <c>IUserDataSeeder</c> is a
    /// signup-time seeder that no-ops for a user who already has data, so it is
    /// the wrong place: it cannot run again, and running it at signup would give
    /// the category to everybody. A user in no pots must never see it — that is
    /// a pinned regression, not a nicety — so creation hangs off the one event
    /// that makes it meaningful.
    /// </para>
    /// </summary>
    public interface ISharedCategoryService
    {
        /// <summary>
        /// The current user's "Shared" category, creating it if this is their
        /// first pot.
        /// <para>
        /// <b>Adds without saving.</b> The caller is joining or creating a pot
        /// and is about to flush that in one <c>SaveChangesAsync</c>; this rides
        /// along in it, on the same scoped <c>DbContext</c>. A user who ended up
        /// with the category and no pot — because the join failed after the
        /// category was committed — would be a visible lie about a wave-level
        /// promise, so the two writes are not allowed to be two transactions.
        /// </para>
        /// </summary>
        Task<Category> EnsureForCurrentUserAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The current user's "Shared" category, or null when they have never
        /// joined a pot. Never creates one — the read side must not be able to
        /// conjure the category the neutrality pins say does not exist.
        /// </summary>
        Task<Category?> FindForCurrentUserAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The "Shared" category id of every member in <paramref name="membershipIds"/>,
        /// creating the ones that are missing.
        ///
        /// <para>
        /// Called once, when an expense is about to write its shares, so that
        /// every share can name the category it lands in. Ordinarily this
        /// creates nothing at all: joining a pot already made the row. The case
        /// it exists for is a membership that predates this wave — W1 shipped
        /// joining without the category — whose shares would otherwise be
        /// consumption with no category to appear under, invisible in the
        /// member's own budget and impossible to notice from the group side.
        /// </para>
        ///
        /// <para>
        /// This writes a row belonging to somebody other than the caller, which
        /// nothing else in the application does lightly. It is not the
        /// cross-user write the charter reserves for W3: no money moves, no
        /// balance changes, and the row is the application's own bookkeeping
        /// rather than a member's data — the same thing <c>IUserDataSeeder</c>
        /// writes for a user who is not the caller either, and for the same
        /// reason. Adds without saving, like its single-user sibling.
        /// </para>
        /// </summary>
        /// <returns>Membership id → that member's Shared category id.</returns>
        Task<IReadOnlyDictionary<Guid, Guid>> EnsureForMembershipsAsync(
            IReadOnlyList<Guid> membershipIds, CancellationToken cancellationToken = default);
    }
}
