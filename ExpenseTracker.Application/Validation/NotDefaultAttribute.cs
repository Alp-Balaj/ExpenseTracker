using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.Validation
{
    // Rejects value-type defaults the model binder silently fills in when the
    // client omits the field (Guid.Empty, 0001-01-01 dates). Null passes —
    // pair with [Required] when the field itself is mandatory.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class NotDefaultAttribute : ValidationAttribute
    {
        public NotDefaultAttribute() : base("The {0} field is required.") { }

        public override bool IsValid(object? value)
        {
            if (value == null) return true;

            var type = value.GetType();
            if (!type.IsValueType) return true;

            return !value.Equals(Activator.CreateInstance(type));
        }
    }
}
