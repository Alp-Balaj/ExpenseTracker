using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Infrastructure.DependencyInjections;

public static class DependencyInjection
{
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var dbProvider = configuration["DatabaseProvider"];

        if (dbProvider == "SQLServer")
        {
            services.AddDbContext<ApplicationDbContext, SQLServerDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("SQLServer")));
        }
        else if (dbProvider == "PostgreSQL")
        {
            var connectionString = configuration.GetConnectionString("PostgreSQL");

            if (!string.IsNullOrEmpty(connectionString) && connectionString.StartsWith("postgresql://"))
            {
                var uri = new Uri(connectionString);
                connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={uri.UserInfo.Split(':')[0]};Password={uri.UserInfo.Split(':')[1]}";
            }

            services.AddDbContext<ApplicationDbContext, PostgresContext>(options =>
                options.UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.MigrationsAssembly("ExpenseTracker.Infrastructure")
                ));
        }
        else
        {
            throw new Exception($"Couldn't find dbProvider: {dbProvider}");
        }

        services.AddSingleton<MongoDbContext>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserServices, CurrentUserServices>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IUserRelatedRepository<>), typeof(UserRelatedRepository<>));
        services.AddScoped(typeof(IAccountRepository), typeof(AccountRepository));
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITransactionReportService, TransactionReportService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();

        return services;
    }
}