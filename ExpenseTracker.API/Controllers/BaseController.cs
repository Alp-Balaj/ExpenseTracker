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
        protected virtual Task<string?> ValidateReferencesAsync(TDto dto) =>
            Task.FromResult<string?>(null);


        [HttpGet]
        public virtual async Task<IActionResult> GetAll()
        {
            var entities = await _repository.GetAllUserDataAsync();
            var dtos = entities.Select(ToDto).ToList();
            return Ok(dtos);
        }

        [HttpGet("{id:guid}")]
        public virtual async Task<IActionResult> Get(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();
            return Ok(ToDto(entity));
        }

        [HttpPost]
        public virtual async Task<IActionResult> Create([FromBody] TDto dto)
        {
            var referenceError = await ValidateReferencesAsync(dto);
            if (referenceError != null) return BadRequest(referenceError);

            var entity = ToEntity(dto);
            await _repository.AddAsync(entity);
            await _unitOfWork.SaveAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public virtual async Task<IActionResult> Update(Guid id, [FromBody] TDto dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            var referenceError = await ValidateReferencesAsync(dto);
            if (referenceError != null) return BadRequest(referenceError);

            ApplyUpdates(entity, dto);
            _repository.UpdateUserData(entity);
            await _unitOfWork.SaveAsync();

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public virtual async Task<IActionResult> Delete(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            _repository.DeleteUserData(entity);
            await _unitOfWork.SaveAsync();
            return NoContent();
        }
    }
}