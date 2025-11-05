using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;

namespace ExpenseTracker.API.Controllers
{
    public class ExpenseController : BaseController<Expense, ExpenseDTO>
    {
        public ExpenseController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Expenses)
        {
        }
    }
}
