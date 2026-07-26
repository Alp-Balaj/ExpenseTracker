using ExpenseTracker.API.Extensions;
using ExpenseTracker.API.Middleware;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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
app.UseStaticFiles();

app.UseCors("ReactCors");
app.UseRateLimiter();
app.UseAuthentication();
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
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();

// Exposes the top-level-statement entry point so WebApplicationFactory<Program>
// in ExpenseTracker.Tests can boot this app in-process.
public partial class Program { }