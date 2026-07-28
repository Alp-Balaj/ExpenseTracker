using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.API.Extensions
{
    public static class CorsExtensions
    {
        public static IServiceCollection AddReactCors(this IServiceCollection services, IConfiguration config)
        {
            var origins = new List<string>
            {
                "https://localhost:6969"
            };

            var frontendOrigin = config["FRONTEND_ORIGIN"];
            if (!string.IsNullOrWhiteSpace(frontendOrigin))
            {
                origins.Add(frontendOrigin);
            }

            services.AddCors(options =>
            {
                options.AddPolicy("ReactCors", policy =>
                {
                    policy
                        .WithOrigins(origins.ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        // Browsers hide every response header from JS except a
                        // short safelist, so the paging total has to be named
                        // explicitly or the SPA can never read it.
                        .WithExposedHeaders(PageQuery.TotalCountHeader)
                        .AllowCredentials();
                });
            });

            return services;
        }
    }
}
