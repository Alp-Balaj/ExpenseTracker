using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.API.Filters
{
    /// <summary>
    /// Makes a write apply exactly once, however many times it is sent.
    /// <para>
    /// Opt-in per action, and inert without the header — every existing caller,
    /// including the SPA's normal online path, is unaffected. Only Stream O's
    /// outbox sends the key.
    /// </para>
    /// </summary>
    public sealed class IdempotentAttribute : TypeFilterAttribute
    {
        public IdempotentAttribute() : base(typeof(IdempotencyFilter))
        {
        }
    }

    /// <summary>
    /// The replay contract, in one filter.
    ///
    /// <para>
    /// An offline queue cannot tell "the server never received it" from "the
    /// server applied it and the reply was lost" — a dropped connection looks
    /// identical from the client. So it must retry, and something on the server
    /// must remember. Without that, an outbox is a machine for double-charging
    /// accounts on a bad train journey.
    /// </para>
    ///
    /// <para>
    /// The recorded response is replayed verbatim rather than answering with a
    /// bare 200. A retry has to be indistinguishable from the original for the
    /// client's cache to end up correct: the second attempt gets the same 201 and
    /// the same created id, so the optimistic row in the UI is reconciled against
    /// the real one instead of being orphaned.
    /// </para>
    /// </summary>
    public sealed class IdempotencyFilter : IAsyncActionFilter
    {
        /// <summary>The de-facto standard header (IETF draft-ietf-httpapi-idempotency-key-header).</summary>
        public const string HeaderName = "Idempotency-Key";

        /// <summary>Set on a replayed response so a client — and a test — can tell.</summary>
        public const string ReplayedHeader = "Idempotency-Replayed";

        private const int MaxKeyLength = 100;

        private readonly IIdempotencyStore _store;
        private readonly ILogger<IdempotencyFilter> _logger;
        private readonly JsonSerializerOptions _json;

        public IdempotencyFilter(
            IIdempotencyStore store,
            ILogger<IdempotencyFilter> logger,
            IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
        {
            _store = store;
            _logger = logger;
            // The same options MVC would have used, so a replayed body is byte-for-byte
            // what the original response was.
            _json = jsonOptions.Value.JsonSerializerOptions;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;

            if (!request.Headers.TryGetValue(HeaderName, out var values)
                || values.Count == 0
                || string.IsNullOrWhiteSpace(values[0]))
            {
                // No key, no contract. The ordinary online path goes straight through.
                await next();
                return;
            }

            var key = values[0]!.Trim();
            if (key.Length > MaxKeyLength)
            {
                context.Result = new BadRequestObjectResult($"{HeaderName} must be at most {MaxKeyLength} characters.");
                return;
            }

            var endpoint = $"{request.Method} {request.Path.Value}";
            var claim = await _store.TryClaimAsync(key, endpoint, context.HttpContext.RequestAborted);

            switch (claim.Claim)
            {
                case IdempotencyClaim.AlreadyCompleted:
                    _logger.LogInformation("Replaying idempotent {Endpoint} for key {Key}.", endpoint, key);
                    context.HttpContext.Response.Headers[ReplayedHeader] = "true";
                    context.Result = new ContentResult
                    {
                        Content = claim.ResponseBody,
                        ContentType = "application/json",
                        StatusCode = claim.StatusCode
                    };
                    return;

                case IdempotencyClaim.InFlight:
                    // The first attempt is still running. Telling the client to come
                    // back is the only honest answer: succeeding would be a lie and
                    // running concurrently would be the duplicate this exists to stop.
                    context.Result = new ObjectResult("That request is still being processed.")
                    {
                        StatusCode = StatusCodes.Status409Conflict
                    };
                    return;

                case IdempotencyClaim.EndpointMismatch:
                    context.Result = new BadRequestObjectResult(
                        $"That {HeaderName} was already used for a different request.");
                    return;
            }

            var executed = await next();

            // An exception is handled by GlobalExceptionHandler further out, so the
            // status here is not yet known — but the write did not apply, so the key
            // must be released either way.
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                await _store.AbandonAsync(key, CancellationToken.None);
                return;
            }

            var statusCode = StatusCode(executed.Result);

            if (statusCode is >= 200 and < 300)
            {
                await _store.CompleteAsync(key, statusCode.Value, Body(executed.Result), CancellationToken.None);
            }
            else
            {
                // A rejected write applied nothing, so there is nothing to be
                // idempotent about — and pinning the key would stop the client from
                // ever retrying the corrected request under the same queued id.
                await _store.AbandonAsync(key, CancellationToken.None);
            }
        }

        private static int? StatusCode(IActionResult? result) => result switch
        {
            null => null,
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => StatusCodes.Status200OK
        };

        private string? Body(IActionResult? result) => result switch
        {
            ObjectResult { Value: not null } objectResult =>
                JsonSerializer.Serialize(objectResult.Value, objectResult.Value.GetType(), _json),
            _ => null
        };
    }
}
