using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Application.DependencyInjection
{
    public static class DependencyInjection
    {
        
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {

            services.AddScoped<IBudgetService, BudgetService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();
            services.AddScoped<IAccountServices, AccountServices>();
            services.AddScoped<ISavingGoalServices, SavingGoalServices>();
            // Shared by TransferController and by committing a planned transfer,
            // so the two can never drift apart on how a transfer is booked.
            services.AddScoped<ITransferPostingService, TransferPostingService>();
            services.AddScoped<IInsightsService, InsightsService>();
            // Sugg-11: moved out of Infrastructure — it only needs IUnitOfWork
            // and LINQ, so it is application logic, not persistence.
            services.AddScoped<ITransactionReportService, TransactionReportService>();
            services.AddScoped<ICashflowProjectionService, CashflowProjectionService>();

            return services;
        }
    }
}
