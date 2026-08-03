using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// A browser's <c>PushSubscription.toJSON()</c>, as the SPA hands it over.
    /// The shape is the Push API's, not ours — keeping it verbatim means the
    /// client posts what the browser gave it without reshaping anything.
    /// </summary>
    public sealed class SavePushSubscriptionDTO
    {
        [Required]
        [MaxLength(500)]
        [Url]
        public string Endpoint { get; set; } = string.Empty;

        [Required]
        public PushSubscriptionKeysDTO Keys { get; set; } = new();

        /// <summary>Optional label for the settings list; the browser's UA string.</summary>
        [MaxLength(200)]
        public string? UserAgent { get; set; }
    }

    public sealed class PushSubscriptionKeysDTO
    {
        /// <summary>Base64url P-256 public key. 65 raw bytes, so 87–88 characters.</summary>
        [Required]
        [MaxLength(200)]
        public string P256dh { get; set; } = string.Empty;

        /// <summary>Base64url 16-byte auth secret.</summary>
        [Required]
        [MaxLength(100)]
        public string Auth { get; set; } = string.Empty;
    }

    /// <summary>Body of the unsubscribe call — the endpoint the browser just discarded.</summary>
    public sealed class UnsubscribePushDTO
    {
        [Required]
        [MaxLength(500)]
        public string Endpoint { get; set; } = string.Empty;
    }

    /// <summary>One registered device, for the settings screen.</summary>
    public sealed class PushDeviceDTO
    {
        public Guid Id { get; init; }
        public string? UserAgent { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastSuccessAtUtc { get; init; }

        /// <summary>
        /// Whether this row is the caller's current browser. The endpoint itself
        /// is never returned — it is a capability to push to that device, and it
        /// buys the client nothing it does not already have.
        /// </summary>
        public bool IsCurrentDevice { get; init; }
    }

    /// <summary>
    /// What the SPA needs before it can even offer the permission prompt: the
    /// application server key, and whether this deployment can send at all.
    /// </summary>
    public sealed class PushConfigurationDTO
    {
        public bool Available { get; init; }
        public string? PublicKey { get; init; }
    }
}
