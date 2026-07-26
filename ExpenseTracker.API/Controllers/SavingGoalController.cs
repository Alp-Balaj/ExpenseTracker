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
        private readonly IUserRelatedRepository<Account> _accountRepository;
        private readonly IUserRelatedRepository<Category> _categoryRepository;

        public SavingGoalController(IUnitOfWork uow, IUserRelatedRepository<SavingGoal> repo,
            IUserRelatedRepository<Account> accountRepository,
            IUserRelatedRepository<Category> categoryRepository)
            : base(uow, repo)
        {
            _accountRepository = accountRepository;
            _categoryRepository = categoryRepository;
        }
        protected override SavingGoalDTO ToDto(SavingGoal entity) => entity.ToDto();
        protected override SavingGoal ToEntity(SavingGoalDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(SavingGoal entity, SavingGoalDTO dto) => entity.Apply(dto);

        protected override async Task<string?> ValidateReferencesAsync(SavingGoalDTO dto)
        {
            if (await _accountRepository.GetUserDataByIdAsync(dto.AccountId) == null)
                return "Account not found";
            if (await _categoryRepository.GetUserDataByIdAsync(dto.CategoryId) == null)
                return "Category not found";
            return null;
        }
    }
}
