using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Money moved between two of the user's own accounts. Unlike
    /// <see cref="ExpenseController"/> / <see cref="IncomeController"/> this
    /// touches two balances at once, so it can't reuse
    /// <see cref="TransactionControllerBase{TEntity, TDto}"/>'s single-account
    /// mechanism — but it follows the same rule: every balance move happens
    /// inside one DB transaction with the row change itself.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TransferController : BaseController<Transfer, TransferDTO>
    {
        private readonly ITransferPostingService _posting;
        private readonly IAccountRepository _accountRepository;
        private readonly IUserRelatedRepository<SavingGoal> _savingGoalRepository;
        private readonly ISavingsReservationService _reservations;

        public TransferController(
            IUnitOfWork uow,
            IUserRelatedRepository<Transfer> repo,
            ITransferPostingService posting,
            IAccountRepository accountRepository,
            IUserRelatedRepository<SavingGoal> savingGoalRepository,
            ISavingsReservationService reservations)
            : base(uow, repo)
        {
            _posting = posting;
            _accountRepository = accountRepository;
            _savingGoalRepository = savingGoalRepository;
            _reservations = reservations;
        }

        protected override TransferDTO ToDto(Transfer entity) => entity.ToDto();
        protected override Transfer ToEntity(TransferDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Transfer entity, TransferDTO dto) => entity.Apply(dto);

        // Newest move first, matching the (UserId, Date) index the transfer
        // table already carries.
        protected override IOrderedQueryable<Transfer> OrderForListing(IQueryable<Transfer> query) =>
            query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id);

        // Both accounts are loaded (not just balance-adjusted) because v1 needs
        // their currencies: "Account not found" covers both the missing and the
        // someone-else's case, so it never confirms another user's ids.
        protected override Task<string?> ValidateReferencesAsync(TransferDTO dto, CancellationToken cancellationToken = default) =>
            ValidateReferencesAsync(dto, ignoreContributionId: null, cancellationToken);

        /// <param name="ignoreContributionId">
        /// The contribution this transfer already booked, when the transfer is
        /// being edited. Update tears the old contribution down and books a new
        /// one, so leaving it in would let a goal's own only contribution veto
        /// its own correction.
        /// </param>
        private async Task<string?> ValidateReferencesAsync(TransferDTO dto, Guid? ignoreContributionId, CancellationToken cancellationToken = default)
        {
            if (dto.FromAccountId == dto.ToAccountId)
                return "A transfer must be between two different accounts";

            var from = await _accountRepository.GetUserDataWithCurrencyByIdAsync(dto.FromAccountId, cancellationToken);
            if (from == null) return "Account not found";

            var to = await _accountRepository.GetUserDataWithCurrencyByIdAsync(dto.ToAccountId, cancellationToken);
            if (to == null) return "Account not found";

            // v1 carries a single amount, which only describes both sides of the
            // move when the two accounts agree on the currency. Converting here
            // would need a received-amount the client hasn't given us.
            if (from.CurrencyId != to.CurrencyId)
                return "Transfers must be between accounts with the same currency";

            if (dto.SavingGoalId is Guid goalId)
            {
                var goal = await _savingGoalRepository.GetUserDataByIdAsync(goalId, cancellationToken);
                if (goal == null)
                    return "Saving goal not found";

                // Stream V: a goal is no longer backed by one account, so any
                // account may credit it — the contribution simply books against
                // the destination, where the money actually landed. What still
                // holds is the currency: a goal reserves in one currency so that
                // "reserved" stays subtractable from a balance.
                var accountError = await _reservations.ValidateContributionAccountAsync(
                    goalId, dto.ToAccountId, ignoreContributionId, cancellationToken);
                if (accountError != null) return accountError;
            }

            return null;
        }

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] TransferDTO dto, CancellationToken cancellationToken)
        {
            var referenceError = await ValidateReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            var entity = ToEntity(dto);

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var error = await _posting.ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, +1, cancellationToken);
            if (error != null) return BadRequest(error);

            error = await _posting.LinkContributionAsync(entity, cancellationToken);
            if (error != null) return BadRequest(error);

            await _repository.AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] TransferDTO dto, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            var referenceError = await ValidateReferencesAsync(dto, entity.SavingContributionId, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            // Snapshot before ApplyUpdates overwrites the old values: the reversal
            // has to undo what was actually booked, not what is being asked for.
            var bookedAmount = entity.Amount;
            var bookedFromAccountId = entity.FromAccountId;
            var bookedToAccountId = entity.ToAccountId;
            var bookedContributionId = entity.SavingContributionId;

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var error = await _posting.ApplyMoveAsync(bookedAmount, bookedFromAccountId, bookedToAccountId, -1, cancellationToken);
            if (error != null) return BadRequest(error);

            // The old contribution is dropped and a fresh one booked rather than
            // edited in place. An edit can change the goal, the amount and the
            // date at once; recreating keeps one code path for every combination,
            // at the cost of a new contribution id.
            error = await _posting.UnlinkContributionAsync(bookedContributionId, cancellationToken);
            if (error != null) return BadRequest(error);

            ApplyUpdates(entity, dto);
            entity.SavingContributionId = null;

            error = await _posting.ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, +1, cancellationToken);
            if (error != null) return BadRequest(error);

            error = await _posting.LinkContributionAsync(entity, cancellationToken);
            if (error != null) return BadRequest(error);

            _repository.UpdateUserData(entity);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public override async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var error = await _posting.ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, -1, cancellationToken);
            if (error != null) return BadRequest(error);

            error = await _posting.UnlinkContributionAsync(entity.SavingContributionId, cancellationToken);
            if (error != null) return BadRequest(error);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
    }
}
