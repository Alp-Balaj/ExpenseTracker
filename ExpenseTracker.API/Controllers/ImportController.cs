using System.Text.Json;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Suggestion-6: import a bank statement instead of typing thirty rows.
    /// <para>
    /// Two calls, both taking the file: <c>preview</c> says what would happen,
    /// <c>commit</c> does it. The file is uploaded twice on purpose — parking a
    /// parsed statement in server memory or a session between the two would make
    /// the preview a snapshot of something that no longer exists, and re-reading
    /// it means the preview and the commit are produced by literally the same
    /// code path.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class ImportController : ControllerBase
    {
        /// <summary>
        /// Cap on the upload. A personal statement is tens of kilobytes; anything
        /// past this is either a mistake or an attempt to make the server chew.
        /// </summary>
        private const long MaxFileBytes = 5 * 1024 * 1024;

        private static readonly JsonSerializerOptions MappingJson = new(JsonSerializerDefaults.Web);

        private readonly IStatementImportService _import;

        public ImportController(IStatementImportService import)
        {
            _import = import;
        }

        /// <summary>
        /// Reads the file and reports what it found. Called once on upload with no
        /// mapping (answering with the columns and a suggestion), then again after
        /// the user adjusts the mapping.
        /// </summary>
        [HttpPost("preview")]
        [RequestSizeLimit(MaxFileBytes)]
        public async Task<IActionResult> Preview(
            IFormFile file, [FromForm] string? mapping, CancellationToken cancellationToken)
        {
            var error = Validate(file, mapping, out var parsed);
            if (error is not null) return BadRequest(error);

            try
            {
                await using var stream = file.OpenReadStream();
                return Ok(await _import.PreviewAsync(stream, parsed!, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                // Account or category not found / not owned — a client-side mistake,
                // not a server fault.
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Creates the importable rows. Duplicates and unreadable rows are reported
        /// back rather than skipped silently — "42 imported, 3 duplicates skipped,
        /// 2 unreadable" is the sentence that makes an import trustworthy.
        /// </summary>
        [HttpPost("commit")]
        [RequestSizeLimit(MaxFileBytes)]
        public async Task<IActionResult> Commit(
            IFormFile file, [FromForm] string? mapping, CancellationToken cancellationToken)
        {
            var error = Validate(file, mapping, out var parsed);
            if (error is not null) return BadRequest(error);

            if (!parsed!.IsComplete)
                return BadRequest("Choose the date, amount and description columns and both default categories first.");

            try
            {
                await using var stream = file.OpenReadStream();
                return Ok(await _import.CommitAsync(stream, parsed, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// The mapping rides as a JSON string in the multipart body rather than as
        /// form fields: it is a nested object with a rule list, and flattening that
        /// into <c>rules[0].contains</c> keys would be worse on both sides.
        /// </summary>
        private static string? Validate(IFormFile? file, string? mapping, out ImportMappingDTO? parsed)
        {
            parsed = null;

            if (file is null || file.Length == 0) return "Upload a CSV file.";
            if (file.Length > MaxFileBytes) return "That file is too large to import.";

            if (string.IsNullOrWhiteSpace(mapping))
            {
                // No mapping yet: the caller wants to know what the file looks like.
                parsed = new ImportMappingDTO();
                return null;
            }

            try
            {
                parsed = JsonSerializer.Deserialize<ImportMappingDTO>(mapping, MappingJson);
            }
            catch (JsonException)
            {
                return "The import mapping could not be read.";
            }

            if (parsed is null) return "The import mapping could not be read.";
            return null;
        }
    }
}
