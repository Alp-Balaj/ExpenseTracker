using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// C·4: take everything with you.
    /// <para>
    /// The counterweight to Suggestion-6's import, and the reason handing this app
    /// years of financial history is a reversible decision. Two formats because
    /// they answer different questions: the JSON is complete and keeps every
    /// relationship, the CSV is the one people actually open.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class ExportController : ControllerBase
    {
        private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web)
        {
            // Indented on purpose: an export is read by a person at least once,
            // usually while deciding whether to trust it.
            WriteIndented = true
        };

        private readonly IAccountExportService _export;

        public ExportController(IAccountExportService export)
        {
            _export = export;
        }

        /// <summary>Every table this account owns, ids included so the relationships survive.</summary>
        [HttpGet("json")]
        public async Task<IActionResult> Json(CancellationToken cancellationToken)
        {
            var snapshot = await _export.ExportAsync(cancellationToken);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Pretty);

            return File(bytes, "application/json", FileName("json"));
        }

        /// <summary>
        /// Transactions flattened into one sheet. Lossier than the JSON — budgets,
        /// goals and planned templates are not transactions — which is why both
        /// exist.
        /// </summary>
        [HttpGet("csv")]
        public async Task<IActionResult> Csv(CancellationToken cancellationToken)
        {
            var csv = await _export.ExportTransactionsCsvAsync(cancellationToken);

            // BOM so Excel opens UTF-8 as UTF-8 rather than as the local codepage,
            // which is what turns a café into a cafÃ©.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

            return File(bytes, "text/csv", FileName("csv"));
        }

        private static string FileName(string extension) =>
            $"expensetracker-export-{DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{extension}";
    }
}
