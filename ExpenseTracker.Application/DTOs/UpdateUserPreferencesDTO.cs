using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Application.DTOs;
public class UpdateUserPreferencesDTO
{
    [Required]
    public string Theme { get; set; } = "system";

    [Required, StringLength(3, MinimumLength = 3)]
    public string BaseCurrency { get; set; } = "EUR";
}
