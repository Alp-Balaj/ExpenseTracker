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
// app.UseHttpsRedirection();
app.UseCors("ReactCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("OK"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();