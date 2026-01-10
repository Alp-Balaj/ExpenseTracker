using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.API.Controllers
{
    public class CurrencyController : BaseController<Currency, CurrencyDTO>
    {
        public CurrencyController(IUnitOfWork uow, IUserRelatedRepository<Currency> repo)
            : base(uow, repo) { }
        protected override CurrencyDTO ToDto(Currency entity) => entity.ToDto();
        protected override Currency ToEntity(CurrencyDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Currency entity, CurrencyDTO dto) => entity.Apply(dto);
    }
}
