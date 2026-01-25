using ExpenseTracker.Application.DTOs.Reports;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Date,Type,Category,Account,Amount,Currency");

        foreach (var r in result.Rows)
        {
            sb.AppendLine($"{r.Date:yyyy-MM-dd},{r.Kind},{Csv(r.Category)},{Csv(r.Account)},{r.Amount},{r.Currency}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "transaction-report.csv");
    }
}
