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

        [HttpGet("{id:guid}")]
        public override async Task<IActionResult> Get(Guid id)
        {
            var entity = await _accountRepository.GetUserDataWithCurrencyByIdAsync(id);
            if (entity == null) return NotFound();
            return Ok(ToDto(entity));
        }

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] AccountDTO dto)
        {
            var entity = ToEntity(dto);
            await _repository.AddAsync(entity);
            await _unitOfWork.SaveAsync();

            // Re-query so the Currency navigation is loaded for the response DTO;
            // EF returns the tracked instance with the navigation fixed up.
            var saved = await _accountRepository.GetUserDataWithCurrencyByIdAsync(entity.Id) ?? entity;
            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(saved));
        }
    }
}
