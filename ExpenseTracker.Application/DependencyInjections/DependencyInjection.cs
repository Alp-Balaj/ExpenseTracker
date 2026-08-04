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
            services.AddScoped<ISavingsReservationService, SavingsReservationService>();
            // Shared by TransferController and by committing a planned transfer,
            // so the two can never drift apart on how a transfer is booked.
            services.AddScoped<ITransferPostingService, TransferPostingService>();
            services.AddScoped<IInsightsService, InsightsService>();
            // Sugg-11: moved out of Infrastructure — it only needs IUnitOfWork
            // and LINQ, so it is application logic, not persistence.
            services.AddScoped<ITransactionReportService, TransactionReportService>();
            services.AddScoped<ICashflowProjectionService, CashflowProjectionService>();
            // Stream R: split bills. Shared by ReimbursementController and by the
            // expense write path, so the receivable and the expense that created
            // it can never disagree.
            services.AddScoped<IReimbursementService, ReimbursementService>();
            // Cycle U W1: the shared pot. Also called from UserController's
            // account-deletion path, which has to anonymize memberships before
            // Identity removes the user — the membership FK is Restrict, so
            // skipping that step fails at the database rather than cascading a
            // group's history away.
            services.AddScoped<IPotService, PotService>();

            return services;
        }
    }
}
