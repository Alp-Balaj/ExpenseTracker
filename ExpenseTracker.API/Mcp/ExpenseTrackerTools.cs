using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Security;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ExpenseTracker.API.Mcp;

public sealed record McpAccount(Guid Id, string Name, string Type, decimal Balance, decimal ReservedForGoals, string Currency);
public sealed record McpCategory(Guid Id, string Name, string Type, string? Description);
public sealed record McpTransaction(Guid Id, DateOnly Date, string? Title, decimal Amount, string Currency, string? Description, Guid AccountId, string Account, Guid CategoryId, string Category);
public sealed record McpTransactionPage(int TotalCount, int Offset, IReadOnlyList<McpTransaction> Items);
public sealed record McpLoggedTransaction(Guid Id, decimal NewAccountBalance, string Currency, bool AlreadyLogged);

/// <summary>
/// What an AI client can do with the caller's money.
/// <para>
/// Everything here runs inside the POST that carried the tool call, on that
/// request's scope, so the user-scoped repositories see the token's user exactly
/// as they see a JWT's. That depends on the transport staying stateless; see
/// Program.cs.
/// </para>
/// <para>
/// The two writes only ever add. Editing and deleting are left out deliberately:
/// a model that misreads a request should be able to leave a wrong row behind, not
/// rewrite financial history.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class ExpenseTrackerTools
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MaxIdempotencyKeyLength = 100;
    private const int CreatedStatus = StatusCodes.Status201Created;

    private readonly IHttpContextAccessor _http;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountServices _accountServices;
    private readonly IAccountRepository _accounts;
    private readonly IUserRelatedRepository<Category> _categories;
    private readonly IUserRelatedRepository<Expense> _expenses;
    private readonly IUserRelatedRepository<Income> _incomes;
    private readonly ISavingsReservationService _reservations;
    private readonly IBudgetService _budgets;
    private readonly IInsightsService _insights;
    private readonly ICashflowProjectionService _cashflow;
    private readonly IIdempotencyStore _idempotency;

    public ExpenseTrackerTools(
        IHttpContextAccessor http,
        IUnitOfWork unitOfWork,
        IAccountServices accountServices,
        IAccountRepository accounts,
        IUserRelatedRepository<Category> categories,
        IUserRelatedRepository<Expense> expenses,
        IUserRelatedRepository<Income> incomes,
        ISavingsReservationService reservations,
        IBudgetService budgets,
        IInsightsService insights,
        ICashflowProjectionService cashflow,
        IIdempotencyStore idempotency)
    {
        _http = http;
        _unitOfWork = unitOfWork;
        _accountServices = accountServices;
        _accounts = accounts;
        _categories = categories;
        _expenses = expenses;
        _incomes = incomes;
        _reservations = reservations;
        _budgets = budgets;
        _insights = insights;
        _cashflow = cashflow;
        _idempotency = idempotency;
    }

    #region Reads
    [McpServerTool(Name = "list_accounts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists the user's accounts with balances. Each balance is in that account's own currency; never add balances of different currencies together. reservedForGoals is the part of the balance set aside for savings goals. Use the ids with log_expense, log_income and the list filters.")]
    public async Task<IReadOnlyList<McpAccount>> ListAccounts(CancellationToken cancellationToken)
    {
        var (accounts, _) = await _accounts.GetUserDataPageWithCurrencyAsync(null, null, cancellationToken);
        var reserved = await _reservations.GetReservedByAccountAsync(cancellationToken);

        return accounts
            .Select(a => new McpAccount(
                a.Id,
                a.Name,
                a.AmountType.ToString(),
                a.Balance,
                reserved.TryGetValue(a.Id, out var amount) ? amount : 0m,
                a.Currency.Code))
            .ToList();
    }

    [McpServerTool(Name = "list_categories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists the user's categories. Each has a type: Expense, Income or SavingGoals. log_expense needs an Expense category and log_income an Income one.")]
    public async Task<IReadOnlyList<McpCategory>> ListCategories(CancellationToken cancellationToken) =>
        await _categories.Query()
            .OrderBy(c => c.CategoryType).ThenBy(c => c.Name)
            .Select(c => new McpCategory(c.Id, c.Name, c.CategoryType.ToString(), c.Description))
            .ToListAsync(cancellationToken);

    [McpServerTool(Name = "list_expenses", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists expenses, newest first, with optional filters. totalCount is the number of matches before paging; if it exceeds the items returned, page with offset. Amounts are in each expense's account currency. For month-level analysis prefer get_spending_insights.")]
    public async Task<McpTransactionPage> ListExpenses(
        [Description("Earliest date, inclusive (YYYY-MM-DD).")] DateOnly? from = null,
        [Description("Latest date, inclusive (YYYY-MM-DD).")] DateOnly? to = null,
        [Description("Only this account.")] Guid? accountId = null,
        [Description("Only this category.")] Guid? categoryId = null,
        [Description("Case-insensitive text to find in the title or description.")] string? search = null,
        [Description("Rows to return, 1-200. Default 50.")] int limit = DefaultPageSize,
        [Description("Rows to skip.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(limit, offset);

        var rows = _expenses.Query();
        if (from is not null) rows = rows.Where(e => e.Date >= from);
        if (to is not null) rows = rows.Where(e => e.Date <= to);
        if (accountId is not null) rows = rows.Where(e => e.AccountId == accountId);
        if (categoryId is not null) rows = rows.Where(e => e.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePattern(search);
            rows = rows.Where(e =>
                EF.Functions.ILike(e.Title, pattern, "\\")
                || (e.Description != null && EF.Functions.ILike(e.Description, pattern, "\\")));
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
            .Skip(offset).Take(limit)
            .Select(e => new McpTransaction(
                e.Id, e.Date, e.Title, e.Amount, e.Account.Currency.Code, e.Description,
                e.AccountId, e.Account.Name, e.CategoryId, e.Category.Name))
            .ToListAsync(cancellationToken);

        return new McpTransactionPage(total, offset, items);
    }

    [McpServerTool(Name = "list_incomes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists incomes, newest first, with optional filters. totalCount is the number of matches before paging; if it exceeds the items returned, page with offset. Amounts are in each income's account currency.")]
    public async Task<McpTransactionPage> ListIncomes(
        [Description("Earliest date, inclusive (YYYY-MM-DD).")] DateOnly? from = null,
        [Description("Latest date, inclusive (YYYY-MM-DD).")] DateOnly? to = null,
        [Description("Only this account.")] Guid? accountId = null,
        [Description("Only this category.")] Guid? categoryId = null,
        [Description("Rows to return, 1-200. Default 50.")] int limit = DefaultPageSize,
        [Description("Rows to skip.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(limit, offset);

        var rows = _incomes.Query();
        if (from is not null) rows = rows.Where(i => i.Date >= from);
        if (to is not null) rows = rows.Where(i => i.Date <= to);
        if (accountId is not null) rows = rows.Where(i => i.AccountId == accountId);
        if (categoryId is not null) rows = rows.Where(i => i.CategoryId == categoryId);

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Skip(offset).Take(limit)
            .Select(i => new McpTransaction(
                i.Id, i.Date, null, i.Amount, i.Account.Currency.Code, i.Description,
                i.AccountId, i.Account.Name, i.CategoryId, i.Category.Name))
            .ToListAsync(cancellationToken);

        return new McpTransactionPage(total, offset, items);
    }

    [McpServerTool(Name = "get_budget_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Shows each budget's limit, spend and remaining amount for one month, in the user's base currency.")]
    public async Task<IReadOnlyList<BudgetStatusDTO>> GetBudgetStatus(
        [Description("The month as YYYY-MM. Defaults to the current month.")] string? month = null,
        CancellationToken cancellationToken = default)
    {
        var date = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(month)
            && !DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            throw new McpException("month must be in YYYY-MM format.");
        }

        return await _budgets.GetStatusAsync(date.Year, date.Month, cancellationToken);
    }

    [McpServerTool(Name = "get_spending_insights", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compares this month's spending with the previous month and the trailing three-month average, overall and per category, in the user's base currency. If hasMissingRates is true the totals leave out currencies with no exchange rate.")]
    public Task<InsightsDTO> GetSpendingInsights(CancellationToken cancellationToken) =>
        _insights.GetInsightsAsync(cancellationToken);

    [McpServerTool(Name = "get_cashflow_projection", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Projects the user's liquid balance day by day over the next month from current balances, planned bills and income, and the everyday spending rate. In the user's base currency.")]
    public Task<CashflowProjectionDTO> GetCashflowProjection(CancellationToken cancellationToken) =>
        _cashflow.GetProjectionAsync(cancellationToken);
    #endregion

    #region Writes
    [McpServerTool(Name = "log_expense", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Records an expense and debits its account. This changes the user's real balance and cannot be undone from here, so confirm the amount, account and category with the user unless they stated them. The amount is in the account's currency. Needs a token with write access. Pass a fresh idempotencyKey (any unique string) and reuse it only when retrying this same expense, so a retry cannot debit twice.")]
    public Task<McpLoggedTransaction> LogExpense(
        [Description("Short label, e.g. 'Groceries at Lidl'. At most 100 characters.")] string title,
        [Description("Amount in the account's currency, greater than zero.")] decimal amount,
        [Description("When it happened (YYYY-MM-DD), in the user's local calendar.")] DateOnly date,
        [Description("Id from list_accounts.")] Guid accountId,
        [Description("Id of an Expense category from list_categories.")] Guid categoryId,
        [Description("Optional note. At most 500 characters.")] string? description = null,
        [Description("Unique string that makes retries safe.")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new ExpenseDTO
        {
            Title = title,
            Amount = amount,
            Date = date,
            Description = description,
            AccountId = accountId,
            CategoryId = categoryId
        };

        return PostAsync("mcp:log_expense", idempotencyKey, dto, CategoryType.Expense, categoryId, accountId, -amount, _expenses, dto.ToEntity(), cancellationToken);
    }

    [McpServerTool(Name = "log_income", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Records income and credits its account. This changes the user's real balance and cannot be undone from here, so confirm the amount, account and category with the user unless they stated them. The amount is in the account's currency. Needs a token with write access. Pass a fresh idempotencyKey (any unique string) and reuse it only when retrying this same income, so a retry cannot credit twice.")]
    public Task<McpLoggedTransaction> LogIncome(
        [Description("Amount in the account's currency, greater than zero.")] decimal amount,
        [Description("When it was received (YYYY-MM-DD), in the user's local calendar.")] DateOnly date,
        [Description("Id from list_accounts.")] Guid accountId,
        [Description("Id of an Income category from list_categories.")] Guid categoryId,
        [Description("Optional note. At most 500 characters.")] string? description = null,
        [Description("Unique string that makes retries safe.")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new IncomeDTO
        {
            Amount = amount,
            Date = date,
            Description = description,
            AccountId = accountId,
            CategoryId = categoryId
        };

        return PostAsync("mcp:log_income", idempotencyKey, dto, CategoryType.Income, categoryId, accountId, amount, _incomes, dto.ToEntity(), cancellationToken);
    }

    /// <summary>
    /// The MCP counterpart of TransactionControllerBase.Create plus the two things
    /// MVC gives a controller for free and a tool does not get: DTO validation and
    /// the idempotency filter. Row and balance move in one transaction.
    /// </summary>
    private async Task<McpLoggedTransaction> PostAsync<TEntity>(
        string endpoint,
        string? idempotencyKey,
        object dto,
        CategoryType categoryType,
        Guid categoryId,
        Guid accountId,
        decimal balanceDelta,
        IUserRelatedRepository<TEntity> repository,
        TEntity entity,
        CancellationToken cancellationToken)
        where TEntity : BaseEntity
    {
        RequireWriteAccess();
        Validate(dto);

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > MaxIdempotencyKeyLength })
            throw new McpException($"idempotencyKey must be at most {MaxIdempotencyKeyLength} characters.");

        if (key is not null)
        {
            var claim = await _idempotency.TryClaimAsync(key, endpoint, cancellationToken);
            switch (claim.Claim)
            {
                case IdempotencyClaim.AlreadyCompleted:
                    return JsonSerializer.Deserialize<McpLoggedTransaction>(claim.ResponseBody!)! with { AlreadyLogged = true };
                case IdempotencyClaim.InFlight:
                    throw new McpException("That request is still being processed. Check the account before retrying.");
                case IdempotencyClaim.EndpointMismatch:
                    throw new McpException("That idempotencyKey was already used for a different kind of request.");
            }
        }

        try
        {
            var category = await _categories.GetUserDataByIdAsync(categoryId, cancellationToken);
            if (category is null || category.CategoryType != categoryType)
                throw new McpException($"No {categoryType} category with that id. Use list_categories.");

            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var (success, error) = await _accountServices.AdjustAccountBalance(balanceDelta, accountId, cancellationToken);
            if (!success) throw new McpException(error);

            await repository.AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // A failed write applied nothing, so the retry must be a fresh attempt.
            if (key is not null) await _idempotency.AbandonAsync(key, CancellationToken.None);
            throw;
        }

        var account = await _accounts.GetUserDataWithCurrencyByIdAsync(accountId, cancellationToken);
        var result = new McpLoggedTransaction(entity.Id, account!.Balance, account.Currency.Code, AlreadyLogged: false);

        if (key is not null)
            await _idempotency.CompleteAsync(key, CreatedStatus, JsonSerializer.Serialize(result), CancellationToken.None);

        return result;
    }
    #endregion

    private void RequireWriteAccess()
    {
        if (_http.HttpContext?.User.HasClaim(c => c.Type == McpAuthDefaults.WriteClaim) != true)
            throw new McpException("This token is read-only. Create one with write access in the app's settings to log transactions.");
    }

    private static void Validate(object dto)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), errors, validateAllProperties: true))
            throw new McpException(string.Join(" ", errors.Select(e => e.ErrorMessage)));
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > MaxPageSize)
            throw new McpException($"limit must be between 1 and {MaxPageSize}.");
        if (offset < 0)
            throw new McpException("offset must be 0 or greater.");
    }

    private static string LikePattern(string search)
    {
        var escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }
}
