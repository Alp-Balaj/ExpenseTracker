using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Application.Services;

/// <inheritdoc cref="IPotLedgerService"/>
public sealed class PotLedgerService : IPotLedgerService
{
    /// <summary>How much of a pot's money feed one read returns.</summary>
    private const int FeedPageSize = 200;

    private readonly IPotRepository _pots;
    private readonly IAccountServices _accounts;
    private readonly ISharedCategoryService _sharedCategories;

    public PotLedgerService(
        IPotRepository pots,
        IAccountServices accounts,
        ISharedCategoryService sharedCategories)
    {
        _pots = pots;
        _accounts = accounts;
        _sharedCategories = sharedCategories;
    }

    // ── Reading ────────────────────────────────────────────────────────────────

    public async Task<PotLedgerDTO?> GetLedgerAsync(Guid potId, CancellationToken cancellationToken = default)
    {
        var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
        if (pot is null) return null;

        var me = await _pots.FindMyMembershipAsync(potId, cancellationToken);
        if (me is null) return null;

        return await BuildLedgerAsync(pot, me.Id, cancellationToken);
    }

    /// <summary>
    /// Everything the money screen shows, summed from the rows.
    /// <para>
    /// Loaded whole rather than aggregated in SQL, and that is a size judgement
    /// rather than an oversight: a pot is four people and a few hundred entries,
    /// the feed is being returned in full anyway, and the positions have to be
    /// derived from exactly the rows the feed shows or the two could disagree.
    /// Five grouped aggregates would be faster and would let the balance and the
    /// feed be computed from different snapshots — which is the one failure mode
    /// this wave cannot tolerate. If a pot ever grows past a page of history,
    /// the aggregate belongs in SQL and the feed gains paging; both together.
    /// </para>
    /// </summary>
    private async Task<PotLedgerDTO> BuildLedgerAsync(
        Pot pot, Guid myMembershipId, CancellationToken cancellationToken)
    {
        var members = await _pots.MembershipsIn(pot.Id)
                                 .OrderBy(m => m.CreatedAt)
                                 .ThenBy(m => m.Id)
                                 .ToListAsync(cancellationToken);

        var topUps = await _pots.TopUpsIn(pot.Id).ToListAsync(cancellationToken);
        var expenses = await _pots.ExpensesIn(pot.Id).ToListAsync(cancellationToken);
        var shares = await _pots.SharesIn(pot.Id).ToListAsync(cancellationToken);
        var withdrawals = await _pots.WithdrawalsIn(pot.Id).ToListAsync(cancellationToken);

        var nameOf = members.ToDictionary(m => m.Id, m => m.DisplayName);
        var sharesByExpense = shares.ToLookup(s => s.PotExpenseId);

        // The pot's cash. Out-of-pocket expenses are absent on purpose: the
        // member paid, not the pot, so no pot cash moved — which is exactly why
        // the payer's outOfPocket term and everybody's shares cancel in Σ Bᵢ.
        var balance =
            topUps.Sum(t => t.Amount)
            - expenses.Where(e => !e.IsOutOfPocket).Sum(e => e.Amount)
            - withdrawals.Sum(w => w.Amount);

        var positions = members
            .Select(m =>
            {
                var toppedUp = topUps.Where(t => t.MembershipId == m.Id).Sum(t => t.Amount);
                var outOfPocket = expenses.Where(e => e.PaidByMembershipId == m.Id).Sum(e => e.Amount);
                var consumed = shares.Where(s => s.MembershipId == m.Id).Sum(s => s.Amount);
                var withdrawn = withdrawals.Where(w => w.MembershipId == m.Id).Sum(w => w.Amount);

                var position = toppedUp + outOfPocket - consumed - withdrawn;

                return new PotPositionDTO
                {
                    MembershipId = m.Id,
                    DisplayName = m.DisplayName,
                    IsYou = m.Id == myMembershipId,
                    IsActive = m.IsActive,
                    ToppedUp = toppedUp,
                    OutOfPocket = outOfPocket,
                    Shares = consumed,
                    Withdrawn = withdrawn,
                    Position = position,
                    // Your own stake, but never more cash than is actually in
                    // the pot: a member whose position is positive only because
                    // somebody else has overspent cannot withdraw money that is
                    // not there. Both halves of the cap are enforced again on
                    // the write path — this one exists so the form agrees with it.
                    Withdrawable = Math.Max(0m, Math.Min(position, balance)),
                };
            })
            // Members who have left keep their row — their history is part of the
            // sum — but they sort after the people still in the pot.
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => members.First(m => m.Id == p.MembershipId).CreatedAt)
            .ToList();

        var entries = new List<PotLedgerEntryDTO>(topUps.Count + expenses.Count + withdrawals.Count);

