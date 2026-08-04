using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services;

/// <summary>
/// <inheritdoc cref="ISharedCategoryService"/>
/// <para>
/// In Infrastructure and on <see cref="ApplicationDbContext"/> directly, for the
/// same reason <see cref="UserDataSeeder"/> is: <c>IUserRelatedRepository</c>
/// and <c>IUnitOfWork</c> scope every read and write to
/// <c>ICurrentUserServices.UserId</c>, and
/// <see cref="EnsureForMembershipsAsync"/> has to see the categories of members
/// who are not the caller. The single-user methods below keep the user filter
/// explicit rather than inheriting it, so the widening is visible in the one
/// method that needs it instead of being a property of the class.
/// </para>
/// </summary>
public sealed class SharedCategoryService : ISharedCategoryService
{
    /// <summary>
    /// What it is called. The name is not what identifies it —
    /// <see cref="Category.SystemKind"/> is — so a user who already happens to
    /// have a category of their own called "Shared" ends up with two rows that
    /// look alike and behave differently, only one of which refuses deletion.
    /// Renaming either is available to them. Refusing to create it, or picking
    /// "Shared (pots)", would make the common case worse to protect a case that
    /// costs a rename.
    /// </summary>
    public const string SharedCategoryName = "Shared";

    /// <summary>Distinct from all eleven of the seeder's colours, so it reads as its own thing on a chart.</summary>
    public const string SharedCategoryColor = "#5C6BC0";

    public const string SharedCategoryDescription =
        "Your share of what your pots spend. Created when you joined your first pot.";

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _user;

    public SharedCategoryService(ApplicationDbContext context, ICurrentUserServices user)
    {
        _context = context;
        _user = user;
    }

    public Task<Category?> FindForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = _user.UserId;
        return _context.Categories
                       .FirstOrDefaultAsync(
                           c => c.UserId == userId && c.SystemKind == CategorySystemKind.Shared,
                           cancellationToken);
    }

    public async Task<Category> EnsureForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (await FindForCurrentUserAsync(cancellationToken) is Category existing)
            return existing;

        // Local changes count too: creating a pot ensures the category and then
        // joining one in the same request would otherwise add a second.
        var pending = LocallyAddedFor(_user.UserId);
        if (pending is not null) return pending;

        var category = New(_user.UserId);
        _context.Categories.Add(category);
        return category;
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> EnsureForMembershipsAsync(
        IReadOnlyList<Guid> membershipIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, Guid>(membershipIds.Count);
        if (membershipIds.Count == 0) return result;

        // Membership → the user behind it. An anonymized membership has no user
        // and therefore no taxonomy to land in; it is left out rather than
        // guessed at. Its share still exists and still counts in Σ Bᵢ — the
        // group's arithmetic does not depend on anybody's categories.
        var owners = await _context.Set<PotMembership>()
            .Where(m => membershipIds.Contains(m.Id) && m.UserId != null)
            .Select(m => new { m.Id, UserId = m.UserId! })
            .ToListAsync(cancellationToken);

        if (owners.Count == 0) return result;

        var userIds = owners.Select(o => o.UserId).Distinct().ToList();

        var existing = await _context.Categories
            .Where(c => userIds.Contains(c.UserId) && c.SystemKind == CategorySystemKind.Shared)
            .Select(c => new { c.UserId, c.Id })
            .ToListAsync(cancellationToken);

        var categoryByUser = existing.ToDictionary(c => c.UserId, c => c.Id);

        foreach (var userId in userIds)
        {
            if (categoryByUser.ContainsKey(userId)) continue;

            var pending = LocallyAddedFor(userId);
            if (pending is not null)
            {
                categoryByUser[userId] = pending.Id;
                continue;
            }

            var category = New(userId);
            _context.Categories.Add(category);
            categoryByUser[userId] = category.Id;
        }

        foreach (var owner in owners)
            if (categoryByUser.TryGetValue(owner.UserId, out var categoryId))
                result[owner.Id] = categoryId;

        return result;
    }

    /// <summary>
    /// A Shared category added in this unit of work but not yet flushed. The
    /// database lookups above cannot see it, and adding a second would breach
    /// "one per user" in a way no constraint here would catch.
    /// </summary>
    private Category? LocallyAddedFor(string userId) =>
        _context.ChangeTracker.Entries<Category>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .FirstOrDefault(c => c.UserId == userId && c.SystemKind == CategorySystemKind.Shared);

    private static Category New(string userId) => new()
    {
        UserId = userId,
        Name = SharedCategoryName,
        // An Expense category, because a share is spending. It appears in the
        // expense pickers like any other and can be budgeted like any other,
        // which is the point of putting pot spending in the user's own taxonomy
        // rather than inventing a fourth axis for it.
        CategoryType = CategoryType.Expense,
        Color = SharedCategoryColor,
        SystemKind = CategorySystemKind.Shared,
        Description = SharedCategoryDescription,
    };
}
