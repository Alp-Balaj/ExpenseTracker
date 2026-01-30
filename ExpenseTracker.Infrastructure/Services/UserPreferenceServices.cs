using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using MongoDB.Driver;

namespace ExpenseTracker.Infrastructure.Services;

public class UserPreferencesService : IUserPreferencesService
{
    private readonly IMongoCollection<UserPreferences> _collection;
    private readonly ICurrentUserServices _user;

    public UserPreferencesService(MongoDbContext context, ICurrentUserServices user)
    {
        _collection = context.UserPreferences;
        _user = user;
    }

    public async Task<UserPreferences> GetAsync()
    {
        var prefs = await _collection
            .Find(x => x.UserId == _user.UserId)
            .FirstOrDefaultAsync();

        if (prefs == null)
        {
            prefs = new UserPreferences { UserId = _user.UserId };
            await _collection.InsertOneAsync(prefs);
        }

        return prefs;
    }

    public async Task UpdateAsync(UserPreferences updated)
    {
        try
        {
            var update = Builders<UserPreferences>.Update
            .SetOnInsert(x => x.UserId, _user.UserId)
            .Set(x => x.Theme, updated.Theme)
            .Set(x => x.BaseCurrency, updated.BaseCurrency);

            await _collection.UpdateOneAsync(
                x => x.UserId == _user.UserId,
                update,
                new UpdateOptions { IsUpsert = true }
            );
        }
        catch (Exception)
        {
            throw;
        }
    }
}
