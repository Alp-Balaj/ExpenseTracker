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
    public class ExpenseController : BaseController<Expense, ExpenseDTO>
    {
        public ExpenseController(IUnitOfWork uow, IUserRelatedRepository<Expense> repo)
            : base(uow, repo) { }
        protected override ExpenseDTO ToDto(Expense entity) => entity.ToDto();
        protected override Expense ToEntity(ExpenseDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Expense entity, ExpenseDTO dto) => entity.Apply(dto);
    }
}
