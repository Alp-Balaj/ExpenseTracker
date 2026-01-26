using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseTracker.Application.DTOs;
public class UpdateUserPreferencesDTO
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; } = null!;
    public string? UserId { get; set; } = null!;
    public string Theme { get; set; } = "system";
    public string BaseCurrency { get; set; } = "EUR";
}
