using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ExpenseTracker.Infrastructure.Persistence.Factories;

public class PostgresFactory
    : IDesignTimeDbContextFactory<PostgresContext>
{
    public PostgresContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Migrations.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("PostgreSQL");

        var optionsBuilder = new DbContextOptionsBuilder<PostgresContext>();

        var options = new DbContextOptionsBuilder<PostgresContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresContext(options);
    }
}
