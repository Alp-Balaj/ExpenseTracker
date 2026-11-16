namespace ExpenseTracker.Domain.Entities.User
{
    /// <summary>
    /// An OAuth client that registered itself through dynamic client registration
    /// (RFC 7591), e.g. claude.ai adding this server as a custom connector.
    /// <para>
    /// Deliberately not a <c>BaseEntity</c>: a client exists before any user has
    /// approved it and is shared by everyone who connects through it, so it must
    /// not sit under the per-user cascade in <c>ConfigureUserOwnership</c>.
    /// Clients that identify themselves by a metadata document URL (CIMD) are never
    /// stored; their document is fetched when they ask for authorization.
    /// </para>
    /// </summary>
    public class OAuthClient
    {
        /// <summary>The client_id handed back by registration. Random and opaque.</summary>
        public required string ClientId { get; set; }

        /// <summary>
        /// Peppered SHA-256 of the client secret, for confidential clients. Null for
        /// public clients (token_endpoint_auth_method "none"), which rely on PKCE alone.
        /// </summary>
        public string? ClientSecretHash { get; set; }

        /// <summary>Self-declared and unverified, so the consent screen also shows the redirect host.</summary>
        public required string ClientName { get; set; }

        /// <summary>Exact-match list: an authorization request must name one of these.</summary>
        public List<string> RedirectUris { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
