using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace ExpenseTracker.Tests.Infrastructure;

// One real Postgres container backs every test in the collection (see
// IntegrationTestCollection) — migrations run automatically via Program.cs's
// startup db.Database.Migrate(), same as in production.
//
// Overrides are pushed via process environment variables, not
// WebHostBuilder.ConfigureAppConfiguration: Program.cs reads Jwt:Key/Issuer,
// the Postgres connection string and the auth rate limit synchronously
// *before* WebApplicationBuilder.Build() runs, and under the minimal hosting
// model ConfigureAppConfiguration is only merged in at Build() -- too late
// for those reads, so they'd silently fall through to whatever this project's
// Development user-secrets (or appsettings) provide instead. Environment
// variables are loaded inside WebApplication.CreateBuilder(args) itself,
// before any of Program.cs's own code runs, and outrank user secrets in the
// default provider order, so they're the only override visible everywhere.
// (This was found the hard way: without it, the suite silently ran against
// the real local "ExpenseTrackerRepro" Postgres from this project's
// user-secrets instead of the ephemeral container below.)
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__PostgreSQL", _postgres.GetConnectionString());
        // 32+ byte HMAC key required by JWTServices at startup.
        Environment.SetEnvironmentVariable("Jwt__Key", "test-only-signing-key-at-least-32-bytes-long!!");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://tests.local");
        Environment.SetEnvironmentVariable("Jwt__Audience", "https://tests.local");
        Environment.SetEnvironmentVariable("RefreshToken__Longevity", "14");
        Environment.SetEnvironmentVariable("RefreshToken__Pepper", "test-only-pepper");
        // Every test signs up + logs in its own user from the same loopback IP;
        // the production default (10/min) would throttle the suite itself (Fix-13).
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__WindowSeconds", "60");
        // Sugg-8: keep the daily rate sweep from starting under test. The manual
        // refresh endpoint is unaffected and is what the rate tests drive, through
        // the stub provider registered below — no test may depend on the network.
        Environment.SetEnvironmentVariable("ExchangeRates__Enabled", "false");
    }

    /// <summary>
    /// The fake upstream behind /api/Currency/RefreshRates. Mutate its
    /// <see cref="StubExchangeRateProvider.Snapshots"/> to model what the ECB publishes.
    /// </summary>
    public StubExchangeRateProvider RateProvider { get; } = new();

    /// <summary>
    /// Counts the SQL commands each request sends. Tests in this collection run
    /// one at a time, so a test may <see cref="QueryCountingInterceptor.Reset"/>
    /// it, drive one request, and read the count back.
    /// </summary>
    public QueryCountingInterceptor QueryCounter { get; } = new();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Belt and suspenders: skip Development-only user secrets even if the
        // DOTNET_ENVIRONMENT env var above were ever bypassed.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Swap the real HTTP provider for a deterministic one. ConfigureTestServices
            // runs after the app's own registrations, so this wins.
            services.RemoveAll<IExchangeRateProvider>();
            services.AddSingleton<IExchangeRateProvider>(RateProvider);

            // Re-register the context purely to attach the query counter:
            // command interceptors are not among the kinds EF discovers from
            // the application service provider, so they have to go on the
            // options. These are the same options AddInfrastructureServices
            // builds — only AddInterceptors is new.
            services.RemoveAll<DbContextOptions<PostgresContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<ApplicationDbContext, PostgresContext>(options =>
                options
                    .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                        npgsql.MigrationsAssembly("ExpenseTracker.Infrastructure"))
                    .AddInterceptors(QueryCounter));
        });
    }
}
