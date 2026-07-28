using System.Globalization;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [Authorize]
    public abstract class BaseController<TEntity, TDto> : ControllerBase
        where TEntity : BaseEntity, new()
        where TDto : class
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly IUserRelatedRepository<TEntity> _repository;

        protected BaseController(IUnitOfWork unitOfWork, IUserRelatedRepository<TEntity> repository)
        {
            _unitOfWork = unitOfWork;
            _repository = repository;
        }

        protected abstract TDto ToDto(TEntity entity);
        protected abstract TEntity ToEntity(TDto dto);
        protected abstract void ApplyUpdates(TEntity entity, TDto dto);

        // Ownership check for reference ids the client supplies inside the DTO
        // (AccountId, CategoryId, ...). Returns an error message when a
        // reference does not resolve to a row owned by the current user, null
        // when all references are valid. The message must not distinguish
        // "exists but foreign" from "does not exist".
        protected virtual Task<string?> ValidateReferencesAsync(TDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        /// <summary>
        /// Order a list response is served in. A window is only meaningful over a
        /// deterministic order, and ties on the sort key would otherwise let the
        /// same row appear on two pages. Controllers whose entity carries a
        /// business date override this to lead with it.
        /// </summary>
        protected virtual IOrderedQueryable<TEntity> OrderForListing(IQueryable<TEntity> query) =>
            query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id);

        /// <summary>Writes the unwindowed row count onto the current response.</summary>
        protected void SetTotalCount(int totalCount) =>
            Response.Headers[PageQuery.TotalCountHeader] = totalCount.ToString(CultureInfo.InvariantCulture);

        [HttpGet]
        public virtual async Task<IActionResult> GetAll([FromQuery] PageQuery page, CancellationToken cancellationToken)
        {
            if (page.Validate() is string error) return BadRequest(error);

            var (entities, totalCount) = await _repository.GetUserDataPageAsync(
                page.Skip, page.Take, OrderForListing, cancellationToken);

            SetTotalCount(totalCount);

            var dtos = entities.Select(ToDto).ToList();
            return Ok(dtos);
        }

        [HttpGet("{id:guid}")]
        public virtual async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();
            return Ok(ToDto(entity));
        }

        [HttpPost]
        public virtual async Task<IActionResult> Create([FromBody] TDto dto, CancellationToken cancellationToken)
        {
            var referenceError = await ValidateReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            var entity = ToEntity(dto);
            await _repository.AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveAsync(cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public virtual async Task<IActionResult> Update(Guid id, [FromBody] TDto dto, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            var referenceError = await ValidateReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return BadRequest(referenceError);

            ApplyUpdates(entity, dto);
            _repository.UpdateUserData(entity);
            await _unitOfWork.SaveAsync(cancellationToken);

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public virtual async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _repository.GetUserDataByIdAsync(id, cancellationToken);
            if (entity == null) return NotFound();

            _repository.DeleteUserData(entity);
            await _unitOfWork.SaveAsync(cancellationToken);
            return NoContent();
        }
    }
}
