using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Repositories
{
    public class UserRelatedRepository<T> : IUserRelatedRepository<T> where T : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserServices _user;
        private readonly DbSet<T> _dbSet;

        public UserRelatedRepository(ApplicationDbContext context, ICurrentUserServices user)
        {
            _context = context;
            _user = user;
            _dbSet = _context.Set<T>();
        }

        private string _UserId =>
            _user.UserId;

        // Reads never enter the change tracker: a list request used to leave one
        // tracked entity per row behind, which costs a snapshot per row and makes
        // every later SaveChanges in the request walk them all.
        private IQueryable<T> UserRows() =>
            _dbSet.AsNoTracking().Where(e => e.UserId == _UserId);

        public async Task<IEnumerable<T>> GetAllUserDataAsync(CancellationToken cancellationToken = default) =>
            await UserRows().ToListAsync(cancellationToken);

        public async Task<(IReadOnlyList<T> Items, int TotalCount)> GetUserDataPageAsync(
            int? skip,
            int? take,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            CancellationToken cancellationToken = default)
        {
            var rows = UserRows();

            IQueryable<T> ordered = orderBy is null
                ? rows.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
                : orderBy(rows);

            // Unwindowed: the list we are about to materialise *is* the total, so
            // the extra COUNT query only happens for callers that actually page.
            if (skip is null && take is null)
            {
                var all = await ordered.ToListAsync(cancellationToken);
                return (all, all.Count);
            }

            var total = await rows.CountAsync(cancellationToken);

            if (skip is int skipCount and > 0)
                ordered = ordered.Skip(skipCount);

            if (take is int takeCount)
                ordered = ordered.Take(takeCount);

            return (await ordered.ToListAsync(cancellationToken), total);
        }

        public async Task<T?> GetUserDataByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            await _dbSet.Where(e => e.UserId == _UserId)
                        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            entity.UserId = _UserId;
            await _dbSet.AddAsync(entity, cancellationToken);
        }

        public void UpdateUserData(T entity)
        {
            if (entity.UserId != _UserId)
                throw new UnauthorizedAccessException("Cannot update another user's data.");
            _dbSet.Update(entity);
        }

        public void DeleteUserData(T entity)
        {
            if (entity.UserId != _UserId)
                throw new UnauthorizedAccessException("Cannot delete another user's data.");
            _dbSet.Remove(entity);
        }

        public IQueryable<T> Query()
        {
            return UserRows();
        }

        public string GetCurrentUserId()
        {
            return _user.UserId;
        }

    }
}
