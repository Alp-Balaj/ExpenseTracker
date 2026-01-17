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
    public class CategoryController : BaseController<Category, CategoryDTO>
    {
        public CategoryController(IUnitOfWork uow, IUserRelatedRepository<Category> repo)
            : base(uow, repo) { }
        protected override CategoryDTO ToDto(Category entity) => entity.ToDto();
        protected override Category ToEntity(CategoryDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Category entity, CategoryDTO dto) => entity.Apply(dto);
    }
}
