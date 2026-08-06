using ExpenseTracker.API.Extensions;
using ExpenseTracker.API.Middleware;
using ExpenseTracker.API.Services;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Port binding for Railway
builder.WebHost.UseUrls(
    $"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}"
);


builder.Services.AddEndpointsApiExplorer(); 
builder.Services.AddControllers();

#region Custom Services and Configurations
//Identity and JWT
builder.Services.AddAuthServices(builder.Configuration);
//Infrastructure services
builder.Services.AddInfrastructureServices(builder.Configuration);
//Application services
builder.Services.AddApplicationServices();
//Swagger configuration
builder.Services.AddCustomSwagger();
//Cors configuration
builder.Services.AddReactCors(builder.Configuration);
//Stream P: the one background host. Ticks, asks every registered IScheduledJob
//whether it is due against a persisted last-success timestamp, and runs the ones
//that are — catch-up by design, because Railway's single container is free to
//sleep and every deploy replaces it. Absorbed Sugg-8's ExchangeRateRefreshService,
//which is now ScheduledJobNames.ExchangeRates and still honours ExchangeRates:Enabled.
//Self-disables when Scheduler:Enabled is false (the test suite sets that), so it is
//registered unconditionally and decides for itself whether to do any work.
builder.Services.AddHostedService<SchedulerHostedService>();
//Global exception -> ProblemDetails mapping (400/404/409/500)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
//Rate limiting for the auth endpoints (login/signup/refresh): 10 requests/min per client IP
//by default. Behind the Railway proxy RemoteIpAddress is the proxy's, so the limit is
//effectively shared there; per-account brute force is still stopped by Identity lockout.
//Configurable (not just hardcoded) so ExpenseTracker.Tests can raise the ceiling -- every
//WebApplicationFactory test runs from the same loopback IP, and signup+login per test would
//otherwise trip the limit within seconds.
var authRateLimitPermits = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10;
var authRateLimitWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60);
//General limiter over the whole API surface: a publicly reachable API with open registration
//shouldn't rely on the auth lane alone. Partitioned per authenticated user (NameIdentifier
//claim) so one abusive account can't starve the rest; anonymous traffic falls back to the
//client IP, which behind Railway's proxy is the proxy's — the same shared-partition caveat as
//the auth lane, hence the generous ceiling. Requires UseRateLimiter to run after
//UseAuthentication so the claim is populated.
var globalRateLimitPermits = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 300;
var globalRateLimitWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitPermits,
                Window = authRateLimitWindow,
                QueueLimit = 0
            }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalRateLimitPermits,
                Window = globalRateLimitWindow,
                QueueLimit = 0
            }));
});
#endregion

var app = builder.Build();

app.UseCustomSwagger(app.Environment.IsDevelopment());
app.UseExceptionHandler();

// Baseline security headers on every response (API, Swagger UI, and the SPA in wwwroot).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

// HSTS outside Development. Note: the middleware only emits the header on HTTPS requests,
// and Railway forwards plain http (TLS ends at its proxy) — so it takes effect only where
// the app itself terminates TLS. Harmless no-op behind the proxy.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Development only: Railway terminates TLS at its proxy and forwards plain http, so
// redirecting in production would bounce every request into a loop.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Single-origin serving: the built SPA ships in wwwroot (copied in by the Dockerfile) and
// the API already lives under /api/*, so no path rewrite is needed — the SPA's axios
// baseURL is empty in production and every call is same-origin. Static-file middleware
// sits outside endpoint routing, so [Authorize] never applies to public assets. In local
// dev wwwroot doesn't exist and both this and the fallback below are no-ops (the Vite dev
// server on :6969 serves the SPA and talks to this API cross-origin via ReactCors).
//
// Cache policy (Suggestion-12): Vite fingerprints everything under /assets, so those
// files may be cached forever; everything else — index.html, the service workers, the
// manifest — is unhashed, and without an explicit no-cache the browser's heuristic
// caching keeps a stale index.html alive across deploys. A stale index.html asks for
// chunk names that no longer exist, which surfaces as a blank page on the first
// refresh after a deploy.
var spaStaticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    }
};
app.UseStaticFiles(spaStaticFiles);

app.UseCors("ReactCors");
app.UseAuthentication();
// After UseAuthentication on purpose: the global limiter partitions by the user's
// NameIdentifier claim, which is only populated once the JWT has been validated.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("OK"));

// An unmatched path under /api is a genuine 404, not a page. Without this the SPA fallback
// below would answer it with index.html and axios would try to parse HTML as JSON. The
// literal segments of real controller routes outrank this catch-all, so it only ever
// catches misses.
app.Map("/api/{**rest}", () => Results.NotFound());

// SPA fallback — client-routed deep links (/login, /dashboard) must serve index.html on a
// cold GET. Lowest routing priority, so it never shadows an /api/* controller route.
// Same options as above: a deep-link refresh is precisely the request that must not be
// answered from a stale cache.
app.MapFallbackToFile("index.html", spaStaticFiles);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();

// Exposes the top-level-statement entry point so WebApplicationFactory<Program>
// in ExpenseTracker.Tests can boot this app in-process.
public partial class Program { }