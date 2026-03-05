using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SavingContributionController : BaseController<SavingContribution, SavingContributionDTO>
    {
        private readonly IUserRelatedRepository<SavingGoal> _savingGoalRepo;
        private readonly ISavingGoalServices _savingGoalServices;
        public SavingContributionController(IUnitOfWork uow, IUserRelatedRepository<SavingContribution> repo, 
        IUserRelatedRepository<SavingGoal> savingGoalRepo, ISavingGoalServices savingGoalServices)
            : base(uow, repo)
        {
            _savingGoalRepo = savingGoalRepo;
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
            var goal = await _savingGoalRepo.GetUserDataByIdAsync(goalId);
            
            if(goal == null)
                return BadRequest("Saving Goal wasn't found");

            var entity = contributionDto.ToEntity();

            entity.GoalId = goalId;

            await _repository.AddAsync(entity);
            
            await _savingGoalServices.UpdateSavingGoalAmount(goalId, entity.Amount);

            await _unitOfWork.SaveAsync();

            return Ok();
        }
    }
}
