using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.API.Middleware
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            // UnauthorizedAccessException (repository ownership guards) maps to 404, not 403:
            // a 403 would confirm the row exists for another user. DbUpdateException detail
            // stays generic so SQL/constraint names never reach the client — the log line
            // below carries the specifics.
            var (status, title, detail) = exception switch
            {
                ArgumentException => (
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    exception.Message),
                UnauthorizedAccessException => (
                    StatusCodes.Status404NotFound,
                    "Not Found",
                    "The requested resource was not found."),
                DbUpdateException => (
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "The operation conflicts with existing data."),
                _ => (
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    "An unexpected error occurred.")
            };

            _logger.LogError(
                exception,
                "Unhandled exception mapped to {StatusCode} for {Method} {Path}",
                status,
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Response.StatusCode = status;
            await httpContext.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = detail
                },
                cancellationToken);

            return true;
        }
    }
}
