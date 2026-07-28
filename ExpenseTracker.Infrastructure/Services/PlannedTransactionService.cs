using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IPlannedTransactionService"/>
/// <remarks>
/// Lives in Infrastructure rather than Application because both halves need the
/// DbContext itself, not just the repositories: generation has to survive a
/// unique-constraint collision without poisoning the change tracker, and commit
/// claims its occurrence with a conditional UPDATE so two clicks cannot post the
/// same rent twice. Everything is still scoped to the current user — through the
/// repositories where possible, and explicitly in the raw queries where not.
/// </remarks>
public sealed class PlannedTransactionService : IPlannedTransactionService
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountServices _accountServices;
    private readonly ITransferPostingService _transferPosting;
    private readonly IAccountRepository _accountRepository;
    private readonly ICurrentUserServices _currentUser;
    private readonly ILogger<PlannedTransactionService> _logger;

    public PlannedTransactionService(
        ApplicationDbContext context,
        IUnitOfWork unitOfWork,
        IAccountServices accountServices,
        ITransferPostingService transferPosting,
        IAccountRepository accountRepository,
        ICurrentUserServices currentUser,
        ILogger<PlannedTransactionService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _accountServices = accountServices;
        _transferPosting = transferPosting;
        _accountRepository = accountRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    private static DateOnly Today => PlannedSchedule.Today;

    public async Task<int> GenerateDueOccurrencesAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var today = Today;

        var templates = await _context.PlannedTransactions
            .Where(p => p.UserId == userId && p.IsActive)
            .ToListAsync(cancellationToken);

        if (templates.Count == 0) return 0;

        var templateIds = templates.Select(t => t.Id).ToList();

        // One round trip for every template's existing keys, rather than one per
        // template: this runs on every read of the planned data.
        var existing = (await _context.PlannedOccurrences
                .Where(o => o.UserId == userId && templateIds.Contains(o.PlannedTransactionId))
                .Select(o => new { o.PlannedTransactionId, o.PeriodKey })
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var queued = new List<PlannedOccurrence>();

        foreach (var template in templates)
        {
            foreach (var (periodKey, dueDate) in PlannedSchedule.DueOccurrences(template, today))
            {
                if (!existing.Add(new { PlannedTransactionId = template.Id, PeriodKey = periodKey }))
                    continue;

                queued.Add(new PlannedOccurrence
                {
                    UserId = userId,
                    PlannedTransactionId = template.Id,
                    PlannedTransaction = null!,
                    PeriodKey = periodKey,
                    DueDate = dueDate,
                    State = PlannedOccurrenceState.Pending
                });
            }
        }

        if (queued.Count == 0) return 0;

        _context.PlannedOccurrences.AddRange(queued);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return queued.Count;
        }
        catch (DbUpdateException ex)
        {
            // Two requests generating at once — a second tab, a double-click on
            // refresh. The unique index did its job; the other request queued the
            // period and this one has nothing left to do. Detaching keeps the
            // change tracker usable for whatever the caller does next, which
            // matters because generation runs as the opening move of a read.
            foreach (var occurrence in queued)
                _context.Entry(occurrence).State = EntityState.Detached;

            _logger.LogInformation(
                ex, "Planned occurrence generation collided with a concurrent run for user {UserId}; the other run won.", userId);
            return 0;
        }
    }

    public async Task<PlannedActionResult> CommitAsync(
        Guid occurrenceId, CommitPlannedOccurrenceDTO commit, CancellationToken cancellationToken = default)
    {
        var occurrence = await _unitOfWork.PlannedOccurrences.GetUserDataByIdAsync(occurrenceId, cancellationToken);
        if (occurrence == null) return PlannedActionResult.NotFound();

        if (occurrence.State != PlannedOccurrenceState.Pending)
            return PlannedActionResult.Invalid(occurrence.State == PlannedOccurrenceState.Committed
                ? "This occurrence has already been committed."
                : "This occurrence was skipped.");

        var template = await _unitOfWork.PlannedTransactions.GetUserDataByIdAsync(
            occurrence.PlannedTransactionId, cancellationToken);
        if (template == null) return PlannedActionResult.NotFound();

        var amount = commit.Amount ?? template.Amount;
        if (amount <= 0) return PlannedActionResult.Invalid("Amount must be greater than zero.");

        var date = commit.Date ?? occurrence.DueDate;
        var fromAccountId = commit.FromAccountId ?? template.FromAccountId;
        var toAccountId = commit.ToAccountId ?? template.ToAccountId;

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Claim the occurrence before spending any money on it. A conditional
        // UPDATE that only matches a Pending row means two simultaneous commits
        // cannot both proceed: the loser matches nothing and stops here, before
        // it has adjusted a balance. Loading and checking the state above is not
        // enough on its own — that read and this write are not atomic together.
        var claimed = await _context.PlannedOccurrences
            .Where(o => o.Id == occurrenceId
                     && o.UserId == occurrence.UserId
                     && o.State == PlannedOccurrenceState.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.State, PlannedOccurrenceState.Committed),
                cancellationToken);

        if (claimed == 0)
            return PlannedActionResult.Invalid("This occurrence has already been dealt with.");

        var result = template.Kind switch
        {
            PlannedKind.Expense => await CommitExpenseAsync(template, occurrence, amount, date, fromAccountId, cancellationToken),
            PlannedKind.Income => await CommitIncomeAsync(template, occurrence, amount, date, toAccountId, cancellationToken),
            PlannedKind.Transfer => await CommitTransferAsync(template, occurrence, amount, date, fromAccountId, toAccountId, cancellationToken),
            _ => PlannedActionResult.Invalid("Unknown planned transaction kind.")
        };

        if (result.Status != PlannedActionStatus.Success)
        {
            // Nothing is saved and the claim is rolled back with everything else,
            // so the occurrence goes back to Pending and can be retried.
            await transaction.RollbackAsync(cancellationToken);
            return result;
        }

        occurrence.State = PlannedOccurrenceState.Committed;
        occurrence.CommittedAt = DateTime.UtcNow;

        // The accounts carry over so the next commit pre-fills with whatever was
        // used last — the phone bill paid in cash three months running should not
        // keep offering the bank account. The amount only carries over when the
        // user asked for it: one cold January must not redefine the heating bill.
        if (fromAccountId is Guid usedFrom) template.FromAccountId = usedFrom;
        if (toAccountId is Guid usedTo) template.ToAccountId = usedTo;
        if (commit.UpdateTemplateAmount) template.Amount = amount;

        _unitOfWork.PlannedOccurrences.UpdateUserData(occurrence);
        _unitOfWork.PlannedTransactions.UpdateUserData(template);

        await _unitOfWork.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PlannedActionResult.Success();
    }

    private async Task<PlannedActionResult> CommitExpenseAsync(
        PlannedTransaction template, PlannedOccurrence occurrence,
        decimal amount, DateOnly date, Guid? fromAccountId, CancellationToken cancellationToken)
    {
        if (fromAccountId is not Guid accountId)
            return PlannedActionResult.Invalid("Choose the account this was paid from.");

        if (template.CategoryId is not Guid categoryId)
            return PlannedActionResult.Invalid("This planned expense has no category.");

        var expense = new Expense
        {
            UserId = null!,
            Title = template.Title,
            Amount = amount,
            Date = date,
            Description = template.Description,
            AccountId = accountId,
            CategoryId = categoryId,
            Account = null!,
            Category = null!
        };

        // Fix-2's mechanism, and the ownership check for the account in one: a
        // user-scoped atomic UPDATE that matches nothing when the account is not
        // theirs.
        var (success, errorMessage) = await _accountServices.AdjustAccountBalance(-amount, accountId, cancellationToken);
        if (!success) return PlannedActionResult.Invalid(errorMessage);

        await _unitOfWork.Expenses.AddAsync(expense, cancellationToken);
        occurrence.ExpenseId = expense.Id;

        return PlannedActionResult.Success();
    }

    private async Task<PlannedActionResult> CommitIncomeAsync(
        PlannedTransaction template, PlannedOccurrence occurrence,
        decimal amount, DateOnly date, Guid? toAccountId, CancellationToken cancellationToken)
    {
        if (toAccountId is not Guid accountId)
            return PlannedActionResult.Invalid("Choose the account this was paid into.");

        if (template.CategoryId is not Guid categoryId)
            return PlannedActionResult.Invalid("This planned income has no category.");

        var income = new Income
        {
            UserId = null!,
            Amount = amount,
            Date = date,
            // Income carries no title of its own, so the template's is the only
            // thing that would tell "Salary" from "Rent from the flat" later.
            Description = string.IsNullOrWhiteSpace(template.Description) ? template.Title : template.Description,
            AccountId = accountId,
            CategoryId = categoryId,
            Account = null!,
            Category = null!
        };

        var (success, errorMessage) = await _accountServices.AdjustAccountBalance(amount, accountId, cancellationToken);
        if (!success) return PlannedActionResult.Invalid(errorMessage);

        await _unitOfWork.Incomes.AddAsync(income, cancellationToken);
        occurrence.IncomeId = income.Id;

        return PlannedActionResult.Success();
    }

    private async Task<PlannedActionResult> CommitTransferAsync(
        PlannedTransaction template, PlannedOccurrence occurrence,
        decimal amount, DateOnly date, Guid? fromAccountId, Guid? toAccountId, CancellationToken cancellationToken)
    {
        if (fromAccountId is not Guid from) return PlannedActionResult.Invalid("Choose the account the money leaves.");
        if (toAccountId is not Guid to) return PlannedActionResult.Invalid("Choose the account the money lands in.");
        if (from == to) return PlannedActionResult.Invalid("A transfer must be between two different accounts.");

        // The same v1 rule TransferController enforces: one amount only describes
        // both sides of the move when the accounts agree on the currency.
        var fromAccount = await _accountRepository.GetUserDataWithCurrencyByIdAsync(from, cancellationToken);
        if (fromAccount == null) return PlannedActionResult.Invalid("Account not found");

        var toAccount = await _accountRepository.GetUserDataWithCurrencyByIdAsync(to, cancellationToken);
        if (toAccount == null) return PlannedActionResult.Invalid("Account not found");

        if (fromAccount.CurrencyId != toAccount.CurrencyId)
            return PlannedActionResult.Invalid("Transfers must be between accounts with the same currency");

        // A goal that has since been repointed at another account would silently
        // credit savings the money never reached, so it is re-checked here rather
        // than trusted from when the template was created.
        var savingGoalId = template.SavingGoalId;
        if (savingGoalId is Guid goalId)
        {
            var goal = await _unitOfWork.SavingGoals.GetUserDataByIdAsync(goalId, cancellationToken);
            if (goal == null) return PlannedActionResult.Invalid("Saving goal not found");
            if (goal.AccountId != to)
                return PlannedActionResult.Invalid("The savings goal must be backed by the destination account");
        }

        var transfer = new Transfer
        {
            UserId = null!,
            Amount = amount,
            Date = date,
            Note = string.IsNullOrWhiteSpace(template.Description) ? template.Title : template.Description,
            FromAccountId = from,
            ToAccountId = to,
            FromAccount = null!,
            ToAccount = null!,
            SavingGoalId = savingGoalId
        };

        var moveError = await _transferPosting.ApplyMoveAsync(amount, from, to, +1, cancellationToken);
        if (moveError != null) return PlannedActionResult.Invalid(moveError);

        var linkError = await _transferPosting.LinkContributionAsync(transfer, cancellationToken);
        if (linkError != null) return PlannedActionResult.Invalid(linkError);

        await _unitOfWork.Transfers.AddAsync(transfer, cancellationToken);
        occurrence.TransferId = transfer.Id;

        return PlannedActionResult.Success();
    }

    public async Task<PlannedActionResult> SkipAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
    {
        var occurrence = await _unitOfWork.PlannedOccurrences.GetUserDataByIdAsync(occurrenceId, cancellationToken);
        if (occurrence == null) return PlannedActionResult.NotFound();

        if (occurrence.State != PlannedOccurrenceState.Pending)
            return PlannedActionResult.Invalid(occurrence.State == PlannedOccurrenceState.Committed
                ? "This occurrence has already been committed."
                : "This occurrence was already skipped.");

        occurrence.State = PlannedOccurrenceState.Skipped;
        _unitOfWork.PlannedOccurrences.UpdateUserData(occurrence);
        await _unitOfWork.SaveAsync(cancellationToken);

        return PlannedActionResult.Success();
    }
}