        entries.AddRange(topUps.Select(t => new PotLedgerEntryDTO
        {
            Id = t.Id,
            Kind = PotLedgerEntryKind.TopUp,
            Date = t.Date,
            CreatedAt = t.CreatedAt,
            Amount = t.Amount,
            Title = t.Note,
            ActorMembershipId = t.MembershipId,
            ActorDisplayName = nameOf.GetValueOrDefault(t.MembershipId),
        }));

        entries.AddRange(expenses.Select(e => new PotLedgerEntryDTO
        {
            Id = e.Id,
            Kind = PotLedgerEntryKind.Expense,
            Date = e.Date,
            CreatedAt = e.CreatedAt,
            Amount = e.Amount,
            Title = e.Title,
            Description = e.Description,
            ActorMembershipId = e.CreatedByMembershipId,
            ActorDisplayName = nameOf.GetValueOrDefault(e.CreatedByMembershipId),
            PaidByMembershipId = e.PaidByMembershipId,
            PaidByDisplayName = e.PaidByMembershipId is Guid paidBy
                ? nameOf.GetValueOrDefault(paidBy)
                : null,
            Shares = sharesByExpense[e.Id]
                .Select(s => new PotShareDTO
                {
                    MembershipId = s.MembershipId,
                    DisplayName = nameOf.GetValueOrDefault(s.MembershipId) ?? string.Empty,
                    IsYou = s.MembershipId == myMembershipId,
                    Amount = s.Amount,
                })
                .OrderByDescending(s => s.Amount)
                .ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            CanDelete = CanDelete(e, myMembershipId),
        }));

        entries.AddRange(withdrawals.Select(w => new PotLedgerEntryDTO
        {
            Id = w.Id,
            Kind = PotLedgerEntryKind.Withdrawal,
            Date = w.Date,
            CreatedAt = w.CreatedAt,
            Amount = w.Amount,
            Title = w.Note,
            ActorMembershipId = w.MembershipId,
            ActorDisplayName = nameOf.GetValueOrDefault(w.MembershipId),
        }));

