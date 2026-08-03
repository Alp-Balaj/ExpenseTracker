using System.Globalization;
using System.Text;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Domain.Entities.User;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IAccountExportService"/>
/// <remarks>
/// Reads the tables directly rather than going through the repositories: an
/// export is the one operation that legitimately wants every row of every
/// user-owned table at once, and the repositories are shaped for one entity at a
/// time. Every query still filters on the current user's id explicitly — the
/// export is exactly the wrong place to rely on a filter being applied somewhere
/// else.
/// </remarks>
public sealed class AccountExportService : IAccountExportService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _currentUser;
    private readonly UserManager<User> _userManager;

    public AccountExportService(
        ApplicationDbContext context,
        ICurrentUserServices currentUser,
        UserManager<User> userManager)
    {
        _context = context;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    public async Task<AccountExportDTO> ExportAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var user = await _userManager.FindByIdAsync(userId);
        var preferences = await _context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var currencies = await _context.Currencies.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

        var categories = await _context.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CategoryType).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var accounts = await _context.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.Currency)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);

        var expenses = await _context.Expenses.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Include(e => e.Account).ThenInclude(a => a.Currency)
            .Include(e => e.Category)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var incomes = await _context.Incomes.AsNoTracking()
            .Where(i => i.UserId == userId)
            .Include(i => i.Account).ThenInclude(a => a.Currency)
            .Include(i => i.Category)
            .OrderBy(i => i.Date).ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var transfers = await _context.Transfers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var budgets = await _context.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);

        var goals = await _context.SavingGoals.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.Deadline)
            .ToListAsync(cancellationToken);

        var contributions = await _context.SavingContributions.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date)
            .ToListAsync(cancellationToken);

        var planned = await _context.PlannedTransactions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.StartDate)
            .ToListAsync(cancellationToken);

        var occurrences = await _context.PlannedOccurrences.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderBy(o => o.DueDate)
            .ToListAsync(cancellationToken);

        return new AccountExportDTO
        {
            Profile = new ExportProfileDTO
            {
                Email = user?.Email,
                UserName = user?.UserName,
                BaseCurrency = preferences?.BaseCurrency,
                Theme = preferences?.Theme
            },
            Currencies = currencies.Select(c => new ExportCurrencyDTO
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                Symbol = c.Symbol,
                ExchangeRateToBase = c.ExchangeRateToBase,
                RateUpdatedAtUtc = c.RateUpdatedAtUtc
            }).ToList(),
            Categories = categories.Select(c => new ExportCategoryDTO
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                CategoryType = (int)c.CategoryType,
                Color = c.Color
            }).ToList(),
            Accounts = accounts.Select(a => new ExportAccountDTO
            {
                Id = a.Id,
                Name = a.Name,
                AmountType = (int)a.AmountType,
                Balance = a.Balance,
                Description = a.Description,
                CurrencyId = a.CurrencyId,
                CurrencyCode = a.Currency?.Code ?? string.Empty
            }).ToList(),
            Expenses = expenses.Select(e => new ExportTransactionDTO
            {
                Id = e.Id,
                Date = e.Date,
                Title = e.Title,
                Amount = e.Amount,
                Description = e.Description,
                AccountId = e.AccountId,
                AccountName = e.Account?.Name ?? string.Empty,
                CurrencyCode = e.Account?.Currency?.Code ?? string.Empty,
                CategoryId = e.CategoryId,
                CategoryName = e.Category?.Name ?? string.Empty,
                CreatedAt = e.CreatedAt
            }).ToList(),
            Incomes = incomes.Select(i => new ExportTransactionDTO
            {
                Id = i.Id,
                Date = i.Date,
                // Income carries no title of its own; its description is what every
                // list shows, so that is what lands in the shared column.
                Title = i.Description,
                Amount = i.Amount,
                Description = i.Description,
                AccountId = i.AccountId,
                AccountName = i.Account?.Name ?? string.Empty,
                CurrencyCode = i.Account?.Currency?.Code ?? string.Empty,
                CategoryId = i.CategoryId,
                CategoryName = i.Category?.Name ?? string.Empty,
                CreatedAt = i.CreatedAt
            }).ToList(),
            Transfers = transfers.Select(t => new ExportTransferDTO
            {
                Id = t.Id,
                Date = t.Date,
                Amount = t.Amount,
                Description = t.Note,
                FromAccountId = t.FromAccountId,
                FromAccountName = accountNames.GetValueOrDefault(t.FromAccountId, string.Empty),
                ToAccountId = t.ToAccountId,
                ToAccountName = accountNames.GetValueOrDefault(t.ToAccountId, string.Empty),
                SavingGoalId = t.SavingGoalId,
                SavingContributionId = t.SavingContributionId,
                CreatedAt = t.CreatedAt
            }).ToList(),
            Budgets = budgets.Select(b => new ExportBudgetDTO
            {
                Id = b.Id,
                Amount = b.Amount,
                Period = (int)b.Period,
                Note = b.Note,
                CategoryId = b.CategoryId,
                CategoryName = b.CategoryId is Guid id ? categoryNames.GetValueOrDefault(id) : null
            }).ToList(),
            SavingGoals = goals.Select(g => new ExportSavingGoalDTO
            {
                Id = g.Id,
                Title = g.Title,
                TargetAmount = g.TargetAmount,
                SavedAmount = g.SavedAmount,
                Deadline = g.Deadline,
                Description = g.Description,
                AccountId = g.AccountId,
                CategoryId = g.CategoryId
            }).ToList(),
            SavingContributions = contributions.Select(c => new ExportSavingContributionDTO
            {
                Id = c.Id,
                GoalId = c.GoalId,
                Amount = c.Amount,
                Date = c.Date,
                Note = c.Note
            }).ToList(),
            PlannedTransactions = planned.Select(p => new ExportPlannedTransactionDTO
            {
                Id = p.Id,
                Title = p.Title,
                Amount = p.Amount,
                Description = p.Description,
                Kind = (int)p.Kind,
                Recurrence = (int)p.Recurrence,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsActive = p.IsActive,
                CategoryId = p.CategoryId,
                FromAccountId = p.FromAccountId,
                ToAccountId = p.ToAccountId,
                SavingGoalId = p.SavingGoalId
            }).ToList(),
            PlannedOccurrences = occurrences.Select(o => new ExportPlannedOccurrenceDTO
            {
                Id = o.Id,
                PlannedTransactionId = o.PlannedTransactionId,
                PeriodKey = o.PeriodKey,
                DueDate = o.DueDate,
                State = (int)o.State,
                CommittedAt = o.CommittedAt,
                ExpenseId = o.ExpenseId,
                IncomeId = o.IncomeId,
                TransferId = o.TransferId
            }).ToList()
        };
    }

    public async Task<string> ExportTransactionsCsvAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ExportAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("Date,Kind,Title,Category,Account,Amount,Currency,Description");

        // One sheet, sorted by date, with the kind as a column — the shape people
        // expect to be able to pivot. Transfers are included as two halves so the
        // sheet's own arithmetic balances; the JSON keeps them as single rows.
        var rows = snapshot.Expenses
            .Select(e => (e.Date, Kind: "Expense", e.Title, e.CategoryName, e.AccountName, Amount: -e.Amount, e.CurrencyCode, e.Description))
            .Concat(snapshot.Incomes
                .Select(i => (i.Date, Kind: "Income", i.Title, i.CategoryName, i.AccountName, Amount: i.Amount, i.CurrencyCode, i.Description)))
            .Concat(snapshot.Transfers
                .Select(t => (t.Date, Kind: "Transfer out", Title: (string?)"Transfer", CategoryName: string.Empty, AccountName: t.FromAccountName, Amount: -t.Amount, CurrencyCode: string.Empty, Description: t.Description)))
            .Concat(snapshot.Transfers
                .Select(t => (t.Date, Kind: "Transfer in", Title: (string?)"Transfer", CategoryName: string.Empty, AccountName: t.ToAccountName, Amount: t.Amount, CurrencyCode: string.Empty, Description: t.Description)))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Kind, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // Date and Amount go through as typed values, not pre-formatted
            // strings: a negative amount would otherwise trip the formula guard
            // and land in the sheet as text that will not sum.
            CsvWriter.AppendRow(
                builder,
                row.Date,
                row.Kind,
                row.Title,
                row.CategoryName,
                row.AccountName,
                row.Amount,
                row.CurrencyCode,
                row.Description);
        }

        return builder.ToString();
    }
}
