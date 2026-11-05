using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Issues and revokes the tokens MCP clients use. JWT-only by construction: the
    /// path is not under /api/mcp, so an MCP token cannot mint another MCP token.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class McpTokenController : ControllerBase
    {
        private readonly IMcpTokenService _tokens;

        public McpTokenController(IMcpTokenService tokens)
        {
            _tokens = tokens;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
            Ok(await _tokens.ListAsync(cancellationToken));

        /// <summary>The response is the only time the raw token is ever available.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateMcpTokenDTO dto, CancellationToken cancellationToken)
        {
            var created = await _tokens.CreateAsync(dto, cancellationToken);
            if (created == null)
                return BadRequest($"You can hold at most {McpTokenLimits.MaxTokensPerUser} tokens. Revoke one first.");

            return StatusCode(StatusCodes.Status201Created, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
            await _tokens.RevokeAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
