using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Cycle U W1: the shared pot, before there is any money in it.
    /// <para>
    /// Not a <see cref="BaseController{TEntity,TDto}"/>, and it cannot be: that
    /// base is generic over <c>BaseEntity</c> and reads through
    /// <c>IUserRelatedRepository</c>, both of which describe rows one person
    /// owns. A pot is read and written by people who did not create the rows in
    /// it, so every route here goes through <c>IPotService</c> and its
    /// membership-scoped repository instead.
    /// </para>
    /// <para>
    /// <b>Non-members get 404, never 403.</b> A 403 would confirm that the pot is
    /// there, and the existence of a group you were not invited to is itself
    /// private. Throughout, a null from the service means exactly that and is
    /// answered <c>NotFound()</c> — including for ids that never existed, which
    /// is the point: the two must be indistinguishable.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PotController : ControllerBase
    {
        private readonly IPotService _pots;
        private readonly IPotLedgerService _ledger;

        public PotController(IPotService pots, IPotLedgerService ledger)
        {
            _pots = pots;
            _ledger = ledger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
            Ok(await _pots.GetPotsAsync(cancellationToken));

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var pot = await _pots.GetPotAsync(id, cancellationToken);
            return pot is null ? NotFound() : Ok(pot);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePotDTO dto, CancellationToken cancellationToken)
        {
            var pot = await _pots.CreatePotAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = pot.Id }, pot);
        }

        /// <summary>Any member may rename a pot or move its low-balance line. Both are logged.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePotDTO dto, CancellationToken cancellationToken)
        {
            var pot = await _pots.UpdatePotAsync(id, dto, cancellationToken);
            return pot is null ? NotFound() : Ok(pot);
        }

        [HttpGet("{id:guid}/members")]
        public async Task<IActionResult> GetMembers(Guid id, CancellationToken cancellationToken)
        {
            var members = await _pots.GetMembersAsync(id, cancellationToken);
            return members is null ? NotFound() : Ok(members);
        }

        [HttpGet("{id:guid}/invites")]
        public async Task<IActionResult> GetInvites(Guid id, CancellationToken cancellationToken)
        {
            var invites = await _pots.GetInvitesAsync(id, cancellationToken);
            return invites is null ? NotFound() : Ok(invites);
        }

        [HttpPost("{id:guid}/invites")]
        public async Task<IActionResult> CreateInvite(Guid id, CancellationToken cancellationToken)
        {
            var invite = await _pots.CreateInviteAsync(id, cancellationToken);
            return invite is null ? NotFound() : Created($"/api/Pot/{id}/invites/{invite.Id}", invite);
        }

        [HttpPost("{id:guid}/invites/{inviteId:guid}/revoke")]
        public async Task<IActionResult> RevokeInvite(Guid id, Guid inviteId, CancellationToken cancellationToken)
        {
            var (invite, error) = await _pots.RevokeInviteAsync(id, inviteId, cancellationToken);

            if (error != null) return BadRequest(new { message = error });
            return invite is null ? NotFound() : Ok(invite);
        }

        /// <summary>
        /// What a link says about the pot behind it. The one route that answers
        /// for a caller who is not a member — that is what an invite is — so it
        /// discloses only the pot's name, its currency and who invited them.
        /// </summary>
        [HttpGet("invites/{code}")]
        public async Task<IActionResult> PreviewInvite(string code, CancellationToken cancellationToken)
        {
            var preview = await _pots.PreviewInviteAsync(code, cancellationToken);
            return preview is null ? NotFound() : Ok(preview);
        }

        /// <summary>Redeeming is idempotent: twice through the same link leaves one membership.</summary>
        [HttpPost("join")]
        public async Task<IActionResult> Join([FromBody] JoinPotDTO dto, CancellationToken cancellationToken)
        {
            var (pot, error) = await _pots.JoinAsync(dto.Code, cancellationToken);

            if (error != null) return BadRequest(new { message = error });
            return pot is null ? NotFound() : Ok(pot);
        }

        [HttpPost("{id:guid}/leave")]
        public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
        {
            var (found, error) = await _pots.LeaveAsync(id, cancellationToken);

            if (error != null) return BadRequest(new { message = error });
            return found ? NoContent() : NotFound();
        }

        [HttpGet("{id:guid}/activity")]
        public async Task<IActionResult> GetActivity(Guid id, CancellationToken cancellationToken)
        {
            var activity = await _pots.GetActivityAsync(id, cancellationToken);
            return activity is null ? NotFound() : Ok(activity);
        }

        #region W2 — the ledger
        // Every write answers with the whole ledger rather than with the row it
        // wrote. The balance, the positions and the feed all move on every one
        // of these, and a client that had to re-fetch to find out would show a
        // stale figure next to a fresh one for as long as the round trip took —
        // on the screen whose entire promise is that the numbers agree.
        //
        // Deliberately not [Idempotent]: Stream O's outbox is for the personal
        // quick-add path, and pot expenses are entered from pot screens, online,
        // because allocating shares needs live membership.

        /// <summary>Balance, every member's position, and the pot's money feed.</summary>
        [HttpGet("{id:guid}/ledger")]
        public async Task<IActionResult> GetLedger(Guid id, CancellationToken cancellationToken)
        {
            var ledger = await _ledger.GetLedgerAsync(id, cancellationToken);
            return ledger is null ? NotFound() : Ok(ledger);
        }

        [HttpPost("{id:guid}/topups")]
        public Task<IActionResult> TopUp(Guid id, [FromBody] CreatePotTopUpDTO dto, CancellationToken cancellationToken) =>
            RespondAsync(_ledger.TopUpAsync(id, dto, cancellationToken));

        [HttpPost("{id:guid}/expenses")]
        public Task<IActionResult> LogExpense(Guid id, [FromBody] CreatePotExpenseDTO dto, CancellationToken cancellationToken) =>
            RespondAsync(_ledger.LogExpenseAsync(id, dto, cancellationToken));

        [HttpDelete("{id:guid}/expenses/{expenseId:guid}")]
        public Task<IActionResult> DeleteExpense(Guid id, Guid expenseId, CancellationToken cancellationToken) =>
            RespondAsync(_ledger.DeleteExpenseAsync(id, expenseId, cancellationToken));

        [HttpPost("{id:guid}/withdrawals")]
        public Task<IActionResult> Withdraw(Guid id, [FromBody] CreatePotWithdrawalDTO dto, CancellationToken cancellationToken) =>
            RespondAsync(_ledger.WithdrawAsync(id, dto, cancellationToken));

        /// <summary>
        /// The ledger's three-way answer, in one place: a sentence is a 400, a
        /// bare null is the 404 a non-member gets, and anything else is the
        /// fresh ledger.
        /// </summary>
        private static async Task<IActionResult> RespondAsync(Task<(PotLedgerDTO? Ledger, string? Error)> call)
        {
            var (ledger, error) = await call;

            if (error != null) return new BadRequestObjectResult(new { message = error });
            return ledger is null ? new NotFoundResult() : new OkObjectResult(ledger);
        }
        #endregion
    }
}
