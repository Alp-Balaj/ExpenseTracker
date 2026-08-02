using System.Collections.Concurrent;
using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Tests.Infrastructure;

/// <summary>
/// Stands in for <c>WebPushSender</c> the same way <see cref="CapturingEmailSender"/>
/// stands in for outbound email: registered as a singleton by
/// <see cref="CustomWebApplicationFactory"/>, it records every message instead of
/// POSTing it to a push service.
/// <para>
/// The encryption itself is not stubbed away — <c>WebPushEncryptorTests</c> pins
/// it against the RFC 8291 vector. What this removes is the network, so the job
/// and dedupe tests can assert on <em>who was told what</em> without anything
/// leaving the machine.
/// </para>
/// </summary>
public sealed class CapturingPushSender : IPushSender
{
    public sealed record Sent(PushTarget Target, PushMessage Message);

    public ConcurrentQueue<Sent> Messages { get; } = new();

    /// <summary>
    /// What the next send should pretend happened. Set to
    /// <see cref="PushSendOutcome.Gone"/> to model an expired subscription and
    /// prove the row is pruned.
    /// </summary>
    public PushSendOutcome NextOutcome { get; set; } = PushSendOutcome.Delivered;

    /// <summary>Mirrors a configured deployment; flip to false to test the unavailable path.</summary>
    public bool IsConfigured { get; set; } = true;

    public string? PublicKey => IsConfigured ? "BExample-test-only-application-server-key" : null;

    public Task<PushSendResult> SendAsync(
        PushTarget target, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return Task.FromResult(new PushSendResult(PushSendOutcome.NotConfigured, 0, null));

        Messages.Enqueue(new Sent(target, message));

        var status = NextOutcome switch
        {
            PushSendOutcome.Delivered => 201,
            PushSendOutcome.Gone => 410,
            _ => 500
        };

        return Task.FromResult(new PushSendResult(NextOutcome, status, null));
    }

    /// <summary>Every message sent to a particular endpoint, oldest first.</summary>
    public IReadOnlyList<PushMessage> To(string endpoint) =>
        Messages.Where(m => m.Target.Endpoint == endpoint).Select(m => m.Message).ToList();

    public void Reset()
    {
        Messages.Clear();
        NextOutcome = PushSendOutcome.Delivered;
        IsConfigured = true;
    }
}
