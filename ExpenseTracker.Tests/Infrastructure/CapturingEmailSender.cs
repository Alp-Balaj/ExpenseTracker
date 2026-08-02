using System.Collections.Concurrent;
using ExpenseTracker.Application.Interfaces;
using Xunit;

namespace ExpenseTracker.Tests.Infrastructure;

/// <summary>
/// Stands in for the logging email sender the same way StubExchangeRateProvider
/// stands in for frankfurter.app: registered as a singleton by
/// <see cref="CustomWebApplicationFactory"/>, it keeps every "sent" message in
/// memory so a test can fish the reset/confirmation token back out of the link.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Latest message addressed to <paramref name="email"/>, or null.</summary>
    public EmailMessage? LastTo(string email) =>
        Sent.LastOrDefault(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pulls the <c>token</c> query parameter out of the first link in a message
    /// body — the same value the SPA would read off its /reset-password or
    /// /confirm-email route.
    /// </summary>
    public static string ExtractToken(EmailMessage message)
    {
        var start = message.Body.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0, "email body contains no link");

        var end = message.Body.IndexOfAny(new[] { '\n', ' ' }, start);
        var url = message.Body[start..(end < 0 ? message.Body.Length : end)];

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        var token = query["token"];
        Assert.False(string.IsNullOrEmpty(token), $"link carries no token: {url}");
        return token!;
    }
}
