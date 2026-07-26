using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs;
public class UpdateUserPreferencesDTO
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; } = null!;
    public string? UserId { get; set; } = null!;

    [Required]
    public string Theme { get; set; } = "system";

    [Required, StringLength(3, MinimumLength = 3)]
    public string BaseCurrency { get; set; } = "EUR";
}
