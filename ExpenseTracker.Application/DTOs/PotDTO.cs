using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Validation;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>A pot as one of its members sees it. No money in W1.</summary>
    public class PotDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal LowBalanceThreshold { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The creator's frozen membership label, or null once they have
        /// anonymized. Never their user id — a member's identity outside the pot
        /// is not the pot's to disclose.
        /// </summary>
        public string? CreatedByDisplayName { get; set; }

        public int MemberCount { get; set; }

        /// <summary>The reading member's own membership id — what the UI marks as "you".</summary>
        public Guid MembershipId { get; set; }
    }

    public class CreatePotDTO
    {
        [Required]
        [NotWhitespace]
        [StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional; defaults to EUR, which W0 fixed as the cycle's pot currency
        /// because it matches every member's base and so avoids the mismatch W4
        /// would otherwise have to explain.
        /// </summary>
        [StringLength(3, MinimumLength = 3)]
        public string? CurrencyCode { get; set; }

        /// <summary>Optional; defaults to 50. Zero switches the alert off.</summary>
        [Range(0, 1_000_000_000)]
        public decimal? LowBalanceThreshold { get; set; }
    }

    /// <summary>
    /// What any member may change about a pot. The currency is absent on
    /// purpose: it is fixed at creation, because changing it would silently
    /// reinterpret every amount W2 goes on to write.
    /// </summary>
    public class UpdatePotDTO
    {
        [Required]
        [NotWhitespace]
        [StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1_000_000_000)]
        public decimal LowBalanceThreshold { get; set; }
    }

    public class PotMemberDTO
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
        public DateTime? LeftAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsAnonymized { get; set; }

        /// <summary>True for the member doing the reading. There is no other identity here.</summary>
        public bool IsYou { get; set; }
    }

    public class PotInviteDTO
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string IssuedByDisplayName { get; set; } = string.Empty;
        public string? RevokedByDisplayName { get; set; }

        /// <summary>Server-decided rather than left to the client to recompute from two timestamps and a clock.</summary>
        public bool IsRedeemable { get; set; }
        public bool HasExpired { get; set; }
    }

    /// <summary>
    /// What the join screen may show someone who is holding a code and is not
    /// yet a member. Deliberately thin: the pot's name and who invited them, so
    /// they know what they are accepting — and nothing else, no member list, no
    /// history.
    /// </summary>
    public class PotInvitePreviewDTO
    {
        public string PotName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public string InvitedByDisplayName { get; set; } = string.Empty;
        public bool IsRedeemable { get; set; }

        /// <summary>Set when the code is already spent for this caller — they are in the pot.</summary>
        public bool AlreadyAMember { get; set; }

        /// <summary>The plain sentence to show when it cannot be redeemed.</summary>
        public string? Problem { get; set; }
    }

    public class JoinPotDTO
    {
        [Required]
        [NotWhitespace]
        [StringLength(64, MinimumLength = 8)]
        public string Code { get; set; } = string.Empty;
    }

    public class PotActivityDTO
    {
        public Guid Id { get; set; }
        public PotActivityKind Kind { get; set; }
        public string? ActorDisplayName { get; set; }
        public Guid? TargetId { get; set; }
        public string? TargetLabel { get; set; }
        public string? Before { get; set; }
        public string? After { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
