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
    public class SavingController : BaseController<Saving, SavingDTO>
    {
        public SavingController(IUnitOfWork uow, IUserRelatedRepository<Saving> repo)
            : base(uow, repo) { }
        protected override SavingDTO ToDto(Saving entity) => entity.ToDto();
        protected override Saving ToEntity(SavingDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Saving entity, SavingDTO dto) => entity.Apply(dto);
    }
}
