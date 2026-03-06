using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IPushNotificationService _pushNotificationService;

    public NotificationsController(IPushNotificationService pushNotificationService)
    {
        _pushNotificationService = pushNotificationService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceDto dto)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await _pushNotificationService.RegisterDevice(userId, dto.Token, dto.Platform);

        return Ok(new { message = "Device registered successfully" });
    }
}
