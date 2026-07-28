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

        // Accounts need their Currency navigation loaded, so this goes through
        // the dedicated repository rather than the generic one — same paging
        // contract as every other list endpoint.
        [HttpGet]
        public override async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            var (items, totalCount) = await _accountRepository.GetUserDataPageWithCurrencyAsync(
                page.Skip, page.Take, cancellationToken);

            SetTotalCount(totalCount);

            var dtos = items.Select(e => ToDto(e)).ToList();

            return Ok(dtos);
        }

        [HttpGet("{id:guid}")]
        public override async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _accountRepository.GetUserDataWithCurrencyByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();
            return Ok(ToDto(entity));
        }

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] AccountDTO dto, CancellationToken cancellationToken)
        {
            var entity = ToEntity(dto);
            await _repository.AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveAsync(cancellationToken);

            // Re-query so the Currency navigation is loaded for the response DTO;
            // EF returns the tracked instance with the navigation fixed up.
            var saved = await _accountRepository.GetUserDataWithCurrencyByIdAsync(entity.Id, cancellationToken) ?? entity;
            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(saved));
        }
    }
}