        return new PotLedgerDTO
        {
            PotId = pot.Id,
            CurrencyCode = pot.CurrencyCode,
            Balance = balance,
            IsLow = pot.LowBalanceThreshold > 0m && balance < pot.LowBalanceThreshold,
            Positions = positions,
            Entries = entries
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Take(FeedPageSize)
                .ToList(),
        };
    }

    /// <summary>
    /// Flat trust says any member may undo any group action, and for an expense
    /// the pot paid for that is exactly what happens. An out-of-pocket one is
    /// different: reversing it has to put money back into the payer's own
    /// account, and no user may write into an account they do not own. So the
    /// person who fronted it is the person who can unfront it. The genuine
    /// cross-user write is W3's, deliberately, and it does not start here.
    /// </summary>
    private static bool CanDelete(PotExpense expense, Guid myMembershipId) =>
        expense.PaidByMembershipId is null || expense.PaidByMembershipId == myMembershipId;

    // ── Writes ─────────────────────────────────────────────────────────────────

    public async Task<(PotLedgerDTO? Ledger, string? Error)> TopUpAsync(
        Guid potId, CreatePotTopUpDTO dto, CancellationToken cancellationToken = default)
    {
        var context = await OpenAsync(potId, cancellationToken);
        if (context is null) return (null, null);

        var (pot, me) = context.Value;

        var amount = Round(dto.Amount);
        if (amount <= 0m) return (null, "A top-up has to be more than zero.");

        await using var transaction = await _pots.BeginTransactionAsync(cancellationToken);

        // The account move first, because it is the one step that can fail on a
        // reference the caller does not own: AdjustAccountBalance is scoped to
        // the current user, so an account id belonging to somebody else simply
        // is not found. That check is why SourceAccountId needs no FK.
        var debit = await _accounts.AdjustAccountBalance(-amount, dto.SourceAccountId, cancellationToken);
        if (!debit.Success) return (null, debit.ErrorMessage);

        var topUp = new PotTopUp
        {
            PotId = pot.Id,
            MembershipId = me.Id,
            Amount = amount,
            Date = dto.Date ?? Today,
            Note = Trimmed(dto.Note),
            SourceAccountId = dto.SourceAccountId,
        };

        _pots.Add(topUp);
        _pots.Add(Entry(pot, me, PotActivityKind.ToppedUp, topUp.Id, Money(amount, pot.CurrencyCode)));

        await _pots.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await BuildLedgerAsync(pot, me.Id, cancellationToken), null);
    }

    public async Task<(PotLedgerDTO? Ledger, string? Error)> LogExpenseAsync(
        Guid potId, CreatePotExpenseDTO dto, CancellationToken cancellationToken = default)
    {
        var context = await OpenAsync(potId, cancellationToken);
        if (context is null) return (null, null);

        var (pot, me) = context.Value;

        var amount = Round(dto.Amount);
        if (amount <= 0m) return (null, "An expense has to be more than zero.");

        // Everyone who could be attributed to, in the tie-break order the
        // execution plan fixes: PotMembership.CreatedAt, then membership id.
        // Ordered here rather than trusting the order the client sent, so the
        // extra cent lands in the same place whatever the form did.
        var members = await _pots.MembershipsIn(pot.Id)
                                 .Where(m => m.LeftAt == null)
                                 .OrderBy(m => m.CreatedAt)
                                 .ThenBy(m => m.Id)
                                 .ToListAsync(cancellationToken);

        var (participants, shareError) = ResolveParticipants(dto.Shares, members);
        if (shareError is not null) return (null, shareError);

        var (shareCents, allocationError) = ShareAllocator.Allocate(
            ShareAllocator.ToCents(amount),
            participants!.Select(p => p.FixedCents).ToList());

        if (allocationError is not null) return (null, allocationError);

        // Every participant's own "Shared" category, so each share can name
        // where it lands. Ordinarily this creates nothing — joining a pot
        // already made the row — and it adds without saving, so the categories
        // and the shares are flushed by the one SaveAsync below.
        var categories = await _sharedCategories.EnsureForMembershipsAsync(
            participants!.Select(p => p.MembershipId).ToList(), cancellationToken);

        await using var transaction = await _pots.BeginTransactionAsync(cancellationToken);

        // Out of pocket means the caller's own account really paid, so it is
        // debited exactly as a personal expense would debit it. The pot's cash
        // does not move; the payer's contribution and everyone's consumption
        // cancel in the sum.
        if (dto.PaidFromAccountId is Guid accountId)
        {
            var debit = await _accounts.AdjustAccountBalance(-amount, accountId, cancellationToken);
            if (!debit.Success) return (null, debit.ErrorMessage);
        }

        var expense = new PotExpense
        {
            PotId = pot.Id,
            Title = dto.Title.Trim(),
            Description = Trimmed(dto.Description),
            Amount = amount,
            Date = dto.Date ?? Today,
            CreatedByMembershipId = me.Id,
            PaidByMembershipId = dto.PaidFromAccountId is null ? null : me.Id,
            PaidFromAccountId = dto.PaidFromAccountId,
        };

        _pots.Add(expense);

        for (var i = 0; i < participants!.Count; i++)
            _pots.Add(new PotExpenseShare
            {
                PotId = pot.Id,
                PotExpense = expense,
                MembershipId = participants[i].MembershipId,
                Amount = ShareAllocator.FromCents(shareCents![i]),
                CategoryId = categories.GetValueOrDefault(participants[i].MembershipId),
            });

        _pots.Add(Entry(
            pot, me, PotActivityKind.ExpenseLogged, expense.Id, expense.Title,
            after: Money(amount, pot.CurrencyCode)));

        await _pots.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await BuildLedgerAsync(pot, me.Id, cancellationToken), null);
    }

    public async Task<(PotLedgerDTO? Ledger, string? Error)> DeleteExpenseAsync(
        Guid potId, Guid expenseId, CancellationToken cancellationToken = default)
    {
        var context = await OpenAsync(potId, cancellationToken);
        if (context is null) return (null, null);

        var (pot, me) = context.Value;

        var expense = await _pots.FindExpenseWithSharesAsync(potId, expenseId, cancellationToken);
        if (expense is null) return (null, null);

        if (!CanDelete(expense, me.Id))
            return (null, "Only the member who paid for this out of their own pocket can remove it.");

        await using var transaction = await _pots.BeginTransactionAsync(cancellationToken);

        // The contract's fifth sentence, in one transaction: whatever this
        // expense caused, it stops having caused. For a pot-paid expense that is
        // the pot's debit — which reverses by the rows going away, since the
        // balance is summed and not stored. For an out-of-pocket one there is
        // also real cash to put back.
        if (expense.PaidFromAccountId is Guid accountId)
        {
            var credit = await _accounts.AdjustAccountBalance(expense.Amount, accountId, cancellationToken);
            if (!credit.Success) return (null, credit.ErrorMessage);
        }

        // Explicit rather than left to the cascade. The cascade is the backstop;
        // saying it here is what makes "the debit and every share, together" a
        // property of this method rather than of the schema, and what lets the
        // activity entry be written in the same transaction.
        _pots.RemoveRange(expense.Shares);
        _pots.Remove(expense);

        _pots.Add(Entry(
            pot, me, PotActivityKind.ExpenseDeleted, expense.Id, expense.Title,
            before: Money(expense.Amount, pot.CurrencyCode)));

        await _pots.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await BuildLedgerAsync(pot, me.Id, cancellationToken), null);
    }

    public async Task<(PotLedgerDTO? Ledger, string? Error)> WithdrawAsync(
        Guid potId, CreatePotWithdrawalDTO dto, CancellationToken cancellationToken = default)
    {
        var context = await OpenAsync(potId, cancellationToken);
        if (context is null) return (null, null);

        var (pot, me) = context.Value;

        var amount = Round(dto.Amount);
        if (amount <= 0m) return (null, "A withdrawal has to be more than zero.");

        // Read the cap off the same arithmetic the screen shows, so the refusal
        // and the number the member was looking at cannot disagree.
        var ledger = await BuildLedgerAsync(pot, me.Id, cancellationToken);
        var mine = ledger.Positions.First(p => p.MembershipId == me.Id);

        if (mine.Withdrawable <= 0m)
            return (null, "You have nothing in this pot to take out.");

        if (amount > mine.Withdrawable)
            return (null,
                $"You can take out at most {Money(mine.Withdrawable, pot.CurrencyCode)} — that is your share of what is in the pot.");

        await using var transaction = await _pots.BeginTransactionAsync(cancellationToken);

        var credit = await _accounts.AdjustAccountBalance(amount, dto.DestinationAccountId, cancellationToken);
        if (!credit.Success) return (null, credit.ErrorMessage);

        var withdrawal = new PotWithdrawal
        {
            PotId = pot.Id,
            MembershipId = me.Id,
            Amount = amount,
            Date = dto.Date ?? Today,
            Note = Trimmed(dto.Note),
            DestinationAccountId = dto.DestinationAccountId,
        };

        _pots.Add(withdrawal);
        _pots.Add(Entry(pot, me, PotActivityKind.Withdrew, withdrawal.Id, Money(amount, pot.CurrencyCode)));

        await _pots.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await BuildLedgerAsync(pot, me.Id, cancellationToken), null);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>One participant, resolved to a membership in this pot and an optional fixed slice.</summary>
    private readonly record struct Participant(Guid MembershipId, long? FixedCents);

    /// <summary>
    /// Turns what the form sent into participants in tie-break order.
    /// <para>
    /// Every membership id has to be a live member of <em>this</em> pot: an id
    /// from another pot, or one that never existed, is refused rather than
    /// silently dropped, because a share that quietly disappeared would still
    /// sum correctly and would attribute somebody's dinner to the wrong person.
    /// </para>
    /// </summary>
    private static (List<Participant>? Participants, string? Error) ResolveParticipants(
        IReadOnlyList<PotExpenseShareInputDTO> input, IReadOnlyList<PotMembership> members)
    {
        if (input.Count == 0)
            return (null, "Say who this expense was for.");

        var byId = members.ToDictionary(m => m.Id);
        var seen = new HashSet<Guid>();

        foreach (var share in input)
        {
            if (!byId.ContainsKey(share.MembershipId))
                return (null, "One of the people you picked is not in this pot.");

            if (!seen.Add(share.MembershipId))
                return (null, "Somebody is listed twice.");
        }

        var fixedBy = input.ToDictionary(
            s => s.MembershipId,
            s => s.Amount is decimal a ? ShareAllocator.ToCents(a) : (long?)null);

        // Ordered by the membership list, which arrived sorted by CreatedAt then
        // id — so the allocator's "leftover cents to the earliest slots" means
        // "to the earliest-joined member", whatever order the client sent.
        return (members.Where(m => seen.Contains(m.Id))
                       .Select(m => new Participant(m.Id, fixedBy[m.Id]))
                       .ToList(),
                null);
    }

    /// <summary>
    /// Loads the pot and the caller's membership, or null when they are not a
    /// member — which the controller answers 404, indistinguishably from a pot
    /// that never existed.
    /// </summary>
    private async Task<(Pot Pot, PotMembership Me)?> OpenAsync(Guid potId, CancellationToken cancellationToken)
    {
        var pot = await _pots.FindMemberPotAsync(potId, cancellationToken);
        if (pot is null) return null;

        var me = await _pots.FindMyMembershipAsync(potId, cancellationToken);
        if (me is null) return null;

        return (pot, me);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Always in the pot's currency, because group screens never convert.</summary>
    private static string Money(decimal amount, string currencyCode) =>
        $"{amount:0.00} {currencyCode}";

    private static PotActivity Entry(
        Pot pot,
        PotMembership actor,
        PotActivityKind kind,
        Guid targetId,
        string targetLabel,
        string? before = null,
        string? after = null) => new()
        {
            PotId = pot.Id,
            ActorMembershipId = actor.Id,
            Kind = kind,
            TargetId = targetId,
            TargetLabel = targetLabel,
            Before = before,
            After = after,
        };
}
