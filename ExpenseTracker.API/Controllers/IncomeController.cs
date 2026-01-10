using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.API.Controllers
{
    public class IncomeController : BaseController<Income, IncomeDTO>
    {
        public IncomeController(IUnitOfWork uow, IUserRelatedRepository<Income> repo)
            : base(uow, repo) { }
        protected override IncomeDTO ToDto(Income entity) => entity.ToDto();
        protected override Income ToEntity(IncomeDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Income entity, IncomeDTO dto) => entity.Apply(dto);
    }
}
