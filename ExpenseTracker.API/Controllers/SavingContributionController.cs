using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    // Every contribution create, update, delete and goal move is mirrored
    // into the goal's SavedAmount, inside one DB transaction with the row
    // change itself.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SavingContributionController : BaseController<SavingContribution, SavingContributionDTO>
    {
        private readonly ISavingGoalServices _savingGoalServices;
        private readonly ISavingsReservationService _reservations;
        public SavingContributionController(IUnitOfWork uow, IUserRelatedRepository<SavingContribution> repo,
        ISavingGoalServices savingGoalServices,
        ISavingsReservationService reservations)
            : base(uow, repo)
        {
            _savingGoalServices = savingGoalServices;
            _reservations = reservations;
        }
        protected override SavingContributionDTO ToDto(SavingContribution entity) => entity.ToDto();
        protected override SavingContribution ToEntity(SavingContributionDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(SavingContribution entity, SavingContributionDTO dto) => entity.Apply(dto);

        // A goal's history is read newest first.
        protected override IOrderedQueryable<SavingContribution> OrderForListing(IQueryable<SavingContribution> query) =>
            query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id);

        [HttpPost("{goalId:guid}")]
        public async Task<IActionResult> AddContribution(
            Guid goalId,
            [FromBody] SavingContributionDTO contributionDto,
            CancellationToken cancellationToken
        )
        {
            var entity = contributionDto.ToEntity();

            entity.GoalId = goalId;

            return await CreateGuarded(entity, cancellationToken);
        }

        // Generic POST route: routed through the same guarded logic using the
        // GoalId carried in the body, so it can't create a contribution the
        // goal total doesn't account for.
        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] SavingContributionDTO dto, CancellationToken cancellationToken)
        {
            return await CreateGuarded(ToEntity(dto), cancellationToken);
        }

        private async Task<IActionResult> CreateGuarded(SavingContribution entity, CancellationToken cancellationToken)
        {
            // Which account the money is held against is not decoration: it is
            // what makes "reserved" subtractable from a balance, so it is
            // checked before anything is written.
            var accountError = await _reservations.ValidateContributionAccountAsync(
                entity.GoalId, entity.AccountId, ignoreContributionId: null, cancellationToken);
            if (accountError != null) return BadRequest(accountError);

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var (Success, ErrorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount, cancellationToken);

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] SavingContributionDTO dto, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            var oldAmount = entity.Amount;
            var oldGoalId = entity.GoalId;

            ApplyUpdates(entity, dto);

            // The row being edited is excluded from the goal's currency, so the
            // only contribution a goal has can always be corrected — including
            // onto an account in a different currency.
            var accountError = await _reservations.ValidateContributionAccountAsync(
                entity.GoalId, entity.AccountId, ignoreContributionId: entity.Id, cancellationToken);
            if (accountError != null) return BadRequest(accountError);

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            if (entity.GoalId != oldGoalId)
            {
                var reverse = await _savingGoalServices.UpdateSavingGoalAmount(oldGoalId, -oldAmount, cancellationToken);
                if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

                var apply = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount, cancellationToken);
                if (!apply.Success) return BadRequest(apply.ErrorMessage);
            }
            else
            {
                var adjust = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount - oldAmount, cancellationToken);
                if (!adjust.Success) return BadRequest(adjust.ErrorMessage);
            }

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

            var reverse = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, -entity.Amount, cancellationToken);
            if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
    }
}
