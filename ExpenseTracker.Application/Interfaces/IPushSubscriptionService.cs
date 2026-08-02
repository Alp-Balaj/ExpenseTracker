using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// The current user's registered devices — the half of push that a person
    /// drives from the settings screen, as opposed to the half the jobs drive.
    /// </summary>
    public interface IPushSubscriptionService
    {
        /// <summary>
        /// Registers (or re-registers) this browser. Idempotent on the endpoint:
        /// the browser hands back the same endpoint every time until it rotates
        /// one, and a device that gets handed to a different account must move
        /// rather than end up owned twice.
        /// </summary>
        Task<PushDeviceDTO> SubscribeAsync(
            SavePushSubscriptionDTO subscription, CancellationToken cancellationToken = default);

        /// <summary>Removes one endpoint. Returns false when the caller owns no such row.</summary>
        Task<bool> UnsubscribeAsync(string endpoint, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PushDeviceDTO>> ListAsync(
            string? currentEndpoint = null, CancellationToken cancellationToken = default);
    }
}
