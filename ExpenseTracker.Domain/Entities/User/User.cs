using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Domain.Entities.User
{
    public class User : IdentityUser
    {
        [SetsRequiredMembers]
        public User() : base()
        {
            FirstName = null!;
            LastName = null!;
        }
        public required string FirstName { get; set; } 
        public required string LastName { get; set; }
    }
}
