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
                        .AllowCredentials();
                });
            });

            return services;
        }
    }
}
