namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// Stream R: one open (or closed) receivable, flattened with the expense
    /// behind it so the client never has to join the two itself.
    /// </summary>
    public class ReimbursementDTO
    {
        public Guid Id { get; set; }

        public Guid ExpenseId { get; set; }
        public string ExpenseTitle { get; set; } = string.Empty;
        public DateOnly ExpenseDate { get; set; }

        /// <summary>Gross: what actually left the account.</summary>
        public decimal ExpenseAmount { get; set; }

        /// <summary>What is expected back in total.</summary>
        public decimal ExpectedAmount { get; set; }

        /// <summary>Σ of the settlements below.</summary>
        public decimal SettledAmount { get; set; }

        /// <summary>Still out: expected − settled.</summary>
        public decimal OpenAmount { get; set; }

        /// <summary>The expense account's currency — settlements are in it too.</summary>
        public string CurrencyCode { get; set; } = string.Empty;

        /// <summary>Age in days, from the expense's date. Never negative.</summary>
        public int DaysOpen { get; set; }

        public IReadOnlyList<ReimbursementSettlementDTO> Settlements { get; set; } =
            new List<ReimbursementSettlementDTO>();
    }
}
