using ExpenseTracker.Application.DTOs;
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
    public class SavingGoalController : BaseController<SavingGoal, SavingGoalDTO>
    {
        private readonly IUserRelatedRepository<Account> _accountRepository;
        private readonly IUserRelatedRepository<Category> _categoryRepository;

        public SavingGoalController(IUnitOfWork uow, IUserRelatedRepository<SavingGoal> repo,
            IUserRelatedRepository<Account> accountRepository,
            IUserRelatedRepository<Category> categoryRepository)
            : base(uow, repo)
        {
            _accountRepository = accountRepository;
            _categoryRepository = categoryRepository;
        }
        protected override SavingGoalDTO ToDto(SavingGoal entity) => entity.ToDto();
        protected override SavingGoal ToEntity(SavingGoalDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(SavingGoal entity, SavingGoalDTO dto) => entity.Apply(dto);

        protected override async Task<string?> ValidateReferencesAsync(SavingGoalDTO dto, CancellationToken cancellationToken = default)
        {
            // The account is optional since Stream V — a goal's money lives
            // wherever its contributions say it does. When one is supplied it
            // still has to be the user's own.
            if (dto.AccountId is Guid accountId &&
                await _accountRepository.GetUserDataByIdAsync(accountId, cancellationToken) == null)
                return "Account not found";
            if (await _categoryRepository.GetUserDataByIdAsync(dto.CategoryId, cancellationToken) == null)
                return "Category not found";
            return null;
        }

        // Goals are served with their contributions: each one names the account
        // holding its money, and without them a goal card cannot say where that
        // money sits. Same paging contract as every other list endpoint.
        [HttpGet]
        public override async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            var rows = _repository.Query();
            var totalCount = await rows.CountAsync(cancellationToken);

            IQueryable<SavingGoal> window = OrderForListing(rows);

            if (page.Skip is int skip and > 0) window = window.Skip(skip);
            if (page.Take is int take) window = window.Take(take);

            var goals = await WithContributions(window).ToListAsync(cancellationToken);

            SetTotalCount(totalCount);

            return Ok(goals.Select(g => g.ToDto(withContributions: true)).ToList());
        }

        [HttpGet("{id:guid}")]
        public override async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var goal = await WithContributions(_repository.Query().Where(g => g.Id == id))
                .FirstOrDefaultAsync(cancellationToken);

            if (goal == null) return NotFound();

            return Ok(goal.ToDto(withContributions: true));
        }

        /// <summary>
        /// The whole currency chain is loaded, not just the account id: a goal's
        /// currency is that of its earliest contribution's account.
        /// </summary>
        private static IQueryable<SavingGoal> WithContributions(IQueryable<SavingGoal> query) =>
            query
                .Include(g => g.SavingContributions)
                    .ThenInclude(c => c.Account!)
                        .ThenInclude(a => a.Currency);
    }
}
