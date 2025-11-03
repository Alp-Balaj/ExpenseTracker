using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    public class ExpensesController : BaseController<Expense, ExpenseDTO>
    {
        public ExpensesController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Expenses)
        {
        }
    }
}
