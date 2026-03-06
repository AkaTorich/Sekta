using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sekta.Server.Services;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Controllers;

[ApiController]
[Route("api/stickers")]
[Authorize]
public class StickersController : ControllerBase
{
    private readonly IStickerService _stickerService;

    public StickersController(IStickerService stickerService)
    {
        _stickerService = stickerService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// GET api/stickers/packs — list all sticker packs
    /// </summary>
    [HttpGet("packs")]
    public async Task<ActionResult<List<StickerPackDto>>> GetAllPacks()
    {
        var packs = await _stickerService.GetAllPacks();
        return Ok(packs);
    }

    /// <summary>
    /// GET api/stickers/packs/{packId} — get pack with stickers
    /// </summary>
    [HttpGet("packs/{packId:guid}")]
    public async Task<ActionResult<StickerPackDto>> GetPack(Guid packId)
    {
        var pack = await _stickerService.GetPack(packId);
        if (pack is null)
            return NotFound();

        return Ok(pack);
    }

    /// <summary>
    /// POST api/stickers/packs — create pack (title)
    /// </summary>
    [HttpPost("packs")]
    public async Task<ActionResult<StickerPackDto>> CreatePack(CreateStickerPackRequest request)
    {
        var pack = await _stickerService.CreatePack(GetUserId(), request.Title);
        return Ok(pack);
    }

    /// <summary>
    /// POST api/stickers/packs/{packId}/stickers — add sticker (imageUrl)
    /// </summary>
    [HttpPost("packs/{packId:guid}/stickers")]
    public async Task<ActionResult<StickerDto>> AddSticker(Guid packId, AddStickerRequest request)
    {
        var sticker = await _stickerService.AddSticker(packId, request.ImageUrl);
        return Ok(sticker);
    }

    /// <summary>
    /// DELETE api/stickers/packs/{packId} — delete pack (owner only)
    /// </summary>
    [HttpDelete("packs/{packId:guid}")]
    public async Task<IActionResult> DeletePack(Guid packId)
    {
        await _stickerService.DeletePack(packId, GetUserId());
        return Ok();
    }
}

public record CreateStickerPackRequest(string Title);
public record AddStickerRequest(string ImageUrl);
