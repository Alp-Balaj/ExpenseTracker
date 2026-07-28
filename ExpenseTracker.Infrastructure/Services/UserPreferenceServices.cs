using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services;

public class UserPreferencesService : IUserPreferencesService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserServices _user;

    public UserPreferencesService(ApplicationDbContext context, ICurrentUserServices user)
    {
        _context = context;
        _user = user;
    }

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        var prefs = await _context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == _user.UserId, cancellationToken);

        // A user who has never saved preferences has no row: hand back the
        // defaults rather than writing one on a read (the Mongo version
        // inserted here, which raced with itself on concurrent GETs).
        return prefs ?? new UserPreferences { UserId = _user.UserId };
    }

    public async Task UpdateAsync(UserPreferences updated, CancellationToken cancellationToken = default)
    {
        var prefs = await _context.UserPreferences
            .FirstOrDefaultAsync(x => x.UserId == _user.UserId, cancellationToken);

        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = _user.UserId };
            _context.UserPreferences.Add(prefs);
        }

        prefs.Theme = updated.Theme;
        prefs.BaseCurrency = updated.BaseCurrency;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
