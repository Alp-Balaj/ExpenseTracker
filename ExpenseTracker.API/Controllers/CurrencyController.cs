using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using AutoMapper;

namespace ExpenseTracker.API.Controllers
{
    public class CurrencyController : BaseController<Currency, CurrencyDTO>
    {
        public CurrencyController(IUnitOfWork unitOfWork, IMapper mapper)
            : base(unitOfWork, mapper, unitOfWork.Currencies)
        {
        }
    }
}
