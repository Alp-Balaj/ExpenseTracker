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

    /// <summary>
    /// How long a split bill is assumed to take to come back, in days. Matches
    /// the fortnight after which <c>WeeklyDigestJob</c> starts calling a
    /// receivable overdue, so the projection and the nag agree on when a
    /// friend has become slow.
    /// </summary>
    private const int SettlementLagDays = 14;

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
        //
        // Stream R: the numerator is net of split bills. The burn rate answers
        // "how fast is my own money going", and the part a friend is expected to
        // hand back is not going anywhere — it comes back as an inflow further
        // down, on its own expected date.
        var monthSpend = await _unitOfWork.Expenses.Query()
            .Where(e => e.Date >= firstOfMonth && e.Date <= today)
            .GroupBy(e => e.Account.Currency.Code)
            .Select(g => new
            {
                Code = g.Key,
                Total = g.Sum(e => e.Amount - (e.Reimbursement == null ? 0m : e.Reimbursement.ExpectedAmount))
            })
            .ToListAsync(cancellationToken);

        // Netted the same way, and it has to be: this figure is not an analysis
        // total in its own right, it is the correction term subtracted from the
        // numerator above. Whatever a committed bill contributed there is exactly
        // what has to come back out here — leave it gross against a net numerator
        // and a planned bill that happened to be split would subtract more than
        // it ever added, quietly dragging the burn rate below the truth (or into
        // the Math.Max(0) floor, which hides the error completely).
        var committedPlanned = await _unitOfWork.PlannedOccurrences.Query()
            .Where(o => o.State == PlannedOccurrenceState.Committed
                && o.ExpenseId != null
                && o.Expense!.Date >= firstOfMonth
                && o.Expense.Date <= today)
            .Select(o => new
            {
                Code = o.Expense!.Account.Currency.Code,
                Amount = o.Expense.Amount
                    - (o.Expense.Reimbursement == null ? 0m : o.Expense.Reimbursement.ExpectedAmount)
            })
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

        // ── Money owed to the user (Stream R, sentence 4) ─────────────────
        // A receivable is a real expected inflow, so the projection has to see
        // it: the burn rate above already stopped counting the fronted part as
        // spending, and this is where it comes back.
        var receivables = await _unitOfWork.Reimbursements.Query()
            .Select(r => new
            {
                r.Expense.Date,
                Code = r.Expense.Account.Currency.Code,
                r.ExpectedAmount,
                // Nullable: SUM over no settlement rows is NULL, and a receivable
                // nobody has paid anything towards is the ordinary case.
                Settled = r.Settlements.Sum(s => (decimal?)s.Amount)
            })
            .ToListAsync(cancellationToken);

        foreach (var receivable in receivables)
        {
            var open = receivable.ExpectedAmount - (receivable.Settled ?? 0m);
            if (open <= 0m) continue;

            // Nobody settles a split bill the same evening, and nobody chases it
            // forever either; a fortnight is the assumption, and it is the same
            // fortnight the weekly digest calls "late". Already past it, the
            // money is assumed to arrive today rather than in the past — the
            // identical clamp the overdue pending occurrences above get.
            var date = receivable.Date.AddDays(SettlementLagDays);
            if (date < today) date = today;
            if (date > end) continue;

            flows.Add((date, PlannedKind.Income, open, receivable.Code));
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
