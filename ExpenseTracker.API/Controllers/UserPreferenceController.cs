using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserPreferencesController : ControllerBase
{
    private readonly IUserPreferencesService _service;

    public UserPreferencesController(IUserPreferencesService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.FindFirst("sub")!.Value;
        var prefs = await _service.GetAsync();
        return Ok(prefs);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateUserPreferencesDTO dto)
    {
        await _service.UpdateAsync(new UserPreferences
        {
            Theme = dto.Theme,
            BaseCurrency = dto.BaseCurrency
        });

        return NoContent();
    }
}