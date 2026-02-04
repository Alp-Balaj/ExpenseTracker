using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ExpenseController : BaseController<Expense, ExpenseDTO>
    {
        private readonly IAccountServices _accountServices;
        private readonly IUnitOfWork _uow;
        public ExpenseController(IUnitOfWork uow, IUserRelatedRepository<Expense> repo, IAccountServices accountServices)
            : base(uow, repo) 
        {
            _uow = uow;
            _accountServices = accountServices;
        }
        protected override ExpenseDTO ToDto(Expense entity) => entity.ToDto();
        protected override Expense ToEntity(ExpenseDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Expense entity, ExpenseDTO dto) => entity.Apply(dto);

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] ExpenseDTO dto)
        {
            var entity = ToEntity(dto);
            
            if (entity.Amount < 0)
                return BadRequest("Amount cannot be negative.");

            var (Success, ErrorMessage) = await _accountServices.DecrementAmountFromAccount(entity.Amount, entity.AccountId);

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity);

            await _uow.SaveAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }
    }
}
