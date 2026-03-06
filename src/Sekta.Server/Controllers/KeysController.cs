using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/keys")]
[Authorize]
public class KeysController : ControllerBase
{
    // In-memory store for public keys.
    // In a production system this should be replaced with a persistent store.
    private static readonly ConcurrentDictionary<Guid, string> PublicKeys = new();

    /// <summary>
    /// Upload (or update) the authenticated user's public key.
    /// </summary>
    [HttpPost]
    public ActionResult UploadPublicKey([FromBody] PublicKeyDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        if (dto.UserId != userId.Value)
            return BadRequest(new { message = "UserId in the payload must match the authenticated user." });

        PublicKeys[userId.Value] = dto.PublicKeyBase64;

        return Ok(new { message = "Public key stored successfully." });
    }

    /// <summary>
    /// Retrieve the public key for the given user.
    /// </summary>
    [HttpGet("{userId:guid}")]
    public ActionResult<PublicKeyDto> GetPublicKey(Guid userId)
    {
        if (!PublicKeys.TryGetValue(userId, out var publicKeyBase64))
            return NotFound(new { message = "Public key not found for the requested user." });

        return Ok(new PublicKeyDto(userId, publicKeyBase64));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.FindFirst("sub");

        if (claim is null || !Guid.TryParse(claim.Value, out var userId))
            return null;

        return userId;
    }
}
