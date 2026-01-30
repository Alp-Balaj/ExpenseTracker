using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;
public class SqlServerContext : ApplicationDbContext
{
    public SqlServerContext(DbContextOptions<SqlServerContext> options)
        : base(options)
    {
    }
}