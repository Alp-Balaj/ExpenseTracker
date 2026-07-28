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
    public class CategoryController : BaseController<Category, CategoryDTO>
    {
        private readonly ICategoryService _categoryService;
        public CategoryController(IUnitOfWork uow, IUserRelatedRepository<Category> repo, ICategoryService categoryService)
            : base(uow, repo) 
        {
            _categoryService = categoryService;
        }

        // Unlike its siblings this can't window in SQL: a summary is only whole
        // once its totals are converted into the base currency, which happens
        // after the aggregates come back. The list is bounded by how many
        // categories a user defines, so the window is applied to the result.
        [HttpGet]
        public override async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            var summaries = await _categoryService.GetCategorySummariesAsync(cancellationToken);

            SetTotalCount(summaries.Count);

            IEnumerable<CategorySummaryDTO> window = summaries;

            if (page.Skip is int skip and > 0)
                window = window.Skip(skip);

            if (page.Take is int take)
                window = window.Take(take);

            return Ok(window.ToList());
        }

        protected override CategoryDTO ToDto(Category entity) => entity.ToDto();
        protected override Category ToEntity(CategoryDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Category entity, CategoryDTO dto) => entity.Apply(dto);
    }
}
