using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;

namespace ExpenseTracker.API.Controllers
{
    public class FutureExpenseController : BaseController<FutureExpense, FutureExpenseDTO>
    {
        public FutureExpenseController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.FutureExpenses)
        {
        }
    }
}
