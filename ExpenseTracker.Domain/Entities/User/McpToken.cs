using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.User
{
    /// <summary>
    /// A long-lived credential for one MCP client (Claude Code, a desktop app).
    /// <para>
    /// Separate from the JWT/refresh pair on purpose: an MCP client has no browser
    /// to hold a cookie and no person present to log in again every few minutes.
    /// In exchange the token is good for exactly one route, /api/mcp — never for
    /// the REST API, where it would reach password change and account deletion.
    /// </para>
    /// <para>
    /// Revoking deletes the row: nothing needs to know a dead token ever existed.
    /// </para>
    /// </summary>
    public class McpToken : BaseEntity
    {
        /// <summary>The user's label for the client, e.g. "Laptop Claude Code".</summary>
        public required string Name { get; set; }

        /// <summary>Peppered SHA-256 of the raw token. The raw token is never stored.</summary>
        public required string TokenHash { get; set; }

        /// <summary>The token's last characters, so a list can tell two tokens apart.</summary>
        public required string LastFour { get; set; }

        /// <summary>False means read-only: the write tools refuse this token.</summary>
        public bool CanWrite { get; set; }

        /// <summary>Approximate — refreshed at most hourly, see McpTokenAuthenticationHandler.</summary>
        public DateTime? LastUsedAtUtc { get; set; }

        // OAuth connections (claude.ai custom connectors) live in this same table, so
        // the auth handler, the settings list and "revoke all on password change" treat
        // both kinds identically. A token created on the settings screen leaves all four
        // of these null: no client, no expiry, no refresh.

        /// <summary>The OAuth client this connection was granted to; null for a pasted token.</summary>
        public string? OAuthClientId { get; set; }

        /// <summary>When the current access token stops working. Null means never.</summary>
        public DateTime? ExpiresAtUtc { get; set; }

        /// <summary>Peppered SHA-256 of the current refresh token. Rotated on every refresh.</summary>
        public string? RefreshTokenHash { get; set; }

        /// <summary>Sliding: each refresh pushes it out again, so an idle connection lapses.</summary>
        public DateTime? RefreshExpiresAtUtc { get; set; }
    }
}
