using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.API.Controllers
{
    public class CategoryController : BaseController<Category, CategoryDTO>
    {
        public CategoryController(IUnitOfWork unitOfWork)
            : base(unitOfWork, unitOfWork.Categories)
        {
        }
    }
}
