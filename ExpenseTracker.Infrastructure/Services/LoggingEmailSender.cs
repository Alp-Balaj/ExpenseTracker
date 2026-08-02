using ExpenseTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <summary>
/// The default <see cref="IEmailSender"/> until a real provider is chosen
/// (decision D1 is open): sends nothing, logs everything. The full body is
/// logged on purpose — in development the reset/confirmation link is fished
/// out of the console, which is the whole "inbox" this sender provides.
/// Swapped out via the <c>Email:Provider</c> config switch in DI.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email (not sent — logging provider) to {To}: {Subject}\n{Body}",
            message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}
