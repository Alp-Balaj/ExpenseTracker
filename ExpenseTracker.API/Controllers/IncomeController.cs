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
    public class IncomeController : BaseController<Income, IncomeDTO>
    {
        private readonly IAccountServices _accountServices;
        private readonly IUnitOfWork _uow;
        public IncomeController(IUnitOfWork uow, IUserRelatedRepository<Income> repo,  IAccountServices accountServices)
            : base(uow, repo)
        {
            _uow = uow;
            _accountServices = accountServices;
        }
        protected override IncomeDTO ToDto(Income entity) => entity.ToDto();
        protected override Income ToEntity(IncomeDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Income entity, IncomeDTO dto) => entity.Apply(dto);

        [HttpPost]
        public override async Task<IActionResult> Create([FromBody] IncomeDTO dto)
        {
            var entity = ToEntity(dto);
            
            if (entity.Amount < 0)
                return BadRequest("Amount cannot be negative.");

            var (Success, ErrorMessage) = await _accountServices.IncrementAmountToAccount(entity.Amount, entity.AccountId);

            if (!Success)
                return BadRequest(ErrorMessage);

            await _repository.AddAsync(entity);

            await _uow.SaveAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }
    }
}
