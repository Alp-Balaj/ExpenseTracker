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

        public void Add<T>(T entity) where T : PotEntity => _context.Set<T>().Add(entity);

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
