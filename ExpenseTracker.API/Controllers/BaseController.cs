using AutoMapper;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BaseController<TEntity, TDto> : ControllerBase
        where TEntity : BaseEntity, new()
        where TDto : class
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IUserRelatedRepository<TEntity> _repository;

        public BaseController(IUnitOfWork unitOfWork, IMapper mapper, IUserRelatedRepository<TEntity> repository)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _repository = repository;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var entities = await _repository.GetAllUserDataAsync();
            var dtos = _mapper.Map<IEnumerable<TDto>>(entities);
            return Ok(dtos);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();
            return Ok(_mapper.Map<TDto>(entity));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TDto dto)
        {
            var entity = _mapper.Map<TEntity>(dto);
            await _repository.AddAsync(entity);
            await _unitOfWork.SaveAsync();
            return CreatedAtAction(nameof(Get), new { id = (entity as dynamic).Id }, dto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] TDto dto)
        {
            var entity = await _repository.GetUserDataByIdAsync(id);
            if (entity == null) return NotFound();

            _mapper.Map(dto, entity);
            _repository.UpdateUserData(entity);
            await _unitOfWork.SaveAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
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
