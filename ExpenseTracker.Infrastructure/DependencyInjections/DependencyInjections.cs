using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace ExpenseTracker.Infrastructure.DependencyInjections
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var dbProvider = configuration["DatabaseProvider"];

            if (dbProvider == "SQLServer")
            {
                services.AddDbContext<SQLServerContext>(options => 
                    options.UseSqlServer(configuration.GetConnectionString("SQLServer")));
            }
            else if(dbProvider == "PostgreSQL")
            {
                services.AddDbContext<PostgresContext>(options =>
                    options.UseNpgsql(configuration.GetConnectionString("PostgreSQL")));
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

            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

            services.AddScoped<ITransactionReportService, TransactionReportService>();

            services.AddScoped<IUserPreferencesService, UserPreferencesService>();
            return services;
        }
    }
}