using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.API.Controllers
{
    public class CategoryController : BaseController<Category, CategoryDTO>
    {
        public CategoryController(IUnitOfWork uow, IUserRelatedRepository<Category> repo)
            : base(uow, repo) { }
        protected override CategoryDTO ToDto(Category entity) => entity.ToDto();
        protected override Category ToEntity(CategoryDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Category entity, CategoryDTO dto) => entity.Apply(dto);
    }
}
