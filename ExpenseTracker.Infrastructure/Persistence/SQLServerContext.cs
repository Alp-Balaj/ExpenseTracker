using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;
public class SQLServerContext : ApplicationDbContext
{
    public SQLServerContext(DbContextOptions<SQLServerContext> options)
        : base(options)
    {
    }
}