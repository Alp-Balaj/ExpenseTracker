using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// A record that one client-generated request key has already been applied,
    /// and what the answer was.
    /// <para>
    /// Stream O's whole guarantee rests on this row. An offline outbox retries: a
    /// queued expense is POSTed when the network comes back, and if the response
    /// is lost in transit — a dropped connection on a train, a lie-fi network
    /// that accepts the request and then drops the reply — the queue has no way
    /// to know whether it worked and will send it again. Without something on the
    /// server that remembers, "retry until it succeeds" means "charge the account
    /// twice".
    /// </para>
    /// <para>
    /// The stored <see cref="ResponseBody"/> is what makes the retry a real
    /// success rather than a swallowed duplicate: the second attempt gets back the
    /// same 201 and the same created id the first one did, so the client's cache
    /// converges on the right row instead of on nothing.
    /// </para>
    /// </summary>
    public class IdempotencyRecord : BaseEntity
    {
        [SetsRequiredMembers]
        public IdempotencyRecord() : base()
        {
            UserId = null!;
            Key = null!;
            Endpoint = null!;
        }

        /// <summary>
        /// The key the client minted before it ever went online — a UUID created
        /// when the expense was typed, not when it was sent. That ordering is the
        /// point: a key generated at send time would be different on every retry
        /// and would guarantee nothing.
        /// </summary>
        public required string Key { get; set; }

        /// <summary>
        /// Method and path this key was used for. A key is only meaningful for the
        /// request it was minted for; replaying it at a different endpoint is a
        /// client bug, and answering with the wrong stored response would be worse
        /// than refusing.
        /// </summary>
        public required string Endpoint { get; set; }

        /// <summary>
        /// Null while the first attempt is still in flight. A second request
        /// arriving in that window is told to come back rather than being allowed
        /// to run concurrently — see <c>IdempotentAttribute</c>.
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>The response body verbatim, so a replay is answered identically.</summary>
        public string? ResponseBody { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
