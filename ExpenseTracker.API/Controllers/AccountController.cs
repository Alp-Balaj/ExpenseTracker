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
    public class AccountController : BaseController<Account, AccountDTO>
    {
        private readonly IAccountRepository _accountRepository;
        public AccountController(IUnitOfWork uow, IUserRelatedRepository<Account> repo, IAccountRepository accountRepository)
            : base(uow, repo)
        {
            _accountRepository = accountRepository;
        }
        protected override AccountDTO ToDto(Account entity) => entity.ToDto();
        protected override Account ToEntity(AccountDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Account entity, AccountDTO dto) => entity.Apply(dto);

        [HttpGet]
        public override async Task<IActionResult> GetAll()
        {
            var items = await _accountRepository.GetAllUserDataWithCurrencyAsync();

            var dtos = items.Select(e => ToDto(e)).ToList();

            return Ok(dtos);
        }
    }
}
