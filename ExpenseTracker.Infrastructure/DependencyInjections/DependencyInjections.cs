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
        // Sugg-6: CSV import. In Infrastructure because both phases need the
        // database — duplicate detection reads the account's existing rows, and
        // the commit writes many of them in one transaction.
        services.AddScoped<IStatementImportService, StatementImportService>();
        // C·4: the way out. Reads every user-owned table at once, which is the one
        // operation the per-entity repositories are the wrong shape for.
        services.AddScoped<IAccountExportService, AccountExportService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();
        // Sugg-4: the due queue behind planned transactions. In Infrastructure
        // because both generating and committing need the DbContext itself —
        // one to survive a unique-constraint race, the other to claim an
        // occurrence with a conditional UPDATE before it moves any money.
        services.AddScoped<IPlannedTransactionService, PlannedTransactionService>();

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

        // Stream C: signup seeds a starter catalogue for the new user; standalone so
        // demo-data fabrication can reuse it for any user id.
        services.AddScoped<IUserDataSeeder, UserDataSeeder>();

        // Stream C: outbound email. "Logging" (the default) sends nothing and writes
        // the message — links included — to the log. A real provider (decision D1)
        // slots in as another case behind the same config key, so switching is a
        // Railway env var (Email__Provider), not a code change at call sites.
        var emailProvider = configuration["Email:Provider"] ?? "Logging";
        switch (emailProvider)
        {
            case "Logging":
            default:
                services.AddScoped<IEmailSender, LoggingEmailSender>();
                break;
        }

        return services;
    }
}