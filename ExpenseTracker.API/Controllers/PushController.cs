using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// Web Push registration: the four calls the settings screen makes.
    /// <para>
    /// Nothing here decides <em>when</em> anyone is notified — that is the
    /// scheduler's three jobs (decision D3: a budget threshold crossed, a planned
    /// bill due tomorrow, the weekly digest). This controller only manages which
    /// devices are listening.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class PushController : ControllerBase
    {
        private readonly IPushSubscriptionService _subscriptions;
        private readonly IPushNotificationService _notifications;
        private readonly IPushSender _sender;
        private readonly ICurrentUserServices _currentUser;

        public PushController(
            IPushSubscriptionService subscriptions,
            IPushNotificationService notifications,
            IPushSender sender,
            ICurrentUserServices currentUser)
        {
            _subscriptions = subscriptions;
            _notifications = notifications;
            _sender = sender;
            _currentUser = currentUser;
        }

        /// <summary>
        /// The VAPID application server key, and whether this deployment can send
        /// at all. The SPA asks first and hides the whole notifications section
        /// when <c>available</c> is false — a permission prompt that leads nowhere
        /// is worse than no prompt, and the browser only ever asks once.
        /// </summary>
        [HttpGet("configuration")]
        public IActionResult GetConfiguration() => Ok(new PushConfigurationDTO
        {
            Available = _sender.IsConfigured,
            PublicKey = _sender.PublicKey
        });

        /// <summary>Registers this browser, or re-registers it after the endpoint rotated.</summary>
        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(
            [FromBody] SavePushSubscriptionDTO subscription, CancellationToken cancellationToken)
        {
            if (!_sender.IsConfigured)
            {
                // 503 rather than 400: the request is fine, the deployment isn't.
                return Problem(
                    title: "Push notifications are not configured on this server.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // The client may omit it; the header is the honest source anyway.
            subscription.UserAgent ??= Request.Headers.UserAgent.ToString();

            var device = await _subscriptions.SubscribeAsync(subscription, cancellationToken);
            return Ok(device);
        }

        /// <summary>
        /// Unregisters one endpoint. Idempotent by design: the browser may have
        /// already dropped the subscription on its side, and the SPA calls this
        /// on the way out either way.
        /// </summary>
        [HttpPost("unsubscribe")]
        public async Task<IActionResult> Unsubscribe(
            [FromBody] UnsubscribePushDTO request, CancellationToken cancellationToken)
        {
            var removed = await _subscriptions.UnsubscribeAsync(request.Endpoint, cancellationToken);
            return Ok(new { removed });
        }

        /// <summary>This account's registered devices, for the settings list.</summary>
        [HttpGet("devices")]
        public async Task<IActionResult> GetDevices(
            [FromQuery] string? endpoint, CancellationToken cancellationToken)
        {
            var devices = await _subscriptions.ListAsync(endpoint, cancellationToken);
            return Ok(devices);
        }

        /// <summary>
        /// Sends a notification to every device this account has registered.
        /// <para>
        /// Not a trigger — it is the only way to prove the round trip on a real
        /// device, where the failure modes (a VAPID key that does not match the
        /// one the browser subscribed with, an installed PWA on iOS that never
        /// got the permission) are invisible from the server. Deliberately
        /// exempt from the dedupe log, because a test that only works once is
        /// not a test.
        /// </para>
        /// </summary>
        [HttpPost("test")]
        public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
        {
            if (!_sender.IsConfigured)
            {
                return Problem(
                    title: "Push notifications are not configured on this server.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var delivered = await _notifications.SendUndeduplicatedAsync(
                _currentUser.UserId,
                new PushMessage(
                    "ExpenseTracker",
                    "Notifications are working. This is the only one you asked for.",
                    "/",
                    "test"),
                cancellationToken);

            return Ok(new { delivered });
        }
    }
}
