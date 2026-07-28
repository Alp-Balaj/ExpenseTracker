using ExpenseTracker.Application.Validation;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// The confirmation step: what actually happened, as opposed to what was
    /// planned. Every field is optional and falls back to the template's or the
    /// occurrence's own value, so a bill that went exactly as expected commits
    /// on an empty body.
    /// </summary>
    public class CommitPlannedOccurrenceDTO
    {
        /// <summary>Null keeps the template's expected amount.</summary>
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal? Amount { get; set; }

        /// <summary>
        /// The day the money actually moved. Null keeps the due date — which is
        /// only ever a prediction, and is the field most likely to be corrected.
        /// </summary>
        [NotDefault]
        public DateOnly? Date { get; set; }

        /// <summary>Debited account. Null keeps the template's default.</summary>
        [NotDefault]
        public Guid? FromAccountId { get; set; }

        /// <summary>Credited account. Null keeps the template's default.</summary>
        [NotDefault]
        public Guid? ToAccountId { get; set; }

        /// <summary>
        /// Whether an overridden <see cref="Amount"/> should also become the
        /// template's new expected amount. Off by default: one expensive month
        /// of electricity should not silently redefine what electricity costs.
        /// The accounts are not covered by this — they always carry over, so the
        /// next commit pre-fills with whatever was used last.
        /// </summary>
        public bool UpdateTemplateAmount { get; set; }
    }
}
