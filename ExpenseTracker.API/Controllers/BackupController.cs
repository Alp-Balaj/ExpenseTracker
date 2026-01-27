using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupExportService _exportService;

    public BackupController(IBackupExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpGet("export")]
    public async Task Export(CancellationToken ct)
    {
        Response.ContentType = "application/zip";
        var fileName = $"backup_{DateTime.UtcNow:yyyy-MM-dd}.zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        // Important: no userId param. Always use the authenticated user.
        await _exportService.WriteExportZipAsync(Response.Body, ct);
    }
}
