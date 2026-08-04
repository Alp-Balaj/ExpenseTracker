namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// Marks a category the application created and the user did not.
    /// <para>
    /// The column behind this is <b>nullable</b>, and every category that
    /// existed before Cycle U W2 carries null — which is what makes the
    /// migration additive and rule-1 clean. Null means "an ordinary category the
    /// user made", and that is the overwhelming majority of rows forever.
    /// </para>
    /// <para>
    /// It is also server-owned: <c>CategoryDTO</c> exposes it for reading so the
    /// UI can badge the row and hide its delete button, and never maps it back
    /// in. A client cannot promote its own category to a system one, and cannot
    /// demote the system one to get around the delete guard.
    /// </para>
    /// </summary>
    public enum CategorySystemKind
    {
        /// <summary>
        /// The reserved category every member's share of every pot expense lands
        /// in. One per user, common to all of their pots, created lazily the
        /// first time they join one — so a user in no pots never sees it.
        /// Undeletable while any pot expense share still points at it.
        /// </summary>
        Shared = 0,
    }
}
