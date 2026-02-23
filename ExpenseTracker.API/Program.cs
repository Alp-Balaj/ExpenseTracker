using ExpenseTracker.API.Extensions;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var skipDb = builder.Configuration.GetValue<bool>("SKIP_DB");

builder.Services.AddEndpointsApiExplorer(); 
builder.Services.AddControllers();


#region Costum Services and Configurations

//Identity and JWT
builder.Services.AddAuthServices(builder.Configuration);
//Infrastructure services
if (!skipDb)
{
    builder.Services.AddInfrastructureServices(builder.Configuration);
}
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

app.UseHttpsRedirection();

app.UseCors("ReactCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok("OK"));

app.Run();