using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Domain.Entities.User
{
    public class User : IdentityUser
    {
        public string FirstName { get; set; } 
        public string LastName { get; set; }
        public string? Preferences {  get; set; }
    }
}
