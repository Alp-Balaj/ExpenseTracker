using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Stream R: what other people still owe the user, and the act of it coming
    /// back.
    /// <para>
    /// Not a <see cref="BaseController{TEntity,TDto}"/>: a receivable is not
    /// created, edited or deleted on its own — it exists because an expense was
    /// split, and it is maintained by that expense's write path. Only two things
    /// happen here: reading the list, and booking an instalment.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ReimbursementController : ControllerBase
    {
        private readonly IReimbursementService _reimbursements;

        public ReimbursementController(IReimbursementService reimbursements)
        {
            _reimbursements = reimbursements;
        }

        /// <param name="includeSettled">
        /// The default view is what is still out; pass true for the full history.
        /// </param>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeSettled = false, CancellationToken cancellationToken = default) =>
            Ok(await _reimbursements.GetAsync(includeSettled, cancellationToken));

        [HttpPost("{id:guid}/settlements")]
        public async Task<IActionResult> Settle(Guid id, [FromBody] SettleReimbursementDTO dto, CancellationToken cancellationToken)
        {
            var (reimbursement, error) = await _reimbursements.SettleAsync(id, dto, cancellationToken);

            if (error != null) return BadRequest(error);
            // No error and nothing back means the id resolved to nothing this
            // user owns — indistinguishable from an id that never existed.
            if (reimbursement == null) return NotFound();

            return Created($"/api/Reimbursement/{id}", reimbursement);
        }
    }
}
