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
        private readonly IPotRepository _pots;

        public CategoryController(
            IUnitOfWork uow,
            IUserRelatedRepository<Category> repo,
            ICategoryService categoryService,
            IPotRepository pots)
            : base(uow, repo)
        {
            _categoryService = categoryService;
            _pots = pots;
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

        /// <summary>
        /// Cycle U W2: the reserved "Shared" category cannot be deleted while
        /// any pot expense share still points at it.
        ///
        /// <para>
        /// This is the one guard the generic <c>Delete</c> could not carry,
        /// because until this wave nothing in the application could put a
        /// category beyond its owner's reach. A share is not a row this user can
        /// see the other side of — it belongs to a group — so removing the
        /// category it lands in would leave their own pot spending with nowhere
        /// to appear, in a budget they did not know they were breaking.
        /// </para>
        ///
        /// <para>
        /// It is a refusal and not a cascade on purpose: a member who has never
        /// spent from a pot may still delete the category, and one who has can
        /// have it back to normal by leaving their pots and deleting their
        /// history. Nothing here is permanent that the user did not make
        /// permanent.
        /// </para>
        /// </summary>
        [HttpDelete("{id:guid}")]
        public override async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var category = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (category == null) return NotFound();

            if (category.IsSystemOwned && await _pots.AnySharesInCategoryAsync(id, cancellationToken))
                return BadRequest(new
                {
                    message = "Your pots put your share of every shared expense in this category, "
                              + "so it cannot be deleted while any of them is still there."
                });

            return await base.Delete(id, cancellationToken);
        }

        protected override CategoryDTO ToDto(Category entity) => entity.ToDto();
        protected override Category ToEntity(CategoryDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Category entity, CategoryDTO dto) => entity.Apply(dto);
    }
}
