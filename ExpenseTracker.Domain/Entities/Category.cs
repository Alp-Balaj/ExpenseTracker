using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    public class Category : BaseEntity
    {
        [SetsRequiredMembers]
        public Category() : base()
        {
            UserId = null!;
            Name = null!;
            CategoryType = default!;   
        }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required CategoryType CategoryType { get; set; }
        public string? Color { get; set; }

        /// <summary>
        /// Cycle U W2: null for a category the user made, which is every row
        /// that existed before this wave and almost every row that ever will.
        /// <para>
        /// Set only on the reserved "Shared" category, created lazily the first
        /// time the user joins a pot. Server-owned — <c>CategoryDTO</c> reads it
        /// and never writes it — and guarded on delete while any pot expense
        /// share still points at it.
        /// </para>
        /// </summary>
        public CategorySystemKind? SystemKind { get; set; }

        /// <summary>The one category a user cannot have made themselves.</summary>
        public bool IsSystemOwned => SystemKind is not null;

        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<SavingGoal> SavingGoals { get; set; } = new List<SavingGoal>();
        public ICollection<PlannedTransaction> PlannedTransactions { get; set; } = new List<PlannedTransaction>();
        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
    }
}