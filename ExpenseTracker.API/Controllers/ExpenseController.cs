using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ExpenseController : TransactionControllerBase<Expense, ExpenseDTO>
    {
        private readonly IUserRelatedRepository<Category> _categoryRepository;
        private readonly IReimbursementService _reimbursements;

        public ExpenseController(IUnitOfWork uow, IUserRelatedRepository<Expense> repo, IAccountServices accountServices,
            IUserRelatedRepository<Category> categoryRepository,
            IReimbursementService reimbursements)
            : base(uow, repo, accountServices)
        {
            _categoryRepository = categoryRepository;
            _reimbursements = reimbursements;
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

        #region Stream R — the split-bill row rides along with the expense
        // All three run inside the transaction the base class already opened, so
        // the receivable and the expense are written, or abandoned, together.
        // The account debit itself is untouched: a split expense of 60 still
        // debits 60, because that is what left the account.
        protected override Task<string?> OnCreatingAsync(Expense entity, ExpenseDTO dto, CancellationToken cancellationToken = default) =>
            _reimbursements.ApplyToCreatedExpenseAsync(entity, dto.ExpectedReimbursement, cancellationToken);

        protected override Task<string?> OnUpdatingAsync(Expense entity, ExpenseDTO dto, CancellationToken cancellationToken = default) =>
            _reimbursements.ApplyToUpdatedExpenseAsync(entity, dto.ExpectedReimbursement, cancellationToken);

        protected override Task<string?> OnDeletingAsync(Expense entity, CancellationToken cancellationToken = default) =>
            _reimbursements.ReverseForDeletedExpenseAsync(entity, cancellationToken);
        #endregion

        #region Reads that carry the reimbursement figures
        // Two of the three reimbursement fields are server-owned aggregates, so
        // the read paths compute them in SQL beside the row rather than loading
        // the settlement collection. That keeps both endpoints at the round-trip
        // cost QueryCountTests pins: one query unwindowed, two when a page is
        // asked for — the extra one being the same COUNT as before.
        private sealed class ExpenseRow
        {
            public Expense Expense { get; set; } = null!;
            public decimal? Expected { get; set; }
            public decimal? Settled { get; set; }
        }

        private static IQueryable<ExpenseRow> WithReimbursement(IQueryable<Expense> query) =>
            query.Select(e => new ExpenseRow
            {
                Expense = e,
                Expected = e.Reimbursement == null ? null : (decimal?)e.Reimbursement.ExpectedAmount,
                Settled = e.Reimbursement == null ? null : e.Reimbursement.Settlements.Sum(s => (decimal?)s.Amount)
            });

        private static ExpenseDTO ToDto(ExpenseRow row) =>
            row.Expense.ToDto(row.Expected, row.Settled ?? 0m);

        [HttpGet]
        public override async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            var rows = _repository.Query();
            IQueryable<ExpenseRow> window = WithReimbursement(OrderForListing(rows));

            // Unwindowed: the list we are about to materialise *is* the total, so
            // the extra COUNT only happens for callers that actually page.
            if (page.Skip is null && page.Take is null)
            {
                var all = await window.ToListAsync(cancellationToken);
                SetTotalCount(all.Count);
                return Ok(all.Select(ToDto).ToList());
            }

            SetTotalCount(await rows.CountAsync(cancellationToken));

            if (page.Skip is int skipCount and > 0)
                window = window.Skip(skipCount);

            if (page.Take is int takeCount)
                window = window.Take(takeCount);

            return Ok((await window.ToListAsync(cancellationToken)).Select(ToDto).ToList());
        }

        [HttpGet("{id:guid}")]
        public override async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var row = await WithReimbursement(_repository.Query().Where(e => e.Id == id))
                .FirstOrDefaultAsync(cancellationToken);

            if (row == null) return NotFound();
            return Ok(ToDto(row));
        }
        #endregion
    }
}
