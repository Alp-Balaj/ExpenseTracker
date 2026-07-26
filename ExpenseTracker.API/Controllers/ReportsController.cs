using ExpenseTracker.Application.DTOs.Reports;
using ExpenseTracker.Application.Interfaces;
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
    public async Task<IActionResult> Preview([FromBody] TransactionReportRequest request)
    {
        var result = await _service.GenerateAsync(request);
        return Ok(result);
    }

    [HttpPost("transactions/export")]
    public async Task<IActionResult> ExportCsv([FromBody] TransactionReportRequest request)
    {
        var result = await _service.GenerateAsync(request);

        static string Csv(string s)
        {
            // Spreadsheet formula injection: neutralize leading =, +, -, @.
            if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@')
                s = "'" + s;

            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

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
