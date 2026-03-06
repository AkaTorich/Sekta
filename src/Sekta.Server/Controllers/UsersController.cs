using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe()
    {
        var user = await _userService.GetUser(GetUserId());
        return Ok(user);
    }

    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await _userService.UpdateProfile(GetUserId(), dto);
        return Ok(user);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<UserSearchResultDto>>> Search([FromQuery] string q)
    {
        var results = await _userService.SearchUsers(q);
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetUser(Guid id)
    {
        try
        {
            var user = await _userService.GetUser(id);
            return Ok(user);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("contacts")]
    public async Task<ActionResult<List<UserDto>>> GetContacts()
    {
        var contacts = await _userService.GetContacts(GetUserId());
        return Ok(contacts);
    }

    [HttpPost("contacts/{contactId:guid}")]
    public async Task<IActionResult> AddContact(Guid contactId)
    {
        await _userService.AddContact(GetUserId(), contactId);
        return Ok();
    }

    [HttpDelete("contacts/{contactId:guid}")]
    public async Task<IActionResult> RemoveContact(Guid contactId)
    {
        await _userService.RemoveContact(GetUserId(), contactId);
        return Ok();
    }
}
