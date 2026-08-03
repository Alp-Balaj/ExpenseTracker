using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services;

/// <inheritdoc cref="ISavingsReservationService"/>
public sealed class SavingsReservationService : ISavingsReservationService
{
    private readonly IUnitOfWork _unitOfWork;

    public SavingsReservationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetReservedByAccountAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _unitOfWork.SavingContributions.Query()
            .Where(c => c.AccountId != null)
            .GroupBy(c => c.AccountId!.Value)
            .Select(g => new { AccountId = g.Key, Reserved = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.AccountId, r => r.Reserved);
    }

    public async Task<string?> GetGoalCurrencyCodeAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        return await GetGoalCurrencyCodeAsync(goalId, ignoreContributionId: null, cancellationToken);
    }

    public async Task<string?> ValidateContributionAccountAsync(
        Guid goalId,
        Guid? accountId,
        Guid? ignoreContributionId = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId is not Guid id)
            return "A contribution must name the account it is reserved against.";

        var account = await _unitOfWork.Accounts.Query()
            .Where(a => a.Id == id)
            .Select(a => new { a.Id, Code = a.Currency.Code })
            .FirstOrDefaultAsync(cancellationToken);

        if (account == null)
            return "Account not found";

        // A goal is denominated in one currency, so that "reserved" can be
        // subtracted from an account balance without a conversion nobody asked
        // for. The first contribution picks it; later ones must agree.
        var goalCurrency = await GetGoalCurrencyCodeAsync(goalId, ignoreContributionId, cancellationToken);

        if (goalCurrency != null && goalCurrency != account.Code)
            return $"This goal holds {goalCurrency}. Contribute from a {goalCurrency} account, or start a separate goal.";

        return null;
    }

    /// <summary>
    /// Earliest contribution wins, ordered the way the goal's history is
    /// written rather than by id, so the answer does not depend on insert order.
    /// </summary>
    private async Task<string?> GetGoalCurrencyCodeAsync(
        Guid goalId,
        Guid? ignoreContributionId,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.SavingContributions.Query()
            .Where(c => c.GoalId == goalId && c.AccountId != null)
            .Where(c => ignoreContributionId == null || c.Id != ignoreContributionId)
            .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => c.Account!.Currency.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
