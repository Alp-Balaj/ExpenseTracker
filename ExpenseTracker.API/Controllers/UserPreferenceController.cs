using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/user/preferences")]
public class UserPreferencesController : ControllerBase
{
    private readonly UserPreferencesService _service;

    public UserPreferencesController(UserPreferencesService service)
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
    public async Task<IActionResult> Update(UserPreferences prefs)
    {
        var userId = User.FindFirst("sub")!.Value;
        prefs.UserId = userId;

        await _service.UpdateAsync(prefs);
        return NoContent();
    }
}


