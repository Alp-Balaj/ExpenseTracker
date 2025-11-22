using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class UserRepository<T> : IUserRepository<T> where T : BaseEntity
{
    private readonly ApplicationDbContext _context;
    private readonly string _userId;
    private readonly DbSet<T> _dbSet;

    public UserRepository(ApplicationDbContext context, string userId)
    {
        _context = context;
        _userId = userId;
        _dbSet = _context.Set<T>();
    }

    public async Task<IEnumerable<BaseEntity>> GetAllUserDataAsync() =>
        await _dbSet.Where(e => e.UserId == _userId).ToListAsync();

    public async Task<BaseEntity?> GetUserDataByIdAsync(Guid id) => 
        await _dbSet.Where(e => e.UserId == _userId).FirstOrDefaultAsync(e => e.Id == id);

    public async Task AddAsync(T entity) => 
        await _dbSet.AddAsync(entity);

    public void UpdateUserData(T entity) =>
        _dbSet.Update(entity);

    public void DeleteUserData(T entity) =>
        _dbSet.Remove(entity);
}
