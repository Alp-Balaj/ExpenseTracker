using ExpenseTracker.API.Extensions;
using ExpenseTracker.Application.DependencyInjection;
using ExpenseTracker.Infrastructure.DependencyInjections;
using ExpenseTracker.Infrastructure.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

#region Costum Services and Configurations

//Identity and JWT
builder.Services.AddAuthServices(builder.Configuration);
//Infrastructure services
builder.Services.AddInfrastructureServices(builder.Configuration);
//Application services
builder.Services.AddApplicationServices();
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