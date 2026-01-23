using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

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

        [HttpGet]
        public override async Task<IActionResult> GetAll()
        {
            var result = new List<CategorySummaryDTO>();

            foreach (var type in Enum.GetValues<CategoryType>())
            {
                var items = await _categoryService.GetCategorySummariesAsync(type);
                result.AddRange(items);
            }

            return Ok(result);
        }

        protected override CategoryDTO ToDto(Category entity) => entity.ToDto();
        protected override Category ToEntity(CategoryDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Category entity, CategoryDTO dto) => entity.Apply(dto);
    }
}
