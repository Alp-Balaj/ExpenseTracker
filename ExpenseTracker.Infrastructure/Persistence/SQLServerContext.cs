using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;
public class SQLServerDbContext : ApplicationDbContext
{
    public SQLServerDbContext(DbContextOptions options)
        : base(options)
    {
    }
}