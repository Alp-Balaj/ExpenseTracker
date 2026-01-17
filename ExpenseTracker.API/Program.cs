using ExpenseTracker.API.Extensions;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

#region Costum Services and Configurations

//Identity and JWT
builder.Services.AddAuthServices(builder.Configuration);
//Application services
builder.Services.AddApplicationServices();
//Infrastructure services
builder.Services.AddInfrastructureServices(builder.Configuration);
//Swagger configuration
builder.Services.AddCustomSwagger();
//Cors configuration
builder.Services.AddReactCors();

#endregion

var app = builder.Build();

app.UseCustomSwagger(app.Environment.IsDevelopment());

app.UseExceptionHandler("/error");

app.UseHttpsRedirection();

app.UseCors("ReactCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();