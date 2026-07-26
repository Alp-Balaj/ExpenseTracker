using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
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
    public class IncomeController : TransactionControllerBase<Income, IncomeDTO>
    {
        public IncomeController(IUnitOfWork uow, IUserRelatedRepository<Income> repo, IAccountServices accountServices)
            : base(uow, repo, accountServices)
        {
        }
        protected override IncomeDTO ToDto(Income entity) => entity.ToDto();
        protected override Income ToEntity(IncomeDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Income entity, IncomeDTO dto) => entity.Apply(dto);

        protected override int BalanceSign => +1;
        protected override decimal AmountOf(Income entity) => entity.Amount;
        protected override Guid AccountIdOf(Income entity) => entity.AccountId;
    }
}
