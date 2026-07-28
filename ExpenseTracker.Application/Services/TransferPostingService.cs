using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.Application.Services;

/// <inheritdoc cref="ITransferPostingService"/>
public sealed class TransferPostingService : ITransferPostingService
{
    private readonly IAccountServices _accountServices;
    private readonly ISavingGoalServices _savingGoalServices;
    private readonly IUserRelatedRepository<SavingContribution> _contributionRepository;

    public TransferPostingService(
        IAccountServices accountServices,
        ISavingGoalServices savingGoalServices,
        IUserRelatedRepository<SavingContribution> contributionRepository)
    {
        _accountServices = accountServices;
        _savingGoalServices = savingGoalServices;
        _contributionRepository = contributionRepository;
    }

    public async Task<string?> ApplyMoveAsync(
        decimal amount, Guid fromAccountId, Guid toAccountId, int sign,
        CancellationToken cancellationToken = default)
    {
        var debit = await _accountServices.AdjustAccountBalance(-sign * amount, fromAccountId, cancellationToken);
        if (!debit.Success) return debit.ErrorMessage;

        var credit = await _accountServices.AdjustAccountBalance(sign * amount, toAccountId, cancellationToken);
        if (!credit.Success) return credit.ErrorMessage;

        return null;
    }

    public async Task<string?> LinkContributionAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        if (transfer.SavingGoalId is not Guid goalId)
            return null;

        var contribution = new SavingContribution
        {
            UserId = null!,
            GoalId = goalId,
            Amount = transfer.Amount,
            Date = transfer.Date,
            Note = transfer.Note,
            SavingGoal = null!
        };

        var (success, errorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(goalId, transfer.Amount, cancellationToken);
        if (!success) return errorMessage;

        await _contributionRepository.AddAsync(contribution, cancellationToken);
        transfer.SavingContributionId = contribution.Id;

        return null;
    }

    public async Task<string?> UnlinkContributionAsync(Guid? savingContributionId, CancellationToken cancellationToken = default)
    {
        if (savingContributionId is not Guid contributionId)
            return null;

        var contribution = await _contributionRepository.GetUserDataByIdAsync(contributionId, cancellationToken);
        if (contribution == null) return null;

        var (success, errorMessage) = await _savingGoalServices.UpdateSavingGoalAmount(contribution.GoalId, -contribution.Amount, cancellationToken);
        if (!success) return errorMessage;

        _contributionRepository.DeleteUserData(contribution);

        return null;
    }
}
