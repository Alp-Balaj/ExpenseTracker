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
    public class IncomeController : TransactionControllerBase<Income, IncomeDTO>
    {
        private readonly IUserRelatedRepository<Category> _categoryRepository;

        public IncomeController(IUnitOfWork uow, IUserRelatedRepository<Income> repo, IAccountServices accountServices,
            IUserRelatedRepository<Category> categoryRepository)
            : base(uow, repo, accountServices)
        {
            _categoryRepository = categoryRepository;
        }
        protected override IncomeDTO ToDto(Income entity) => entity.ToDto();
        protected override Income ToEntity(IncomeDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Income entity, IncomeDTO dto) => entity.Apply(dto);

        protected override int BalanceSign => +1;
        protected override decimal AmountOf(Income entity) => entity.Amount;
        protected override Guid AccountIdOf(Income entity) => entity.AccountId;

        protected override IOrderedQueryable<Income> OrderForListing(IQueryable<Income> query) =>
            query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id);

        // AccountId is already ownership-checked by the user-scoped balance
        // adjustment; CategoryId has no such side effect, so check it here.
        protected override async Task<string?> ValidateReferencesAsync(IncomeDTO dto, CancellationToken cancellationToken = default)
        {
            if (await _categoryRepository.GetUserDataByIdAsync(dto.CategoryId, cancellationToken) == null)
                return "Category not found";
            return null;
        }
    }
}
