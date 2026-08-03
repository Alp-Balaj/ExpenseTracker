using ExpenseTracker.Application.DTOs.Reports;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace ExpenseTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class ReportsController : ControllerBase
{
    private readonly ITransactionReportService _service;

    public ReportsController(ITransactionReportService service)
    {
        _service = service;
    }

    [HttpPost("transactions/preview")]
    public async Task<IActionResult> Preview([FromBody] TransactionReportRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.GenerateAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("transactions/export")]
    public async Task<IActionResult> ExportCsv([FromBody] TransactionReportRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.GenerateAsync(request, cancellationToken);

        // Escaping (and the formula-injection guard) moved to CsvWriter with C·4,
        // so the report export and the full-account export cannot drift apart on it.
        static string Csv(string s) => CsvWriter.Escape(s);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Type,Category,Account,Amount,Currency");

        foreach (var r in result.Rows)
        {
            sb.AppendLine(string.Join(',',
                r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Kind.ToString(),
                Csv(r.Category),
                Csv(r.Account),
                r.Amount.ToString(CultureInfo.InvariantCulture),
                Csv(r.Currency)));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "transaction-report.csv");
    }
}
