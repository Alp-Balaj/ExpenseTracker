namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// Everything one account owns, in one document.
    /// <para>
    /// C·4's premise: a tracker you cannot leave is a hostage situation with a
    /// nice chart. So this is deliberately the whole thing — not a report, not a
    /// filtered view — and it is shaped so a person could rebuild their history
    /// somewhere else from it: ids are included so the relationships survive, and
    /// every reference also carries the human-readable name so the file is
    /// legible on its own.
    /// </para>
    /// </summary>
    public sealed class AccountExportDTO
    {
        /// <summary>Bumped if the shape ever changes incompatibly, so an old file stays readable.</summary>
        public int FormatVersion { get; init; } = 1;

        public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;

        public ExportProfileDTO Profile { get; init; } = new();

        public IReadOnlyList<ExportCurrencyDTO> Currencies { get; init; } = [];
        public IReadOnlyList<ExportCategoryDTO> Categories { get; init; } = [];
        public IReadOnlyList<ExportAccountDTO> Accounts { get; init; } = [];
        public IReadOnlyList<ExportTransactionDTO> Expenses { get; init; } = [];
        public IReadOnlyList<ExportTransactionDTO> Incomes { get; init; } = [];
        public IReadOnlyList<ExportTransferDTO> Transfers { get; init; } = [];
        public IReadOnlyList<ExportBudgetDTO> Budgets { get; init; } = [];
        public IReadOnlyList<ExportSavingGoalDTO> SavingGoals { get; init; } = [];
        public IReadOnlyList<ExportSavingContributionDTO> SavingContributions { get; init; } = [];
        public IReadOnlyList<ExportPlannedTransactionDTO> PlannedTransactions { get; init; } = [];
        public IReadOnlyList<ExportPlannedOccurrenceDTO> PlannedOccurrences { get; init; } = [];
    }

    public sealed class ExportProfileDTO
    {
        public string? Email { get; init; }
        public string? UserName { get; init; }
        public string? BaseCurrency { get; init; }
        public string? Theme { get; init; }
    }

    public sealed class ExportCurrencyDTO
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Symbol { get; init; }
        public decimal ExchangeRateToBase { get; init; }
        public DateTime? RateUpdatedAtUtc { get; init; }
    }

    public sealed class ExportCategoryDTO
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public int CategoryType { get; init; }
        public string? Color { get; init; }
    }

    public sealed class ExportAccountDTO
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int AmountType { get; init; }
        public decimal Balance { get; init; }
        public string? Description { get; init; }
        public Guid CurrencyId { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
    }

    /// <summary>An expense or an income — same shape, different list.</summary>
    public sealed class ExportTransactionDTO
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public string? Title { get; init; }
        public decimal Amount { get; init; }
        public string? Description { get; init; }
        public Guid AccountId { get; init; }
        public string AccountName { get; init; } = string.Empty;
        public string CurrencyCode { get; init; } = string.Empty;
        public Guid CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }

    public sealed class ExportTransferDTO
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public decimal Amount { get; init; }
        public string? Description { get; init; }
        public Guid FromAccountId { get; init; }
        public string FromAccountName { get; init; } = string.Empty;
        public Guid ToAccountId { get; init; }
        public string ToAccountName { get; init; } = string.Empty;
        public Guid? SavingGoalId { get; init; }
        public Guid? SavingContributionId { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    public sealed class ExportBudgetDTO
    {
        public Guid Id { get; init; }
        public decimal Amount { get; init; }
        public int Period { get; init; }
        public string? Note { get; init; }
        public Guid? CategoryId { get; init; }
        public string? CategoryName { get; init; }
    }

    public sealed class ExportSavingGoalDTO
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public decimal TargetAmount { get; init; }
        public decimal SavedAmount { get; init; }
        public DateOnly Deadline { get; init; }
        public string? Description { get; init; }
        /// <summary>Deprecated by Stream V — a goal's original backing account, absent on goals created since.</summary>
        public Guid? AccountId { get; init; }
        public Guid CategoryId { get; init; }
    }

    public sealed class ExportSavingContributionDTO
    {
        public Guid Id { get; init; }
        public Guid GoalId { get; init; }
        public decimal Amount { get; init; }
        public DateOnly Date { get; init; }
        public string? Note { get; init; }
        /// <summary>The account this money is reserved against — an export without it would not say where the money is.</summary>
        public Guid? AccountId { get; init; }
        public string AccountName { get; init; } = string.Empty;
    }

    public sealed class ExportPlannedTransactionDTO
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string? Description { get; init; }
        public int Kind { get; init; }
        public int Recurrence { get; init; }
        public DateOnly StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public bool IsActive { get; init; }
        public Guid? CategoryId { get; init; }
        public Guid? FromAccountId { get; init; }
        public Guid? ToAccountId { get; init; }
        public Guid? SavingGoalId { get; init; }
    }

    public sealed class ExportPlannedOccurrenceDTO
    {
        public Guid Id { get; init; }
        public Guid PlannedTransactionId { get; init; }
        public string PeriodKey { get; init; } = string.Empty;
        public DateOnly DueDate { get; init; }
        public int State { get; init; }
        public DateTime? CommittedAt { get; init; }
        public Guid? ExpenseId { get; init; }
        public Guid? IncomeId { get; init; }
        public Guid? TransferId { get; init; }
    }
}
