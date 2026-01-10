using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.API.Controllers
{
    public class AccountController : BaseController<Account, AccountDTO>
    {
        public AccountController(IUnitOfWork uow, IUserRelatedRepository<Account> repo)
            : base(uow, repo) { }
        protected override AccountDTO ToDto(Account entity) => entity.ToDto();
        protected override Account ToEntity(AccountDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Account entity, AccountDTO dto) => entity.Apply(dto);
    }
}
