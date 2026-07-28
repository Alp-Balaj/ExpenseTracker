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
    public class ExpenseController : TransactionControllerBase<Expense, ExpenseDTO>
    {
        private readonly IUserRelatedRepository<Category> _categoryRepository;

        public ExpenseController(IUnitOfWork uow, IUserRelatedRepository<Expense> repo, IAccountServices accountServices,
            IUserRelatedRepository<Category> categoryRepository)
            : base(uow, repo, accountServices)
        {
            _categoryRepository = categoryRepository;
        }
        protected override ExpenseDTO ToDto(Expense entity) => entity.ToDto();
        protected override Expense ToEntity(ExpenseDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Expense entity, ExpenseDTO dto) => entity.Apply(dto);

        protected override int BalanceSign => -1;
        protected override decimal AmountOf(Expense entity) => entity.Amount;
        protected override Guid AccountIdOf(Expense entity) => entity.AccountId;

        // Newest spending first, which is both what every list view shows and
        // what the (UserId, Date) index is ordered for.
        protected override IOrderedQueryable<Expense> OrderForListing(IQueryable<Expense> query) =>
            query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id);

        // AccountId is already ownership-checked by the user-scoped balance
        // adjustment; CategoryId has no such side effect, so check it here.
        protected override async Task<string?> ValidateReferencesAsync(ExpenseDTO dto, CancellationToken cancellationToken = default)
        {
            if (await _categoryRepository.GetUserDataByIdAsync(dto.CategoryId, cancellationToken) == null)
                return "Category not found";
            return null;
        }
    }
}
