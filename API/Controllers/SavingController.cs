using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;

namespace ExpenseTracker.API.Controllers
{
    public class SavingController : BaseController<Saving, SavingDTO>
    {
        public SavingController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Savings)
        {
        }
    }
}
