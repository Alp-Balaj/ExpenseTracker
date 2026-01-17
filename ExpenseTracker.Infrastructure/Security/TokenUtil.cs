using System.Security.Cryptography;
using System.Text;

namespace ExpenseTracker.Infrastructure.Security
{
    public static class TokenUtil
    {
        /// <summary>
        /// Generates a cryptographically secure refresh token (raw secret).
        /// This is what you send to the client as an HttpOnly cookie.
        /// </summary>
        public static string GenerateRefreshToken(int sizeBytes = 64)
        {
            if (sizeBytes < 32)
                throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Use at least 32 bytes for security.");

            var bytes = RandomNumberGenerator.GetBytes(sizeBytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Hashes the refresh token for safe storage in DB.
        /// Store ONLY this hash in the database, never the raw token.
        /// Optionally include a server-side pepper from config for extra hardening.
        /// </summary>
        public static string HashRefreshToken(string rawToken, string? pepper = null)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                throw new ArgumentException("Token cannot be null/empty.", nameof(rawToken));

            var input = rawToken + (pepper ?? "");
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashBytes);
        }
    }
}
