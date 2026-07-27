using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Repositories;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Infrastructure.DependencyInjections;

public static class DependencyInjection
{
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Postgres is the only provider (Sugg-10 dropped the SQL Server one, whose
        // migration set had been stale since January, and the Mongo store that held
        // nothing but UserPreferences).
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

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserServices, CurrentUserServices>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IUserRelatedRepository<>), typeof(UserRelatedRepository<>));
        services.AddScoped(typeof(IAccountRepository), typeof(AccountRepository));
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITransactionReportService, TransactionReportService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();

        // Sugg-8: daily exchange rate refresh. frankfurter.app needs no API key, so
        // there is nothing here to keep out of source control.
        services.Configure<ExchangeRateOptions>(configuration.GetSection(ExchangeRateOptions.SectionName));
        services.AddHttpClient(FrankfurterExchangeRateProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ExchangeRateOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            // A hung upstream must not pin a background scope open for the default
            // 100 seconds; the provider treats the timeout as "keep existing rates".
            client.Timeout = options.Timeout;
        });
        services.AddScoped<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
        services.AddScoped<IExchangeRateUpdater, ExchangeRateUpdater>();

        return services;
    }
}