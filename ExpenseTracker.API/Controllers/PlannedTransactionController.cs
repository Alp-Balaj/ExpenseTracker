using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Money the user already knows is coming — rent, a subscription, a salary,
    /// a standing order into savings, or a single future-dated payment.
    /// <para>
    /// Templates are plain CRUD on top of <see cref="BaseController{TEntity, TDto}"/>.
    /// The interesting half is the due queue underneath: reading the templates or
    /// the queue first materialises anything that has come due, and an entry only
    /// becomes real money when the user commits it.
    /// </para>
    /// <para>
    /// Nothing here posts unattended. There is no hosted service and no timer —
    /// see <see cref="IPlannedTransactionService.GenerateDueOccurrencesAsync"/>.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PlannedTransactionController : BaseController<PlannedTransaction, PlannedTransactionDTO>
    {
        private readonly IPlannedTransactionService _planned;
        private readonly IUserRelatedRepository<PlannedOccurrence> _occurrences;
        private readonly IUserRelatedRepository<Category> _categoryRepository;
        private readonly IUserRelatedRepository<SavingGoal> _savingGoalRepository;
        private readonly IAccountRepository _accountRepository;
        private readonly ISavingsReservationService _reservations;

        public PlannedTransactionController(
            IUnitOfWork uow,
            IUserRelatedRepository<PlannedTransaction> repo,
            IPlannedTransactionService planned,
            IUserRelatedRepository<PlannedOccurrence> occurrences,
            IUserRelatedRepository<Category> categoryRepository,
            IUserRelatedRepository<SavingGoal> savingGoalRepository,
            IAccountRepository accountRepository,
            ISavingsReservationService reservations)
            : base(uow, repo)
        {
            _planned = planned;
            _occurrences = occurrences;
            _categoryRepository = categoryRepository;
            _savingGoalRepository = savingGoalRepository;
            _accountRepository = accountRepository;
            _reservations = reservations;
        }

        protected override PlannedTransactionDTO ToDto(PlannedTransaction entity) =>
            entity.ToDto(PlannedSchedule.Today);

        protected override PlannedTransaction ToEntity(PlannedTransactionDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(PlannedTransaction entity, PlannedTransactionDTO dto) => entity.Apply(dto);

        // Alphabetical: this is a short, hand-curated list the user scans by name,
        // not a feed. Id breaks ties so a window stays stable.
        protected override IOrderedQueryable<PlannedTransaction> OrderForListing(IQueryable<PlannedTransaction> query) =>
            query.OrderBy(e => e.Title).ThenBy(e => e.Id);

        [HttpGet]
        public override async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            // Reading the schedule is what advances it. See the service's remarks.
            await _planned.GenerateDueOccurrencesAsync(cancellationToken);

            var (entities, totalCount) = await _repository.GetUserDataPageAsync(
                page.Skip, page.Take, OrderForListing, cancellationToken);

            SetTotalCount(totalCount);

            var pending = await PendingCountsAsync(entities.Select(e => e.Id).ToList(), cancellationToken);
            var today = PlannedSchedule.Today;

            var dtos = entities
                .Select(e => e.ToDto(today, pending.TryGetValue(e.Id, out var count) ? count : 0))
                .ToList();

            return Ok(dtos);
        }

        [HttpGet("{id:guid}")]
        public override async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            var pending = await PendingCountsAsync([entity.Id], cancellationToken);

            return Ok(entity.ToDto(
                PlannedSchedule.Today,
                pending.TryGetValue(entity.Id, out var count) ? count : 0));
        }

        /// <summary>
        /// The due queue. Defaults to what is still waiting to be dealt with —
        /// the only thing the app ever needs in bulk; the committed and skipped
        /// history is available per state but is not what "the queue" means.
        /// </summary>
        [HttpGet("occurrences")]
        public async Task<IActionResult> GetOccurrences(
            [FromQuery] PlannedOccurrenceState? state, CancellationToken cancellationToken)
        {
            await _planned.GenerateDueOccurrencesAsync(cancellationToken);

            var wanted = state ?? PlannedOccurrenceState.Pending;

            // Joined to the templates in one query: the client renders and commits
            // the queue without a second round trip, and the join is user-scoped on
            // both sides.
            var rows = await _occurrences.Query()
                .Where(o => o.State == wanted)
                .OrderBy(o => o.DueDate)
                .ThenBy(o => o.Id)
                .Join(_repository.Query(),
                      o => o.PlannedTransactionId,
                      t => t.Id,
                      (o, t) => new { Occurrence = o, Template = t })
                .ToListAsync(cancellationToken);

            SetTotalCount(rows.Count);

            return Ok(rows.Select(r => r.Occurrence.ToDto(r.Template)).ToList());
        }

        /// <summary>
        /// Confirms that an occurrence really happened, posting it as an Expense,
        /// Income or Transfer. The body is the "what actually happened" step —
        /// every field optional, falling back to what was planned.
        /// </summary>
        [HttpPost("occurrences/{id:guid}/commit")]
        public async Task<IActionResult> CommitOccurrence(
            Guid id, [FromBody] CommitPlannedOccurrenceDTO commit, CancellationToken cancellationToken)
        {
            var result = await _planned.CommitAsync(id, commit ?? new CommitPlannedOccurrenceDTO(), cancellationToken);

            return result.Status switch
            {
                PlannedActionStatus.Success => NoContent(),
                PlannedActionStatus.NotFound => NotFound(),
                _ => BadRequest(result.ErrorMessage)
            };
        }

        /// <summary>Marks an occurrence as didn't-happen. Posts nothing.</summary>
        [HttpPost("occurrences/{id:guid}/skip")]
        public async Task<IActionResult> SkipOccurrence(Guid id, CancellationToken cancellationToken)
        {
            var result = await _planned.SkipAsync(id, cancellationToken);

            return result.Status switch
            {
                PlannedActionStatus.Success => NoContent(),
                PlannedActionStatus.NotFound => NotFound(),
                _ => BadRequest(result.ErrorMessage)
            };
        }

        private async Task<Dictionary<Guid, int>> PendingCountsAsync(
            IReadOnlyList<Guid> templateIds, CancellationToken cancellationToken)
        {
            if (templateIds.Count == 0) return [];

            return await _occurrences.Query()
                .Where(o => templateIds.Contains(o.PlannedTransactionId)
                         && o.State == PlannedOccurrenceState.Pending)
                .GroupBy(o => o.PlannedTransactionId)
                .Select(g => new { TemplateId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TemplateId, x => x.Count, cancellationToken);
        }

        /// <summary>
        /// Everything about a template that only makes sense in combination: the
        /// category has to match the kind, a transfer needs two different accounts
        /// in the same currency, and a savings goal has to be backed by the one
        /// the money lands in.
        /// </summary>
        /// <remarks>
        /// The accounts are only defaults here, so an absent one is legal — the
        /// commit dialog asks. What is checked is that a supplied one is real and
        /// belongs to the caller. Every failure message is deliberately the same
        /// for "does not exist" and "is someone else's".
        /// </remarks>
        protected override async Task<string?> ValidateReferencesAsync(
            PlannedTransactionDTO dto, CancellationToken cancellationToken = default)
        {
            if (dto.EndDate is DateOnly endDate && endDate < dto.StartDate)
                return "The end date cannot be before the start date";

            if (dto.Kind == PlannedKind.Transfer)
            {
                if (dto.CategoryId is not null)
                    return "A planned transfer does not take a category";
            }
            else
            {
                if (dto.CategoryId is not Guid categoryId)
                    return "Choose a category";

                var category = await _categoryRepository.GetUserDataByIdAsync(categoryId, cancellationToken);
                if (category == null) return "Category not found";

                // A planned expense uses the same Expense categories a hand-typed
                // one does — that is what lets a committed occurrence show up in
                // the budget and the report the user already has.
                var required = dto.Kind == PlannedKind.Expense ? CategoryType.Expense : CategoryType.Income;
                if (category.CategoryType != required)
                    return $"Choose a category of type {required}";
            }

            if (dto.Kind == PlannedKind.Income && dto.FromAccountId is not null)
                return "Planned income has no source account";

            if (dto.Kind == PlannedKind.Expense && dto.ToAccountId is not null)
                return "A planned expense has no destination account";

            var from = await ResolveAccountAsync(dto.FromAccountId, cancellationToken);
            if (from.Error != null) return from.Error;

            var to = await ResolveAccountAsync(dto.ToAccountId, cancellationToken);
            if (to.Error != null) return to.Error;

            if (dto.Kind == PlannedKind.Transfer && from.Account != null && to.Account != null)
            {
                if (from.Account.Id == to.Account.Id)
                    return "A transfer must be between two different accounts";

                if (from.Account.CurrencyId != to.Account.CurrencyId)
                    return "Transfers must be between accounts with the same currency";
            }

            if (dto.SavingGoalId is Guid goalId)
            {
                if (dto.Kind != PlannedKind.Transfer)
                    return "Only a planned transfer can contribute to a savings goal";

                var goal = await _savingGoalRepository.GetUserDataByIdAsync(goalId, cancellationToken);
                if (goal == null) return "Saving goal not found";

                // Stream V: any account may credit a goal — the contribution
                // books where the money lands. The currency still has to match,
                // and is checked here when the destination is already known and
                // again at commit time, since either side may move in between.
                if (to.Account != null)
                {
                    var accountError = await _reservations.ValidateContributionAccountAsync(
                        goalId, to.Account.Id, null, cancellationToken);
                    if (accountError != null) return accountError;
                }
            }

            return null;
        }

        private async Task<(Account? Account, string? Error)> ResolveAccountAsync(
            Guid? accountId, CancellationToken cancellationToken)
        {
            if (accountId is not Guid id) return (null, null);

            var account = await _accountRepository.GetUserDataWithCurrencyByIdAsync(id, cancellationToken);
            return account == null ? (null, "Account not found") : (account, null);
        }
    }
}
