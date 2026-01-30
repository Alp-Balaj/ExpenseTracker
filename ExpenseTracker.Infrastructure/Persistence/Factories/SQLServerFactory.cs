using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ExpenseTracker.Infrastructure.Persistence.Factories;

public class SQLServerFactory
    : IDesignTimeDbContextFactory<SQLServerContext>
{
    public SQLServerContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Migrations.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("SQLServer");

        var optionsBuilder = new DbContextOptionsBuilder<SQLServerContext>();

        var options = new DbContextOptionsBuilder<SQLServerContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SQLServerContext(options);
    }
}
