namespace ExpenseTracker.Domain.Interfaces
{
    public interface IUnitOfWorkTransaction : IAsyncDisposable
    {
        Task CommitAsync();
        Task RollbackAsync();
    }
}
