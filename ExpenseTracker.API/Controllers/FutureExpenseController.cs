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
    public class FutureExpenseController : BaseController<FutureExpense, FutureExpenseDTO>
    {
        private readonly IUserRelatedRepository<Category> _categoryRepository;

        public FutureExpenseController(IUnitOfWork uow, IUserRelatedRepository<FutureExpense> repo,
            IUserRelatedRepository<Category> categoryRepository)
            : base(uow, repo)
        {
            _categoryRepository = categoryRepository;
        }
        protected override FutureExpenseDTO ToDto(FutureExpense entity) => entity.ToDto();
        protected override FutureExpense ToEntity(FutureExpenseDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(FutureExpense entity, FutureExpenseDTO dto) => entity.Apply(dto);

        protected override async Task<string?> ValidateReferencesAsync(FutureExpenseDTO dto)
        {
            if (await _categoryRepository.GetUserDataByIdAsync(dto.CategoryId) == null)
                return "Category not found";
            return null;
        }
    }
}
