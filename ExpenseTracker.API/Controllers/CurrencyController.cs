using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Mapping;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CurrencyController : BaseController<Currency, CurrencyDTO>
    {
        private readonly IExchangeRateUpdater _rateUpdater;

        public CurrencyController(
            IUnitOfWork uow,
            IUserRelatedRepository<Currency> repo,
            IExchangeRateUpdater rateUpdater)
            : base(uow, repo)
        {
            _rateUpdater = rateUpdater;
        }

        protected override CurrencyDTO ToDto(Currency entity) => entity.ToDto();
        protected override Currency ToEntity(CurrencyDTO dto) => dto.ToEntity();
        protected override void ApplyUpdates(Currency entity, CurrencyDTO dto) => entity.Apply(dto);
        protected CurrencyDropdownDTO ToDropdownDto(Currency entity) => entity.ToDropdownDto();

        // Feeds a <select>: never windowed, and small by nature.
        [HttpGet("Dropdown")]
        public virtual async Task<IActionResult> GetAllDropdown(CancellationToken cancellationToken)
        {
            var entities = await _repository.GetAllUserDataAsync(cancellationToken);
            var dtos = entities.Select(ToDropdownDto).ToList();
            return Ok(dtos);
        }

        /// <summary>
        /// Refreshes this user's exchange rates from the upstream immediately, instead of
        /// waiting for the daily sweep. Always 200: an unreachable upstream is a normal
        /// outcome that leaves every stored rate untouched, and the response says so via
        /// <c>providerUnavailable</c> rather than by failing.
        /// </summary>
        [HttpPost("RefreshRates")]
        public async Task<IActionResult> RefreshRates(CancellationToken cancellationToken)
        {
            var userId = _repository.GetCurrentUserId();
            var result = await _rateUpdater.UpdateUserAsync(userId, cancellationToken);

            var currencies = await _repository.GetAllUserDataAsync(cancellationToken);

            return Ok(new
            {
                result.BaseCurrency,
                RatesAsOf = result.RatesAsOf?.ToString("yyyy-MM-dd"),
                result.UpdatedCount,
                result.UnsupportedCodes,
                result.ProviderUnavailable,
                Currencies = currencies.Select(ToDto).ToList()
            });
        }
    }


}
