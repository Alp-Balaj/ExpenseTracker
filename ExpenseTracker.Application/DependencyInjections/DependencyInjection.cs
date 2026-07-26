using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Application.DependencyInjection
{
    public static class DependencyInjection
    {
        
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {

            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();
            services.AddScoped<IAccountServices, AccountServices>();
            services.AddScoped<ISavingGoalServices, SavingGoalServices>();

            return services;
        }
    }
}
