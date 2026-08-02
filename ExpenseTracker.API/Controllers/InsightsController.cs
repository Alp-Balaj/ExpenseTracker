using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class InsightsController : ControllerBase
    {
        private readonly IInsightsService _service;

        public InsightsController(IInsightsService service)
        {
            _service = service;
        }

        /// <summary>
        /// The current month's spending measured against recent history —
        /// month-over-month category deltas, the trailing three-month average
        /// and the biggest movers, all for the dashboard's insight sentences.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var insights = await _service.GetInsightsAsync(cancellationToken);
            return Ok(insights);
        }
    }
}
