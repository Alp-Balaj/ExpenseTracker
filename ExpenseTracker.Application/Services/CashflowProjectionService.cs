using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services;

public sealed class CashflowProjectionService : ICashflowProjectionService
{
    /// <summary>
    /// Days projected beyond today. 30 keeps the window a fixed length and —
    /// because no month is more than 31 days long — guarantees the end of the
    /// current month always sits inside it, wherever in the month today falls.
    /// </summary>
    private const int HorizonDays = 30;

    /// <summary>The accounts money can actually be spent from — credit and investments are not liquid.</summary>
    private static readonly AmountType[] LiquidTypes =
    [
        AmountType.CheckingAccount,
        AmountType.SavingsAccount,
        AmountType.Cash,
    ];

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrencyConversionService _conversion;

    public CashflowProjectionService(IUnitOfWork unitOfWork, ICurrencyConversionService conversion)
    {
        _unitOfWork = unitOfWork;
        _conversion = conversion;
    }

    public async Task<CashflowProjectionDTO> GetProjectionAsync(CancellationToken cancellationToken = default)
    {
        // The same UTC "today" the occurrence generator measures "due" against,
        // so the projection and the planned queue can never disagree.
        var today = PlannedSchedule.Today;
        var end = today.AddDays(HorizonDays);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var endOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);

        var converter = await _conversion.GetConverterAsync(cancellationToken);
        var hasMissingRates = false;

        // ── Opening position: what is spendable right now ────────────────
        var accounts = await _unitOfWork.Accounts.Query()
            .Select(a => new { a.Id, a.AmountType, a.Balance, Code = a.Currency.Code })
            .ToListAsync(cancellationToken);

        var openingBalance = Round2(CurrencyTotals.SumConvertible(
            converter,
            accounts
                .Where(a => LiquidTypes.Contains(a.AmountType))
                .Select(a => new CurrencyAmount(a.Code, a.Balance)),
            ref hasMissingRates));

        // ── Everyday burn: this month's spend divided by the days elapsed ─
        // Expenses posted by committing a planned occurrence are excluded:
        // those bills recur on their own schedule and are projected from the
        // templates below, so leaving them in the burn rate would count each
        // one twice — once as history, once as a future due date.
        var monthSpend = await _unitOfWork.Expenses.Query()
            .Where(e => e.Date >= firstOfMonth && e.Date <= today)
            .GroupBy(e => e.Account.Currency.Code)
            .Select(g => new { Code = g.Key, Total = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);

        var committedPlanned = await _unitOfWork.PlannedOccurrences.Query()
            .Where(o => o.State == PlannedOccurrenceState.Committed
                && o.ExpenseId != null
                && o.Expense!.Date >= firstOfMonth
                && o.Expense.Date <= today)
            .Select(o => new { Code = o.Expense!.Account.Currency.Code, o.Expense.Amount })
            .ToListAsync(cancellationToken);

        var monthToDate = CurrencyTotals.SumConvertible(
            converter,
            monthSpend.Select(r => new CurrencyAmount(r.Code, r.Total)),
            ref hasMissingRates);
        var plannedPart = CurrencyTotals.SumConvertible(
            converter,
            committedPlanned.Select(r => new CurrencyAmount(r.Code, r.Amount)),
            ref hasMissingRates);

        var dailyBurn = Round2(Math.Max(0m, monthToDate - plannedPart) / today.Day);

        // ── Planned flows inside the window ───────────────────────────────
        // Two sources, same as the dashboard's "coming up" widget. Pending
        // occurrences are money genuinely owed now (the generator only ever
        // materialises a row once it falls due, so these are today's and
        // overdue bills). The templates contribute their computed future due
        // dates — without them the projection could only ever see what is late.
        var flows = new List<(DateOnly Date, PlannedKind Kind, decimal Amount, string? Code)>();

        var pending = await _unitOfWork.PlannedOccurrences.Query()
            .Where(o => o.State == PlannedOccurrenceState.Pending)
            .Select(o => new
            {
                o.DueDate,
                o.PlannedTransaction.Kind,
                o.PlannedTransaction.Amount,
                FromCode = o.PlannedTransaction.FromAccount != null
                    ? o.PlannedTransaction.FromAccount.Currency.Code
                    : null,
                ToCode = o.PlannedTransaction.ToAccount != null
                    ? o.PlannedTransaction.ToAccount.Currency.Code
                    : null,
            })
            .ToListAsync(cancellationToken);

        foreach (var row in pending)
        {
            // An overdue bill has not left the balance yet, but it will —
            // assume the money goes on day one rather than pretending the
            // debt vanished.
            var date = row.DueDate < today ? today : row.DueDate;
            if (date > end) continue;
            flows.Add((date, row.Kind, row.Amount, row.Kind == PlannedKind.Income ? row.ToCode : row.FromCode));
        }

        var accountCodes = accounts.ToDictionary(a => a.Id, a => a.Code);
        var templates = (await _unitOfWork.PlannedTransactions.GetAllUserDataAsync(cancellationToken))
            .Where(t => t.IsActive && t.Kind != PlannedKind.Transfer);

        foreach (var template in templates)
        {
            // Strictly after today: today's occurrence (if any) already exists
            // as a Pending row and was counted above.
            var cursor = today;
            while (PlannedSchedule.NextDueAfter(template, cursor) is DateOnly due && due <= end)
            {
                var accountId = template.Kind == PlannedKind.Income
                    ? template.ToAccountId
                    : template.FromAccountId;
                var code = accountId is Guid id ? accountCodes.GetValueOrDefault(id) : null;
                flows.Add((due, template.Kind, template.Amount, code));
                cursor = due;
            }
        }

        // Transfers move money between the user's own accounts, so they never
        // change the liquid total and are excluded above. A planned amount with
        // no account is treated as already being in the base currency — the
        // same rule CurrencyConverter applies to a null code.
        var byDay = new Dictionary<DateOnly, (decimal Outflow, decimal Inflow)>();
        foreach (var flow in flows)
        {
            var converted = Round2(CurrencyTotals.SumConvertible(
                converter, [new CurrencyAmount(flow.Code, flow.Amount)], ref hasMissingRates));

            var (outflow, inflow) = byDay.GetValueOrDefault(flow.Date);
            if (flow.Kind == PlannedKind.Income) inflow += converted;
            else outflow += converted;
            byDay[flow.Date] = (outflow, inflow);
        }

        // ── The series ────────────────────────────────────────────────────
        // Today's actual spending is already in the account balances, so the
        // burn starts on tomorrow's point; planned flows land on their due day.
        var points = new List<CashflowPointDTO>(HorizonDays + 1);
        var running = openingBalance;
        for (var date = today; date <= end; date = date.AddDays(1))
        {
            if (date != today) running -= dailyBurn;
            var (outflow, inflow) = byDay.GetValueOrDefault(date);
            running += inflow - outflow;

            points.Add(new CashflowPointDTO
            {
                Date = date,
                ProjectedBalance = running,
                PlannedOutflow = outflow,
                PlannedInflow = inflow,
            });
        }

        return new CashflowProjectionDTO
        {
            Month = $"{today.Year:D4}-{today.Month:D2}",
            BaseCurrency = converter.BaseCurrency,
            HasMissingRates = hasMissingRates,
            Today = today,
            EndOfMonth = endOfMonth,
            StartingBalance = openingBalance,
            DailyBurnRate = dailyBurn,
            ProjectedEndOfMonthBalance = points.First(p => p.Date == endOfMonth).ProjectedBalance,
            ProjectedEndBalance = points[^1].ProjectedBalance,
            Points = points,
        };
    }

    private static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
