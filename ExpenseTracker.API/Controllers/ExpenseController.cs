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
    public class ExpenseController : TransactionControllerBase<Expense, ExpenseDTO>
    {
        public ExpenseController(IUnitOfWork uow, IUserRelatedRepository<Expense> repo, IAccountServices accountServices)
            : base(uow, repo, accountServices)
        {
        }
        protected override ExpenseDTO ToDto(Expense entity) => entity.ToDto();
        protected override Expense ToEntity(ExpenseDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Expense entity, ExpenseDTO dto) => entity.Apply(dto);

        protected override int BalanceSign => -1;
        protected override decimal AmountOf(Expense entity) => entity.Amount;
        protected override Guid AccountIdOf(Expense entity) => entity.AccountId;
    }
}
