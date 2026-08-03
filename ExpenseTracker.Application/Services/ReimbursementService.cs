using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services;

/// <inheritdoc cref="IReimbursementService"/>
public sealed class ReimbursementService : IReimbursementService
{
    // The four documented business-rule 400s. Bare strings, because that is what
    // BadRequest(referenceError) has always returned on these write paths;
    // DataAnnotation failures keep their ValidationProblemDetails.
    internal const string ExpectedExceedsAmount = "Expected reimbursement cannot exceed the expense amount.";
    internal const string AmountBelowExpected = "Amount cannot be less than the expected reimbursement.";
    internal const string ExpectedBelowSettled = "Expected reimbursement cannot be less than the amount already settled.";
    internal const string RemovingSettledReimbursement = "Cannot remove a reimbursement that has settlements.";

    internal const string ExpectedNotPositive = "Expected reimbursement must be greater than zero.";
    internal const string ForeignCurrencySettlement = "Settlement must be in the expense's currency.";
    internal const string SettlementExceedsOpen = "Settlement cannot exceed the amount still open.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountServices _accountServices;

    public ReimbursementService(IUnitOfWork unitOfWork, IAccountServices accountServices)
    {
        _unitOfWork = unitOfWork;
        _accountServices = accountServices;
    }

    public async Task<IReadOnlyList<ReimbursementDTO>> GetAsync(bool includeSettled, CancellationToken cancellationToken = default)
    {
        var today = PlannedSchedule.Today;

        // Oldest expense first: what has been out longest is what the user wants
        // at the top of the list.
        var rows = await _unitOfWork.Reimbursements.Query()
            .OrderBy(r => r.Expense.Date)
            .ThenBy(r => r.Id)
            .ProjectToDto()
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.Complete(today);

        // Filtered here rather than in SQL: SUM over no settlement rows is NULL,
        // and "NULL < expected" is not true, so the predicate would silently drop
        // the receivables that are the whole point of the default view.
        return includeSettled ? rows : rows.Where(r => r.OpenAmount > 0m).ToList();
    }

    public async Task<(ReimbursementDTO? Reimbursement, string? Error)> SettleAsync(
        Guid reimbursementId, SettleReimbursementDTO dto, CancellationToken cancellationToken = default)
    {
        var receivable = await _unitOfWork.Reimbursements.Query()
            .Where(r => r.Id == reimbursementId)
            .Select(r => new
            {
                r.Id,
                r.ExpectedAmount,
                ExpenseCurrencyId = r.Expense.Account.CurrencyId,
                Settled = r.Settlements.Sum(s => (decimal?)s.Amount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Query() is already scoped to the current user, so another user's
        // receivable is simply not there.
        if (receivable == null) return (null, null);

        // Fix-8: resolved through the current user's repository so a foreign
        // account id is answered exactly like one that does not exist.
        var account = await _unitOfWork.Accounts.GetUserDataByIdAsync(dto.AccountId, cancellationToken);
        if (account == null) return (null, "Account not found");

        if (account.CurrencyId != receivable.ExpenseCurrencyId)
            return (null, ForeignCurrencySettlement);

        var settled = receivable.Settled ?? 0m;
        if (settled + dto.Amount > receivable.ExpectedAmount)
            return (null, SettlementExceedsOpen);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var credit = await _accountServices.AdjustAccountBalance(dto.Amount, dto.AccountId, cancellationToken);
        if (!credit.Success) return (null, credit.ErrorMessage);

        await _unitOfWork.ReimbursementSettlements.AddAsync(new ReimbursementSettlement
        {
            UserId = null!,
            ReimbursementId = receivable.Id,
            AccountId = dto.AccountId,
            Amount = dto.Amount,
            Date = dto.Date,

            Reimbursement = null!,
            Account = null!
        }, cancellationToken);

        await _unitOfWork.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var refreshed = await _unitOfWork.Reimbursements.Query()
            .Where(r => r.Id == reimbursementId)
            .ProjectToDto()
            .FirstAsync(cancellationToken);

        return (refreshed.Complete(PlannedSchedule.Today), null);
    }

    public async Task<string?> ApplyToCreatedExpenseAsync(
        Expense expense, decimal? expectedReimbursement, CancellationToken cancellationToken = default)
    {
        if (expectedReimbursement is not decimal expected) return null;
        if (expected == 0m) return null;
        if (expected < 0m) return ExpectedNotPositive;
        if (expected > expense.Amount) return ExpectedExceedsAmount;

        var reimbursement = new Reimbursement
        {
            UserId = null!,
            ExpenseId = expense.Id,
            ExpectedAmount = expected,

            Expense = null!
        };

        await _unitOfWork.Reimbursements.AddAsync(reimbursement, cancellationToken);

        // Set after the add, so the graph is never walked with a UserId still
        // null. This is also what puts the figures in the 201 response body.
        expense.Reimbursement = reimbursement;

        return null;
    }

    public async Task<string?> ApplyToUpdatedExpenseAsync(
        Expense expense, decimal? expectedReimbursement, CancellationToken cancellationToken = default)
    {
        if (expectedReimbursement is decimal negative && negative < 0m)
            return ExpectedNotPositive;

        var existing = await _unitOfWork.Reimbursements.Query()
            .Where(r => r.ExpenseId == expense.Id)
            .Select(r => new
            {
                r.Id,
                r.ExpectedAmount,
                Settled = r.Settlements.Sum(s => (decimal?)s.Amount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var settled = existing?.Settled ?? 0m;
        var wanted = expectedReimbursement ?? 0m;

        if (wanted == 0m)
        {
            if (existing == null) return null;
            if (settled > 0m) return RemovingSettledReimbursement;

            var stale = await _unitOfWork.Reimbursements.GetUserDataByIdAsync(existing.Id, cancellationToken);
            if (stale != null) _unitOfWork.Reimbursements.DeleteUserData(stale);
            return null;
        }

        if (wanted > expense.Amount)
            // Same inequality, two ways to arrive at it. Which side moved is what
            // the user needs told: raising the expectation past the bill, or
            // dropping the bill below the expectation.
            return wanted > (existing?.ExpectedAmount ?? 0m)
                ? ExpectedExceedsAmount
                : AmountBelowExpected;

        if (wanted < settled)
            return ExpectedBelowSettled;

        if (existing == null)
        {
            var created = new Reimbursement
            {
                UserId = null!,
                ExpenseId = expense.Id,
                ExpectedAmount = wanted,

                Expense = null!
            };
            await _unitOfWork.Reimbursements.AddAsync(created, cancellationToken);
            return null;
        }

        var tracked = await _unitOfWork.Reimbursements.GetUserDataByIdAsync(existing.Id, cancellationToken);
        if (tracked != null)
        {
            tracked.ExpectedAmount = wanted;
            _unitOfWork.Reimbursements.UpdateUserData(tracked);
        }

        return null;
    }

    public async Task<string?> ReverseForDeletedExpenseAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        var settlements = await _unitOfWork.ReimbursementSettlements.Query()
            .Where(s => s.Reimbursement.ExpenseId == expense.Id)
            .Select(s => new { s.AccountId, s.Amount })
            .ToListAsync(cancellationToken);

        // The settlement credited this account when it was booked; the expense
        // going away has to take that credit back with it, or deleting a split
        // bill would leave the money it "recovered" sitting in the balance.
        foreach (var settlement in settlements)
        {
            var reverse = await _accountServices.AdjustAccountBalance(
                -settlement.Amount, settlement.AccountId, cancellationToken);
            if (!reverse.Success) return reverse.ErrorMessage;
        }

        return null;
    }
}
