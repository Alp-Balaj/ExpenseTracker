using ExpenseTracker.API.Extensions;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

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
#endregion

var app = builder.Build();

app.UseCustomSwagger(app.Environment.IsDevelopment());
app.UseExceptionHandler("/error");

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