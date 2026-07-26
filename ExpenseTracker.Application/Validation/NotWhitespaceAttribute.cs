using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Validation
{
    // [Required] rejects null and "", but lets whitespace-only strings
    // through; this closes that gap. Null passes — pair with [Required].
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class NotWhitespaceAttribute : ValidationAttribute
    {
        public NotWhitespaceAttribute() : base("The {0} field must not be empty or whitespace.") { }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;

            return value is not string s || !string.IsNullOrWhiteSpace(s);
        }
    }
}
