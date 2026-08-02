namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>One outbound email. Body is plain text (links included verbatim).</summary>
    public record EmailMessage(string To, string Subject, string Body);

    /// <summary>
    /// Outbound email port. The default registration is a logging no-op sender
    /// (<c>Email:Provider</c> unset or "Logging"); a real provider gets wired in
    /// Infrastructure's DI switch behind the same config key later — callers never
    /// know or care which one is active.
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
