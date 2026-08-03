using ExpenseTracker.API.Filters;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    // Base for account-backed transactions (Expense, Income): every create,
    // update, delete and account move is mirrored into the account balance,
    // inside one DB transaction with the row change itself.
    public abstract class TransactionControllerBase<TEntity, TDto> : BaseController<TEntity, TDto>
        where TEntity : BaseEntity, new()
        where TDto : class
    {
        protected readonly IAccountServices _accountServices;

        protected TransactionControllerBase(IUnitOfWork unitOfWork, IUserRelatedRepository<TEntity> repository, IAccountServices accountServices)
            : base(unitOfWork, repository)
        {
            _accountServices = accountServices;
        }

        // Effect of one unit of this transaction on its account balance:
        // -1 for expenses, +1 for incomes.
        protected abstract int BalanceSign { get; }
        protected abstract decimal AmountOf(TEntity entity);
        protected abstract Guid AccountIdOf(TEntity entity);

        #region Satellite-row hooks (Stream R)
        // A subclass may keep rows of its own alongside the transaction —
        // ExpenseController keeps a Reimbursement. These run inside the very
        // same DB transaction and are flushed by the very same SaveAsync, so
        // there is no window in which one exists without the other.
        //
        // Returning a message aborts the request with that string as a 400 and
        // lets the transaction roll back on dispose, exactly as a failed
        // balance adjustment already does. The defaults return null and touch
        // nothing, so a subclass that overrides none of them — IncomeController
        // — runs the identical sequence of statements it ran before.

        /// <summary>After the row is added and before the flush.</summary>
        protected virtual Task<string?> OnCreatingAsync(TEntity entity, TDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        /// <summary>After the updates are applied and the row is marked, before the flush.</summary>
        protected virtual Task<string?> OnUpdatingAsync(TEntity entity, TDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        /// <summary>
        /// After the row's own balance effect is reversed and before it is
        /// removed, so anything else it moved can be reversed in the same
        /// transaction.
        /// </summary>
        protected virtual Task<string?> OnDeletingAsync(TEntity entity, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        #endregion

        // Stream O: the quick-add write path. Inert without an Idempotency-Key
        // header, so the ordinary online SPA is unaffected; the offline outbox
        // sends one so a replayed POST cannot post the same expense twice.
        [HttpPost]
        [Idempotent]
        public override async Task<IActionResult> Create([FromBody] TDto dto, CancellationToken cancellationToken)
        {
            var referenceError = await ValidateReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            var entity = ToEntity(dto);

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var (Success, ErrorMessage) = await _accountServices.AdjustAccountBalance(BalanceSign * AmountOf(entity), AccountIdOf(entity), cancellationToken);

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity, cancellationToken);

            var createHookError = await OnCreatingAsync(entity, dto, cancellationToken);
            if (createHookError != null) return BadRequest(createHookError);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] TDto dto, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            var referenceError = await ValidateReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            var oldAmount = AmountOf(entity);
            var oldAccountId = AccountIdOf(entity);

            ApplyUpdates(entity, dto);

            var newAmount = AmountOf(entity);
            var newAccountId = AccountIdOf(entity);

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            if (newAccountId != oldAccountId)
            {
                var reverse = await _accountServices.AdjustAccountBalance(-BalanceSign * oldAmount, oldAccountId, cancellationToken);
                if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

                var apply = await _accountServices.AdjustAccountBalance(BalanceSign * newAmount, newAccountId, cancellationToken);
                if (!apply.Success) return BadRequest(apply.ErrorMessage);
            }
            else
            {
                var adjust = await _accountServices.AdjustAccountBalance(BalanceSign * (newAmount - oldAmount), oldAccountId, cancellationToken);
                if (!adjust.Success) return BadRequest(adjust.ErrorMessage);
            }

            _repository.UpdateUserData(entity);

            var updateHookError = await OnUpdatingAsync(entity, dto, cancellationToken);
            if (updateHookError != null) return BadRequest(updateHookError);

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

            var reverse = await _accountServices.AdjustAccountBalance(-BalanceSign * AmountOf(entity), AccountIdOf(entity), cancellationToken);
            if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

            var deleteHookError = await OnDeletingAsync(entity, cancellationToken);
            if (deleteHookError != null) return BadRequest(deleteHookError);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
    }
}
