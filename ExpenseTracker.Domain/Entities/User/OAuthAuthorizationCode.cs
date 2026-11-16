using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.User
{
    /// <summary>
    /// A single-use authorization code: the user approved a client on the consent
    /// screen, and the client has a few minutes to trade this for tokens.
    /// <para>
    /// A <c>BaseEntity</c>, so deleting the user takes outstanding codes with them.
    /// Redeeming deletes the row, which is what makes a code single-use under
    /// concurrency: only one DELETE can remove it.
    /// </para>
    /// </summary>
    public class OAuthAuthorizationCode : BaseEntity
    {
        /// <summary>Peppered SHA-256 of the code. The raw code is never stored.</summary>
        public required string CodeHash { get; set; }

        public required string ClientId { get; set; }

        /// <summary>Copied from the client so the resulting connection can be labelled.</summary>
        public required string ClientName { get; set; }

        /// <summary>Must be sent again, identically, when the code is redeemed (RFC 6749 §4.1.3).</summary>
        public required string RedirectUri { get; set; }

        /// <summary>The PKCE S256 challenge; the token request must present the matching verifier.</summary>
        public required string CodeChallenge { get; set; }

        /// <summary>What the user granted on the consent screen, not what the client asked for.</summary>
        public bool CanWrite { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }
}
