namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// Every member-visible mutation a pot can undergo. Flat trust means the
    /// activity log is the accountability, so a mutation with no kind here is a
    /// mutation nobody can see — new pot operations add a value rather than
    /// going unrecorded.
    /// </summary>
    public enum PotActivityKind
    {
        PotCreated = 0,
        PotRenamed = 1,
        LowBalanceThresholdChanged = 2,
        InviteCreated = 3,
        InviteRevoked = 4,
        MemberJoined = 5,
        MemberLeft = 6,
        MemberAnonymized = 7,

        // W2 — the ledger. Money moving is the most member-visible thing a pot
        // can do, so each of the four operations gets its own line.
        ToppedUp = 8,
        ExpenseLogged = 9,
        ExpenseDeleted = 10,
        Withdrew = 11,
    }
}
