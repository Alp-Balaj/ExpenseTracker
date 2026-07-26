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

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] TDto dto)
        {
            var entity = ToEntity(dto);

            if (AmountOf(entity) <= 0)
                return BadRequest("Amount must be greater than zero.");

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var (Success, ErrorMessage) = await _accountServices.AdjustAccountBalance(BalanceSign * AmountOf(entity), AccountIdOf(entity));

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] TDto dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            var oldAmount = AmountOf(entity);
            var oldAccountId = AccountIdOf(entity);

            ApplyUpdates(entity, dto);

            var newAmount = AmountOf(entity);
            var newAccountId = AccountIdOf(entity);

            if (newAmount <= 0)
                return BadRequest("Amount must be greater than zero.");

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            if (newAccountId != oldAccountId)
            {
                var reverse = await _accountServices.AdjustAccountBalance(-BalanceSign * oldAmount, oldAccountId);
                if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

                var apply = await _accountServices.AdjustAccountBalance(BalanceSign * newAmount, newAccountId);
                if (!apply.Success) return BadRequest(apply.ErrorMessage);
            }
            else
            {
                var adjust = await _accountServices.AdjustAccountBalance(BalanceSign * (newAmount - oldAmount), oldAccountId);
                if (!adjust.Success) return BadRequest(adjust.ErrorMessage);
            }

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

            var reverse = await _accountServices.AdjustAccountBalance(-BalanceSign * AmountOf(entity), AccountIdOf(entity));
            if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return NoContent();
        }
    }
}
