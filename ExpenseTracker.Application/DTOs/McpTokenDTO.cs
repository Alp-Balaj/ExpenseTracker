using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Validation;

namespace ExpenseTracker.Application.DTOs
{
    public static class McpTokenLimits
    {
        public const int NameMaxLength = 100;

        /// <summary>One per client is the norm; this only stops a runaway script.</summary>
        public const int MaxTokensPerUser = 20;
    }

    public sealed class CreateMcpTokenDTO
    {
        [Required, NotWhitespace, MaxLength(McpTokenLimits.NameMaxLength)]
        public required string Name { get; set; }

        /// <summary>Opt-in: a token is read-only unless the user asks otherwise.</summary>
        public bool CanWrite { get; set; }
    }

    public class McpTokenDTO
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public required string LastFour { get; init; }
        public bool CanWrite { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastUsedAtUtc { get; init; }
    }

    /// <summary>The only response that ever carries the raw token.</summary>
    public sealed class CreatedMcpTokenDTO : McpTokenDTO
    {
        public required string Token { get; init; }
    }
}
