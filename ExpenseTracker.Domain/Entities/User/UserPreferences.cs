using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseTracker.Domain.Entities.User;
public class UserPreferences
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Theme { get; set; } = "system";
    public string BaseCurrency { get; set; } = "EUR";
    //public string Language { get; set; } = "en";
    //public Dictionary<string, bool> FeatureFlags { get; set; } = new();
}