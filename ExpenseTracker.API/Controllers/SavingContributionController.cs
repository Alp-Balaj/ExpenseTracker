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
        public SavingContributionController(IUnitOfWork uow, IUserRelatedRepository<SavingContribution> repo,
        ISavingGoalServices savingGoalServices)
            : base(uow, repo)
        {
            _savingGoalServices = savingGoalServices;
        }
        protected override SavingContributionDTO ToDto(SavingContribution entity) => entity.ToDto();
        protected override SavingContribution ToEntity(SavingContributionDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(SavingContribution entity, SavingContributionDTO dto) => entity.Apply(dto);

        [HttpPost("{goalId:guid}")]
        public async Task<IActionResult> AddContribution(
            Guid goalId,
            [FromBody] SavingContributionDTO contributionDto
        )
        {
            var entity = contributionDto.ToEntity();

            entity.GoalId = goalId;

            return await CreateGuarded(entity);
        }

        // Generic POST route: routed through the same guarded logic using the
        // GoalId carried in the body, so it can't create a contribution the
        // goal total doesn't account for.
        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] SavingContributionDTO dto)
        {
            return await CreateGuarded(ToEntity(dto));
        }

        private async Task<IActionResult> CreateGuarded(SavingContribution entity)
        {
            if (entity.Amount <= 0)
                return BadRequest("Amount must be greater than zero.");

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var (Success, ErrorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount);

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public override async Task<IActionResult> Update(Guid id, [FromBody] SavingContributionDTO dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            var oldAmount = entity.Amount;
            var oldGoalId = entity.GoalId;

            ApplyUpdates(entity, dto);

            if (entity.Amount <= 0)
                return BadRequest("Amount must be greater than zero.");

            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            if (entity.GoalId != oldGoalId)
            {
                var reverse = await _savingGoalServices.UpdateSavingGoalAmount(oldGoalId, -oldAmount);
                if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

                var apply = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount);
                if (!apply.Success) return BadRequest(apply.ErrorMessage);
            }
            else
            {
                var adjust = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, entity.Amount - oldAmount);
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

            var reverse = await _savingGoalServices.UpdateSavingGoalAmount(entity.GoalId, -entity.Amount);
            if (!reverse.Success) return BadRequest(reverse.ErrorMessage);

            _repository.DeleteUserData(entity);

            await _unitOfWork.SaveAsync();
            await transaction.CommitAsync();

            return NoContent();
        }
    }
}
