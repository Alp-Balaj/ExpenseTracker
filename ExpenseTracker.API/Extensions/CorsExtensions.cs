namespace ExpenseTracker.API.Extensions
{
    public static class CorsExtensions
    {
        public static IServiceCollection AddReactCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("ReactCors", policy =>
                {
                    policy.WithOrigins("https://localhost:6969")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            return services;
        }
    }
}
