using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// A recurring spending limit. It carries no month of its own: a budget
    /// applies to every period until the user changes or deletes it, so
    /// "this month's budget" is the current row evaluated against that month's
    /// spend. <see cref="Amount"/> is always in the user's base currency.
    /// </summary>
    public class Budget : BaseEntity
    {
        [SetsRequiredMembers]
        public Budget() : base()
        {
            UserId = null!;
            Amount = default!;
            Period = default!;
        }

        public required decimal Amount { get; set; }
        public required BudgetPeriod Period { get; set; }
        public string? Note { get; set; }

        /// <summary>Null means an overall budget across every expense category.</summary>
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }
    }
}
