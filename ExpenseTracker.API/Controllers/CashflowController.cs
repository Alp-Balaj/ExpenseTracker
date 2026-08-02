using ExpenseTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class CashflowController : ControllerBase
    {
        private readonly ICashflowProjectionService _service;

        public CashflowController(ICashflowProjectionService service)
        {
            _service = service;
        }

        /// <summary>
        /// The liquid balance projected day by day over the next month: current
        /// balances, minus the everyday burn rate, with every planned bill and
        /// income applied on its due date.
        /// </summary>
        [HttpGet("projection")]
        public async Task<IActionResult> GetProjection(CancellationToken cancellationToken)
        {
            var projection = await _service.GetProjectionAsync(cancellationToken);
            return Ok(projection);
        }
    }
}
