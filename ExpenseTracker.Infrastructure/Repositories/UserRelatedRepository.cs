using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Services;
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

        public async Task<IEnumerable<T>> GetAllUserDataAsync() =>
            await _dbSet.Where(e => e.UserId == _UserId).ToListAsync();

        public async Task<T?> GetUserDataByIdAsync(Guid id) =>
            await _dbSet.Where(e => e.UserId == _UserId).FirstOrDefaultAsync(e => e.Id == id);

        public async Task AddAsync(T entity)
        {
            entity.UserId = _UserId;
            await _dbSet.AddAsync(entity);
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
            return _context.Set<T>()
                .Where(x => x.UserId == _UserId);
        }
    }
}