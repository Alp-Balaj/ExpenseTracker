using ExpenseTracker.Application.DTOs;
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
    public class CurrencyController : BaseController<Currency, CurrencyDTO>
    {
        public CurrencyController(IUnitOfWork uow, IUserRelatedRepository<Currency> repo)
            : base(uow, repo) { }
        protected override CurrencyDTO ToDto(Currency entity) => entity.ToDto();
        protected override Currency ToEntity(CurrencyDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Currency entity, CurrencyDTO dto) => entity.Apply(dto);
        protected CurrencyDropdownDTO ToDropdownDto(Currency entity) => entity.ToDropdownDto();

        [HttpGet("Dropdown")]
        public virtual async Task<IActionResult> GetAllDropdown()
        {
            var entities = await _repository.GetAllUserDataAsync();
            var dtos = entities.Select(ToDropdownDto).ToList();
            return Ok(dtos);
        }
    }


}
