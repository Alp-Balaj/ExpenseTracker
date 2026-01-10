using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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


        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var entities = await _repository.GetAllUserDataAsync();
            var dtos = entities.Select(ToDto).ToList();
            return Ok(dtos);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();
            return Ok(ToDto(entity));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TDto dto)
        {
            var entity = ToEntity(dto);
            await _repository.AddAsync(entity);
            await _unitOfWork.SaveAsync();

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] TDto dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            ApplyUpdates(entity, dto);
            _repository.UpdateUserData(entity);
            await _unitOfWork.SaveAsync();

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            _repository.DeleteUserData(entity);
            await _unitOfWork.SaveAsync();
            return NoContent();
        }
    }
}