using ExpenseTracker.Application.DTOs;
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
    public class SavingGoalController : BaseController<SavingGoal, SavingGoalDTO>
    {
        public SavingGoalController(IUnitOfWork uow, IUserRelatedRepository<SavingGoal> repo)
            : base(uow, repo) { }
        protected override SavingGoalDTO ToDto(SavingGoal entity) => entity.ToDto();
        protected override SavingGoal ToEntity(SavingGoalDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(SavingGoal entity, SavingGoalDTO dto) => entity.Apply(dto);
    }
}
