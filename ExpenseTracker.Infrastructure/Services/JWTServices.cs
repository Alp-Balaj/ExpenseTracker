using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ExpenseTracker.Infrastructure.Services
{
    public static class JWTServices
    {
        public static IServiceCollection AddAuthServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddIdentity<User, IdentityRole>(options =>
                {
                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            var jwtKey = configuration["Jwt:Key"];
            var jwtIssuer = configuration["Jwt:Issuer"];
            var jwtAudience = configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:Key is missing or shorter than 32 bytes. Set it via `dotnet user-secrets set \"Jwt:Key\" \"...\"` " +
                    "(development) or the Jwt__Key environment variable (docker/production). " +
                    "Generate one with `openssl rand -base64 48`.");
            }

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // /api/mcp, and only /api/mcp, authenticates with an MCP token. Selecting
                // by path rather than by token shape is the security boundary: a token
                // presented anywhere else still reaches this handler and is rejected, so
                // a token handed to an AI client can never touch the REST API. Done here
                // so UseAuthentication populates the user before the rate limiter
                // partitions on it.
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments(McpAuthDefaults.Path) ? McpAuthDefaults.Scheme : null;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
            })
            .AddScheme<AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(McpAuthDefaults.Scheme, null);

            return services;
        }
    }
}