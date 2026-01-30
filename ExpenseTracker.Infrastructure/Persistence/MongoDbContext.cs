using ExpenseTracker.Domain.Entities.User;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace ExpenseTracker.Infrastructure.Persistence;

public class MongoDbContext
{
    public IMongoCollection<UserPreferences> UserPreferences { get; }

    public MongoDbContext(IConfiguration config)
    {
        var client = new MongoClient(
            MongoClientSettings.FromConnectionString(
                config.GetConnectionString("MongoDbConnection")
            )
        );

        var database = client.GetDatabase("ExpenseTracker");
        UserPreferences = database.GetCollection<UserPreferences>("UserPreferences");
    }
}
