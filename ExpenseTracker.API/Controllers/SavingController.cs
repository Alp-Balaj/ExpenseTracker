using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.VisualBasic;

namespace ExpenseTracker.API.Controllers
{
    public class SavingsController : BaseController<Saving, SavingDTO>
    {
        public SavingsController(IUnitOfWork uow, IUserRelatedRepository<Saving> repo)
            : base(uow, repo) { }
        protected override SavingDTO ToDto(Saving entity) => entity.ToDto();
        protected override Saving ToEntity(SavingDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Saving entity, SavingDTO dto) => entity.Apply(dto);
    }
}
