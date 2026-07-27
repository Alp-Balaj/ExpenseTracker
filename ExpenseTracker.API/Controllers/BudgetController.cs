using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BudgetController : BaseController<Budget, BudgetDTO>
    {
        private readonly IUserRelatedRepository<Category> _categoryRepository;
        private readonly IBudgetService _budgetService;

        // Set by Update before it delegates to the base implementation, so the
        // duplicate check can exclude the row being edited. Null on create.
        private Guid? _idBeingUpdated;

        public BudgetController(
            IUnitOfWork uow,
            IUserRelatedRepository<Budget> repo,
            IUserRelatedRepository<Category> categoryRepository,
            IBudgetService budgetService)
            : base(uow, repo)
        {
            _categoryRepository = categoryRepository;
            _budgetService = budgetService;
        }

        protected override BudgetDTO ToDto(Budget entity) => entity.ToDto();
        protected override Budget ToEntity(BudgetDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Budget entity, BudgetDTO dto) => entity.Apply(dto);

        [HttpPut("{id:guid}")]
        public override Task<IActionResult> Update(Guid id, [FromBody] BudgetDTO dto)
        {
            _idBeingUpdated = id;
            return base.Update(id, dto);
        }

        protected override async Task<string?> ValidateReferencesAsync(BudgetDTO dto)
        {
            if (dto.CategoryId is Guid categoryId)
            {
                var category = await _categoryRepository.GetUserDataByIdAsync(categoryId);
                if (category == null)
                    return "Category not found";

                // Spend is aggregated from expenses only, so a budget on any
                // other category type could never move off zero.
                if (category.CategoryType != CategoryType.Expense)
                    return "Budgets can only be set on expense categories";
            }

            var period = (BudgetPeriod)dto.Period;
            var existing = await _repository.GetAllUserDataAsync();

            var duplicate = existing.Any(b =>
                b.Id != _idBeingUpdated &&
                b.Period == period &&
                b.CategoryId == dto.CategoryId);

            if (duplicate)
                return dto.CategoryId is null
                    ? "An overall budget already exists"
                    : "A budget already exists for this category";

            return null;
        }

        /// <summary>
        /// Every budget measured against one month's spend.
        /// <c>month</c> is <c>yyyy-MM</c> and defaults to the current month.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus([FromQuery] string? month)
        {
            int year, monthNumber;

            if (string.IsNullOrWhiteSpace(month))
            {
                var today = DateTime.UtcNow;
                year = today.Year;
                monthNumber = today.Month;
            }
            else if (!BudgetService.TryParseMonth(month, out year, out monthNumber))
            {
                return BadRequest("month must be in yyyy-MM format.");
            }

            var statuses = await _budgetService.GetStatusAsync(year, monthNumber);
            return Ok(statuses);
        }
    }
}
