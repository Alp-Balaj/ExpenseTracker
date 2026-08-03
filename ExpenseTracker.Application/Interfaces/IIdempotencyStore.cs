namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>What a key lookup found.</summary>
    public enum IdempotencyClaim
    {
        /// <summary>This caller won the key. Run the request, then record the result.</summary>
        Claimed = 0,

        /// <summary>The key has been used and the answer is known — replay it verbatim.</summary>
        AlreadyCompleted = 1,

        /// <summary>
        /// The key is claimed but the first attempt has not finished. The client is
        /// asked to come back rather than being allowed to run the same write twice
        /// concurrently.
        /// </summary>
        InFlight = 2,

        /// <summary>The key exists but was minted for a different endpoint — a client bug.</summary>
        EndpointMismatch = 3
    }

    /// <param name="Claim">What the caller may do.</param>
    /// <param name="StatusCode">The recorded status, when <see cref="IdempotencyClaim.AlreadyCompleted"/>.</param>
    /// <param name="ResponseBody">The recorded body, when <see cref="IdempotencyClaim.AlreadyCompleted"/>.</param>
    public readonly record struct IdempotencyResult(
        IdempotencyClaim Claim, int? StatusCode = null, string? ResponseBody = null);

    /// <summary>
    /// Stream O's server half: makes a retried write apply exactly once.
    /// <para>
    /// The client's outbox cannot distinguish "the server never got it" from "the
    /// server got it and the reply was lost", so it has to retry, and the server
    /// has to be the thing that remembers. Everything here is decided by a unique
    /// index rather than by a check in code, because the two colliding requests
    /// can be on two connections in the same instant.
    /// </para>
    /// </summary>
    public interface IIdempotencyStore
    {
        /// <summary>
        /// Attempts to claim <paramref name="key"/> for the current user and
        /// <paramref name="endpoint"/>.
        /// </summary>
        Task<IdempotencyResult> TryClaimAsync(
            string key, string endpoint, CancellationToken cancellationToken = default);

        /// <summary>Records the outcome so a later replay is answered identically.</summary>
        Task CompleteAsync(
            string key, int statusCode, string? responseBody, CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a claim whose request failed, so the client's next retry is a
        /// fresh attempt rather than a permanent <see cref="IdempotencyClaim.InFlight"/>.
        /// A failed write applied nothing, so there is nothing to be idempotent about.
        /// </summary>
        Task AbandonAsync(string key, CancellationToken cancellationToken = default);
    }
}
