using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;

namespace ExpenseTracker.API.Controllers
{
    public class IncomeController : BaseController<Income, IncomeDTO>
    {
        public IncomeController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Incomes)
        {
        }
    }
}
