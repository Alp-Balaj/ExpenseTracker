using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    public class IncomesController : BaseController<Income, IncomeDTO>
    {
        public IncomesController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Incomes)
        {
        }
    }
}
