using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;
public class PostgresContext : ApplicationDbContext
{
    public PostgresContext(DbContextOptions<PostgresContext> options)
        : base(options)
    {
    }
}