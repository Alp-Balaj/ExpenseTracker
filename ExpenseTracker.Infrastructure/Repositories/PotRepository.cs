using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseTracker.Infrastructure.Repositories
{
    /// <summary>
    /// The membership-scoped repository. See <see cref="IPotRepository"/> for
    /// why this is a separate type from <c>UserRelatedRepository&lt;T&gt;</c>
    /// rather than a widening of it.
    /// </summary>
    public class PotRepository : IPotRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserServices _user;

        public PotRepository(ApplicationDbContext context, ICurrentUserServices user)
        {
            _context = context;
            _user = user;
        }

        public string CurrentUserId => _user.UserId;

        /// <summary>
        /// The single predicate everything else is built on. "Currently a member"
        /// — a membership that has left stops granting access the moment it is
        /// stamped, without the row going anywhere.
        /// </summary>
        private IQueryable<Guid> MyPotIds()
        {
            var userId = CurrentUserId;
            return _context.Set<PotMembership>()
                           .Where(m => m.UserId == userId && m.LeftAt == null)
                           .Select(m => m.PotId);
        }

        // Every method below hoists MyPotIds() into a local before using it in a
        // predicate. Left as a call inside the lambda it would still translate —
        // EF lifts a subexpression that does not mention the parameter — but as a
        // local it is unmistakably one captured subquery, and nobody has to
        // wonder whether the scoping ran per row.

        public IQueryable<Pot> MemberPots()
        {
            var myPots = MyPotIds();
            return _context.Set<Pot>()
                           .AsNoTracking()
                           .Where(p => myPots.Contains(p.Id));
        }

        public async Task<Pot?> FindMemberPotAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var myPots = MyPotIds();
            return await _context.Set<Pot>()
                                 .Where(p => p.Id == potId && myPots.Contains(p.Id))
                                 .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<PotMembership?> FindMyMembershipAsync(Guid potId, CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            return await _context.Set<PotMembership>()
                                 .FirstOrDefaultAsync(
                                     m => m.PotId == potId && m.UserId == userId && m.LeftAt == null,
                                     cancellationToken);
        }

        public async Task<PotMembership?> FindMembershipForUserAsync(Guid potId, string userId, CancellationToken cancellationToken = default) =>
            await _context.Set<PotMembership>()
                          .FirstOrDefaultAsync(m => m.PotId == potId && m.UserId == userId, cancellationToken);

        public IQueryable<PotMembership> MembershipsIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotMembership>()
                           .AsNoTracking()
                           .Where(m => m.PotId == potId && myPots.Contains(m.PotId));
        }

        public IQueryable<PotInvite> InvitesIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotInvite>()
                           .AsNoTracking()
                           .Where(i => i.PotId == potId && myPots.Contains(i.PotId));
        }

        public IQueryable<PotActivity> ActivityIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotActivity>()
                           .AsNoTracking()
                           .Where(a => a.PotId == potId && myPots.Contains(a.PotId));
        }

        public async Task<T?> FindInPotAsync<T>(Guid potId, Guid id, CancellationToken cancellationToken = default)
            where T : PotEntity, IPotScoped
        {
            var myPots = MyPotIds();
            return await _context.Set<T>()
                                 .Where(e => e.Id == id && e.PotId == potId && myPots.Contains(e.PotId))
                                 .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<PotInvite?> FindInviteByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            await _context.Set<PotInvite>()
                          .Include(i => i.Pot)
                          // The issuer, so the join screen can say who invited
                          // you. That label is the only thing about the pot's
                          // members a code discloses.
                          .Include(i => i.IssuedByMembership)
                          .FirstOrDefaultAsync(i => i.Code == code, cancellationToken);

        public async Task<IReadOnlyList<PotMembership>> MembershipsOfUserAsync(string userId, CancellationToken cancellationToken = default) =>
            await _context.Set<PotMembership>()
                          .Where(m => m.UserId == userId)
                          .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<Pot>> PotsCreatedByAsync(string userId, CancellationToken cancellationToken = default) =>
            await _context.Set<Pot>()
                          .Where(p => p.CreatedByUserId == userId)
                          .ToListAsync(cancellationToken);

        // ── W2, the ledger ─────────────────────────────────────────────────────
        //
        // Same scoping as everything above: one captured "pots I am in" subquery,
        // hoisted into a local so it is visibly one predicate and not a per-row
        // check. A non-member gets an empty sequence rather than an exception,
        // which is what lets the service turn "no rows" and "no such pot" into
        // the same 404.

        public IQueryable<PotTopUp> TopUpsIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotTopUp>()
                           .AsNoTracking()
                           .Where(t => t.PotId == potId && myPots.Contains(t.PotId));
        }

        public IQueryable<PotExpense> ExpensesIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotExpense>()
                           .AsNoTracking()
                           .Where(e => e.PotId == potId && myPots.Contains(e.PotId));
        }

        public IQueryable<PotExpenseShare> SharesIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotExpenseShare>()
                           .AsNoTracking()
                           .Where(s => s.PotId == potId && myPots.Contains(s.PotId));
        }

        public IQueryable<PotWithdrawal> WithdrawalsIn(Guid potId)
        {
            var myPots = MyPotIds();
            return _context.Set<PotWithdrawal>()
                           .AsNoTracking()
                           .Where(w => w.PotId == potId && myPots.Contains(w.PotId));
        }

        public async Task<PotExpense?> FindExpenseWithSharesAsync(
            Guid potId, Guid expenseId, CancellationToken cancellationToken = default)
        {
            var myPots = MyPotIds();
            return await _context.Set<PotExpense>()
                                 .Include(e => e.Shares)
                                 .Where(e => e.Id == expenseId && e.PotId == potId && myPots.Contains(e.PotId))
                                 .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// See <see cref="IPotRepository.SharesOfCurrentUser"/> for why this one
        /// filters on the user rather than on membership. It cannot return
        /// anybody else's row under any scoping, which is what makes the
        /// exception safe as well as necessary.
        /// </summary>
        public IQueryable<PotExpenseShare> SharesOfCurrentUser()
        {
            var userId = CurrentUserId;
            return _context.Set<PotExpenseShare>()
                           .AsNoTracking()
                           .Where(s => s.Membership.UserId == userId);
        }

        public async Task<IReadOnlyDictionary<Guid, PotMoney>> MoneyForMemberPotsAsync(
            CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            var myPots = MyPotIds();

            // Four grouped aggregates, whatever the number of pots or rows. Each
            // sums the pot's side and the reader's side in the same pass — the
            // reader's side keyed on the *user* behind the membership, because
            // "my position" survives me leaving and rejoining on the same row.
            var topUps = await _context.Set<PotTopUp>()
                .Where(t => myPots.Contains(t.PotId))
                .GroupBy(t => t.PotId)
                .Select(g => new
                {
                    PotId = g.Key,
                    All = g.Sum(t => t.Amount),
                    Mine = g.Sum(t => t.Membership.UserId == userId ? t.Amount : 0m),
                })
                .ToDictionaryAsync(x => x.PotId, cancellationToken);

            var expenses = await _context.Set<PotExpense>()
                .Where(e => myPots.Contains(e.PotId))
                .GroupBy(e => e.PotId)
                .Select(g => new
                {
                    PotId = g.Key,
                    // Only what the pot itself paid moves the pot's cash. An
                    // out-of-pocket expense never touched it.
                    PotPaid = g.Sum(e => e.PaidByMembershipId == null ? e.Amount : 0m),
                    MineOutOfPocket = g.Sum(e =>
                        e.PaidByMembership != null && e.PaidByMembership.UserId == userId ? e.Amount : 0m),
                })
                .ToDictionaryAsync(x => x.PotId, cancellationToken);

            var shares = await _context.Set<PotExpenseShare>()
                .Where(s => myPots.Contains(s.PotId) && s.Membership.UserId == userId)
                .GroupBy(s => s.PotId)
                .Select(g => new { PotId = g.Key, Mine = g.Sum(s => s.Amount) })
                .ToDictionaryAsync(x => x.PotId, cancellationToken);

            var withdrawals = await _context.Set<PotWithdrawal>()
                .Where(w => myPots.Contains(w.PotId))
                .GroupBy(w => w.PotId)
                .Select(g => new
                {
                    PotId = g.Key,
                    All = g.Sum(w => w.Amount),
                    Mine = g.Sum(w => w.Membership.UserId == userId ? w.Amount : 0m),
                })
                .ToDictionaryAsync(x => x.PotId, cancellationToken);

            var potIds = topUps.Keys
                .Concat(expenses.Keys)
                .Concat(shares.Keys)
                .Concat(withdrawals.Keys)
                .Distinct();

            var money = new Dictionary<Guid, PotMoney>();

            foreach (var potId in potIds)
            {
                var toppedUp = topUps.TryGetValue(potId, out var t) ? t : null;
                var spent = expenses.TryGetValue(potId, out var e) ? e : null;
                var consumed = shares.TryGetValue(potId, out var s) ? s.Mine : 0m;
                var withdrawn = withdrawals.TryGetValue(potId, out var w) ? w : null;

                money[potId] = new PotMoney(
                    Balance: (toppedUp?.All ?? 0m) - (spent?.PotPaid ?? 0m) - (withdrawn?.All ?? 0m),
                    YourPosition: (toppedUp?.Mine ?? 0m)
                                  + (spent?.MineOutOfPocket ?? 0m)
                                  - consumed
                                  - (withdrawn?.Mine ?? 0m));
            }

            return money;
        }

        public Task<bool> AnySharesInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
            _context.Set<PotExpenseShare>().AnyAsync(s => s.CategoryId == categoryId, cancellationToken);

        public void Add<T>(T entity) where T : PotEntity => _context.Set<T>().Add(entity);

        public void Remove<T>(T entity) where T : PotEntity => _context.Set<T>().Remove(entity);

        public void RemoveRange<T>(IEnumerable<T> entities) where T : PotEntity =>
            _context.Set<T>().RemoveRange(entities);

        public void Update<T>(T entity) where T : PotEntity => _context.Set<T>().Update(entity);

        public Task<int> SaveAsync(CancellationToken cancellationToken = default) =>
            _context.SaveChangesAsync(cancellationToken);

        public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            new PotTransaction(await _context.Database.BeginTransactionAsync(cancellationToken));

        private sealed class PotTransaction : IUnitOfWorkTransaction
        {
            private readonly IDbContextTransaction _transaction;
            public PotTransaction(IDbContextTransaction transaction) => _transaction = transaction;
            public Task CommitAsync(CancellationToken cancellationToken = default) => _transaction.CommitAsync(cancellationToken);
            public Task RollbackAsync(CancellationToken cancellationToken = default) => _transaction.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => _transaction.DisposeAsync();
        }
    }
}
