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
        private readonly IAccountServices _accountServices;
        private readonly ISavingGoalServices _savingGoalServices;
        private readonly IAccountRepository _accountRepository;
        private readonly IUserRelatedRepository<SavingGoal> _savingGoalRepository;
        private readonly IUserRelatedRepository<SavingContribution> _contributionRepository;

        public TransferController(
            IUnitOfWork uow,
            IUserRelatedRepository<Transfer> repo,
            IAccountServices accountServices,
            ISavingGoalServices savingGoalServices,
            IAccountRepository accountRepository,
            IUserRelatedRepository<SavingGoal> savingGoalRepository,
            IUserRelatedRepository<SavingContribution> contributionRepository)
            : base(uow, repo)
        {
            _accountServices = accountServices;
            _savingGoalServices = savingGoalServices;
            _accountRepository = accountRepository;
            _savingGoalRepository = savingGoalRepository;
            _contributionRepository = contributionRepository;
        }

        protected override TransferDTO ToDto(Transfer entity) => entity.ToDto();
        protected override Transfer ToEntity(TransferDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Transfer entity, TransferDTO dto) => entity.Apply(dto);

        // Both accounts are loaded (not just balance-adjusted) because v1 needs
        // their currencies: "Account not found" covers both the missing and the
        // someone-else's case, so it never confirms another user's ids.
        protected override async Task<string?> ValidateReferencesAsync(TransferDTO dto)
        {
            if (dto.FromAccountId == dto.ToAccountId)
                return "A transfer must be between two different accounts";

            var from = await _accountRepository.GetUserDataWithCurrencyByIdAsync(dto.FromAccountId);
            if (from == null) return "Account not found";

            var to = await _accountRepository.GetUserDataWithCurrencyByIdAsync(dto.ToAccountId);
            if (to == null) return "Account not found";

            // v1 carries a single amount, which only describes both sides of the
            // move when the two accounts agree on the currency. Converting here
            // would need a received-amount the client hasn't given us.
            if (from.CurrencyId != to.CurrencyId)
                return "Transfers must be between accounts with the same currency";

            if (dto.SavingGoalId is Guid goalId)
            {
                var goal = await _savingGoalRepository.GetUserDataByIdAsync(goalId);
                if (goal == null)
                    return "Saving goal not found";

                // The goal's saved amount is supposed to track money actually
                // sitting in its account, so only a transfer landing there counts.
                if (goal.AccountId != dto.ToAccountId)
                    return "The savings goal must be backed by the destination account";
            }

            return null;
        }

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] TransferDTO dto)
        {
            var referenceError = await ValidateReferencesAsync(dto);
            if (referenceError != null) return BadRequest(referenceError);

            var entity = ToEntity(dto);

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var error = await ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, +1);
            if (error != null) return BadRequest(error);

            error = await LinkContributionAsync(entity);
            if (error != null) return BadRequest(error);

            await _repository.AddAsync(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] TransferDTO dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            var referenceError = await ValidateReferencesAsync(dto);
            if (referenceError != null) return BadRequest(referenceError);

            // Snapshot before ApplyUpdates overwrites the old values: the reversal
            // has to undo what was actually booked, not what is being asked for.
            var bookedAmount = entity.Amount;
            var bookedFromAccountId = entity.FromAccountId;
            var bookedToAccountId = entity.ToAccountId;
            var bookedContributionId = entity.SavingContributionId;

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var error = await ApplyMoveAsync(bookedAmount, bookedFromAccountId, bookedToAccountId, -1);
            if (error != null) return BadRequest(error);

            // The old contribution is dropped and a fresh one booked rather than
            // edited in place. An edit can change the goal, the amount and the
            // date at once; recreating keeps one code path for every combination,
            // at the cost of a new contribution id.
            error = await UnlinkContributionAsync(bookedContributionId);
            if (error != null) return BadRequest(error);

            ApplyUpdates(entity, dto);
            entity.SavingContributionId = null;

            error = await ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, +1);
            if (error != null) return BadRequest(error);

            error = await LinkContributionAsync(entity);
            if (error != null) return BadRequest(error);

            _repository.UpdateUserData(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public override async Task<IActionResult> Delete(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var error = await ApplyMoveAsync(entity.Amount, entity.FromAccountId, entity.ToAccountId, -1);
            if (error != null) return BadRequest(error);

            error = await UnlinkContributionAsync(entity.SavingContributionId);
            if (error != null) return BadRequest(error);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return NoContent();
        }

        // Moves an amount between two accounts: sign +1 books the move,
        // -1 reverses it. Both legs are user-scoped atomic UPDATEs (Fix-2's
        // mechanism), and the caller's DB transaction is what keeps them paired.
        private async Task<string?> ApplyMoveAsync(decimal amount, Guid fromAccountId, Guid toAccountId, int sign)
        {
            var debit = await _accountServices.AdjustAccountBalance(-sign * amount, fromAccountId);
            if (!debit.Success) return debit.ErrorMessage;

            var credit = await _accountServices.AdjustAccountBalance(sign * amount, toAccountId);
            if (!credit.Success) return credit.ErrorMessage;

            return null;
        }

        // Books the savings contribution for a transfer that names a goal, and
        // stamps the transfer with its id. No goal named: nothing to do.
        private async Task<string?> LinkContributionAsync(Transfer transfer)
        {
            if (transfer.SavingGoalId is not Guid goalId)
                return null;

            var contribution = new SavingContribution
            {
                UserId = null!,
                GoalId = goalId,
                Amount = transfer.Amount,
                Date = transfer.Date,
                Note = transfer.Note,
                SavingGoal = null!
            };

            var (success, errorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(goalId, transfer.Amount);
            if (!success) return errorMessage;

            await _contributionRepository.AddAsync(contribution);
            transfer.SavingContributionId = contribution.Id;

            return null;
        }

        // Reverses whatever LinkContributionAsync booked. A null id means the
        // transfer never had a link; a non-null id with no row behind it means
        // the contribution was deleted on its own (the FK nulls out the link
        // afterwards), in which case the goal total was already corrected by
        // that delete and there is nothing left to reverse.
        private async Task<string?> UnlinkContributionAsync(Guid? savingContributionId)
        {
            if (savingContributionId is not Guid contributionId)
                return null;

            var contribution = await _contributionRepository.GetUserDataByIdAsync(contributionId);
            if (contribution == null) return null;

            var (success, errorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(contribution.GoalId, -contribution.Amount);
            if (!success) return errorMessage;

            _contributionRepository.DeleteUserData(contribution);

            return null;
        }
    }
}
