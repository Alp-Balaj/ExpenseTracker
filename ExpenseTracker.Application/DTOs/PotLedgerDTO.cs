using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// Everything the pot's money screen shows, in one read.
    /// <para>
    /// One call rather than three because it is one screen, and because the
    /// balance, the positions and the feed are three views of the same rows —
    /// fetched separately they could disagree with each other on the way down
    /// the wire, and a positions table that does not add up to the balance
    /// beside it is precisely the failure this wave exists to make impossible.
    /// </para>
    /// <para>
    /// <b>Every amount here is in <see cref="CurrencyCode"/> and nothing is
    /// converted.</b> Group screens are always in the pot's currency. The
    /// personal side converts through each member's own rates; inside a pot
    /// there is one currency and no rate to be missing.
    /// </para>
    /// </summary>
    public class PotLedgerDTO
    {
        public Guid PotId { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;

        /// <summary>
        /// The pot's cash: top-ups, less what the pot itself paid for, less
        /// withdrawals. Out-of-pocket expenses do not appear because no pot cash
        /// moved. <c>Σ Positions.Position</c> equals this, always.
        /// </summary>
        public decimal Balance { get; set; }

        /// <summary>True when the pot has an alert set and has fallen below it. W4 acts on it.</summary>
        public bool IsLow { get; set; }

        public IReadOnlyList<PotPositionDTO> Positions { get; set; } = [];

        /// <summary>Top-ups, expenses and withdrawals as one feed, newest first.</summary>
        public IReadOnlyList<PotLedgerEntryDTO> Entries { get; set; } = [];
    }

    /// <summary>
    /// One member's standing against the pot — the four terms as well as the
    /// answer, because "you are 12.40 down" is only trustworthy next to the
    /// arithmetic that produced it.
    /// </summary>
    public class PotPositionDTO
    {
        public Guid MembershipId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsYou { get; set; }
        public bool IsActive { get; set; }

        public decimal ToppedUp { get; set; }
        public decimal OutOfPocket { get; set; }
        public decimal Shares { get; set; }
        public decimal Withdrawn { get; set; }

        /// <summary><c>ToppedUp + OutOfPocket − Shares − Withdrawn</c>. Positive means the pot owes you.</summary>
        public decimal Position { get; set; }

        /// <summary>
        /// What this member may take out today: their position when it is
        /// positive, and never more than the pot actually holds. Server-decided
        /// so the cap the withdrawal form shows is the cap the withdrawal
        /// endpoint enforces.
        /// </summary>
        public decimal Withdrawable { get; set; }
    }

    /// <summary>One line of the pot's money feed.</summary>
    public class PotLedgerEntryDTO
    {
        public Guid Id { get; set; }
        public PotLedgerEntryKind Kind { get; set; }
        public DateOnly Date { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>The gross amount. What it means depends on <see cref="Kind"/>.</summary>
        public decimal Amount { get; set; }

        /// <summary>The expense's title, or the note on a top-up or withdrawal.</summary>
        public string? Title { get; set; }
        public string? Description { get; set; }

        /// <summary>Whose money moved: the contributor, the withdrawer, or whoever logged the expense.</summary>
        public string? ActorDisplayName { get; set; }
        public Guid? ActorMembershipId { get; set; }

        /// <summary>
        /// On an expense: the member who fronted it out of their own pocket, or
        /// null when the pot paid. Null on every other kind.
        /// </summary>
        public string? PaidByDisplayName { get; set; }
        public Guid? PaidByMembershipId { get; set; }

        /// <summary>Who the expense was for. Empty on top-ups and withdrawals.</summary>
        public IReadOnlyList<PotShareDTO> Shares { get; set; } = [];

        /// <summary>
        /// Whether the reader may delete this line. Only expenses can be
        /// deleted in this wave, and an out-of-pocket one only by the member
        /// whose account has to be credited back — nobody writes into an account
        /// they do not own.
        /// </summary>
        public bool CanDelete { get; set; }
    }

    public class PotShareDTO
    {
        public Guid MembershipId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsYou { get; set; }
        public decimal Amount { get; set; }
    }

    // ── Writes ─────────────────────────────────────────────────────────────────

    public class CreatePotTopUpDTO
    {
        /// <summary>In the pot's currency. Must be positive.</summary>
        [Range(0.01, 1_000_000_000)]
        public decimal Amount { get; set; }

        /// <summary>One of the caller's own accounts. Never disclosed to the other members.</summary>
        [Required]
        public Guid SourceAccountId { get; set; }

        /// <summary>Defaults to today when omitted.</summary>
        public DateOnly? Date { get; set; }

        [MaxLength(200)]
        public string? Note { get; set; }
    }

    public class CreatePotWithdrawalDTO
    {
        [Range(0.01, 1_000_000_000)]
        public decimal Amount { get; set; }

        /// <summary>Any account the caller owns. Capped at their positive position.</summary>
        [Required]
        public Guid DestinationAccountId { get; set; }

        public DateOnly? Date { get; set; }

        [MaxLength(200)]
        public string? Note { get; set; }
    }

    /// <summary>
    /// A pot expense and who it was for.
    /// <para>
    /// The four entry affordances are all this one shape.
    /// <see cref="Shares"/> lists the participants; an entry with no
    /// <see cref="PotExpenseShareInputDTO.Amount"/> divides what is left over
    /// evenly with the other such entries, by largest remainder. "Everyone" is
    /// every member with no amounts, "custom" is some with, "one person" is a
    /// list of one.
    /// </para>
    /// </summary>
    public class CreatePotExpenseDTO
    {
        [Required]
        [NotWhitespace]
        [StringLength(100, MinimumLength = 1)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Range(0.01, 1_000_000_000)]
        public decimal Amount { get; set; }

        public DateOnly? Date { get; set; }

        /// <summary>
        /// Null — the ordinary case — means the pot paid for it, and the pot's
        /// balance falls. Set to one of the <b>caller's own</b> accounts to
        /// record that they fronted it personally: their account is debited, the
        /// pot's balance does not move, and the payment books as a contribution
        /// and a consumption in one gesture.
        /// <para>
        /// There is deliberately no way to say that somebody <i>else</i> paid.
        /// Attribution is free — any member may say an expense was for anyone —
        /// but funding is not, because recording it moves a real account
        /// balance, and no user may write into an account they do not own.
        /// </para>
        /// </summary>
        public Guid? PaidFromAccountId { get; set; }

        [Required]
        [MinLength(1)]
        public List<PotExpenseShareInputDTO> Shares { get; set; } = [];
    }

    public class PotExpenseShareInputDTO
    {
        [Required]
        public Guid MembershipId { get; set; }

        /// <summary>
        /// An exact amount this person owes on this expense, or null to take an
        /// even slice of whatever the typed amounts leave behind.
        /// </summary>
        [Range(0, 1_000_000_000)]
        public decimal? Amount { get; set; }
    }
}
