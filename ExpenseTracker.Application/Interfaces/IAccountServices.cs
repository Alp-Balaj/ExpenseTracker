namespace ExpenseTracker.Application.Interfaces;
public interface IAccountServices
{
    Task<(bool Success, string ErrorMessage)> DecrementAmountFromAccount(decimal amount, Guid accountId, CancellationToken cancellationToken = default);
    Task<(bool Success, string ErrorMessage)> IncrementAmountToAccount(decimal amount, Guid accountId, CancellationToken cancellationToken = default);
    Task<(bool Success, string ErrorMessage)> AdjustAccountBalance(decimal delta, Guid accountId, CancellationToken cancellationToken = default);
}
