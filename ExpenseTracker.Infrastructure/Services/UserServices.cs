using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Infrastructure.Services
{
    public class CurrentUserServices : ICurrentUserServices
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserServices(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string UserId =>
            _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        public string UserName =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name)
        ?? "Unknown";

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    }
}
